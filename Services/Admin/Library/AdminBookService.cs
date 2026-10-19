using CDM_OneServe_API.Data;
using CDM_OneServe_API.DTOs.Admin.Library.Books;
using CDM_OneServe_API.DTOs.Admin.Library.Common;
using CDM_OneServe_API.Interfaces.Admin.Library;
using CDM_OneServe_API.Models.Library;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Services.Admin.Library;

public class AdminBookService : IAdminBookService
{
    private const long MaxCoverBytes = 5L * 1024 * 1024;
    private const long MaxPdfBytes = 50L * 1024 * 1024;

    private static readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".png", ".webp" };
    private static readonly string[] PdfExtensions = { ".pdf" };

    // Books may only belong to these three institutes.
    private static readonly string[] AllowedInstitutes = { "ICS", "ITE", "IBE" };

    private static readonly string[] AllowedInstituteNames =
    {
        "Institute of Computing Studies",
        "Institute of Teacher Education",
        "Institute of Business Education"
    };

    // Includes "Reserved", the status the mobile app actually creates.
    private static readonly string[] ActiveReservationStatuses = ReservationStatuses.Active;

    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly IAdminLibraryActivityLogger _activity;

    public AdminBookService(AppDbContext db, IWebHostEnvironment env, IAdminLibraryActivityLogger activity)
    {
        _db = db;
        _env = env;
        _activity = activity;
    }

    private string WebRoot => _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");

    // =====================================================
    // LIST
    // =====================================================

    public async Task<PagedResult<BookDto>> GetBooksAsync(BookFilterDto f, CancellationToken cancellationToken)
    {
        var page = Math.Max(f.Page, 1);
        var pageSize = Math.Clamp(f.PageSize, 1, 100);

        var query = _db.Books.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(f.Search))
        {
            var s = f.Search.Trim();
            query = query.Where(b =>
                b.Title.Contains(s) ||
                b.Author.Contains(s) ||
                b.ISBN.Contains(s) ||
                (b.CallNo != null && b.CallNo.Contains(s)) ||
                (b.DDC != null && b.DDC.Contains(s)) ||
                (b.Category != null && b.Category.Contains(s)));
        }

        if (!string.IsNullOrWhiteSpace(f.Institute)) query = query.Where(b => b.Institute == f.Institute);
        if (!string.IsNullOrWhiteSpace(f.YearLevel)) query = query.Where(b => b.YearLevel == f.YearLevel);
        if (!string.IsNullOrWhiteSpace(f.Semester)) query = query.Where(b => b.Semester == f.Semester);
        if (!string.IsNullOrWhiteSpace(f.Category)) query = query.Where(b => b.Category == f.Category);

        query = (f.Availability ?? string.Empty).ToLowerInvariant() switch
        {
            "available" => query.Where(b => b.AvailableCopies > 0),
            "borrowed" => query.Where(b => b.AvailableCopies < b.TotalCopies),
            "reserved" => query.Where(b => _db.Reservations.Any(r =>
                r.BookId == b.BookId && ActiveReservationStatuses.Contains(r.Status))),
            _ => query
        };

        query = (f.SortBy ?? string.Empty).ToLowerInvariant() switch
        {
            "borrow" => query
                .OrderByDescending(b => _db.BorrowTransactions.Count(t => t.BookId == b.BookId))
                .ThenBy(b => b.Title),
            "newest" => query.OrderByDescending(b => b.CreatedAt).ThenBy(b => b.Title),
            _ => query.OrderBy(b => b.Title)
        };

        var total = await query.CountAsync(cancellationToken);

        var items = await Project(query.Skip((page - 1) * pageSize).Take(pageSize))
            .ToListAsync(cancellationToken);

        return new PagedResult<BookDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalItems = total,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize)
        };
    }

    // =====================================================
    // GET ONE
    // =====================================================

    public async Task<LibraryResult<BookDto>> GetBookAsync(int id, CancellationToken cancellationToken)
    {
        var book = await FindDtoAsync(id, cancellationToken);

        return book == null
            ? LibraryResult<BookDto>.Failure(404, "Book not found.")
            : LibraryResult<BookDto>.Success(book);
    }

    // =====================================================
    // CREATE
    // =====================================================

    public async Task<LibraryResult<BookDto>> CreateAsync(CreateBookDto dto, int userId, CancellationToken cancellationToken)
    {
        var error = Validate(dto);
        if (error != null) return LibraryResult<BookDto>.Failure(400, error);

        var isbn = dto.ISBN.Trim();

        if (await _db.Books.AnyAsync(b => b.ISBN == isbn, cancellationToken))
            return LibraryResult<BookDto>.Failure(409, "A book with this ISBN already exists.");

        var now = DateTime.Now;

        var book = new Book { CreatedAt = now };
        Apply(book, dto);
        book.AvailableCopies = book.TotalCopies;
        book.Status = StatusFor(book);
        book.UpdatedAt = now;

        _db.Books.Add(book);
        _activity.Add(userId, "LIBRARY_BOOK_ADDED", $"added the book \"{book.Title}\"");

        // One SaveChanges = book and activity log commit together.
        await _db.SaveChangesAsync(cancellationToken);

        return LibraryResult<BookDto>.Success((await FindDtoAsync(book.BookId, cancellationToken))!, 201);
    }

    // =====================================================
    // UPDATE
    // =====================================================

    public async Task<LibraryResult<BookDto>> UpdateAsync(int id, UpdateBookDto dto, int userId, CancellationToken cancellationToken)
    {
        var error = Validate(dto);
        if (error != null) return LibraryResult<BookDto>.Failure(400, error);

        var book = await _db.Books.FirstOrDefaultAsync(b => b.BookId == id, cancellationToken);
        if (book == null) return LibraryResult<BookDto>.Failure(404, "Book not found.");

        var isbn = dto.ISBN.Trim();

        if (await _db.Books.AnyAsync(b => b.ISBN == isbn && b.BookId != id, cancellationToken))
            return LibraryResult<BookDto>.Failure(409, "Another book already uses this ISBN.");

        // Copies currently out of the shelf must stay accounted for.
        var outCopies = Math.Max(book.TotalCopies - book.AvailableCopies, 0);

        if (dto.TotalCopies < outCopies)
            return LibraryResult<BookDto>.Failure(409,
                $"Total copies cannot be lower than the {outCopies} cop{(outCopies == 1 ? "y" : "ies")} currently borrowed or reserved.");

        Apply(book, dto);
        book.AvailableCopies = book.TotalCopies - outCopies;
        book.Status = StatusFor(book);
        book.UpdatedAt = DateTime.Now;

        _activity.Add(userId, "LIBRARY_BOOK_UPDATED", $"updated the book \"{book.Title}\"");

        await _db.SaveChangesAsync(cancellationToken);

        return LibraryResult<BookDto>.Success((await FindDtoAsync(id, cancellationToken))!);
    }

    // =====================================================
    // DELETE
    // =====================================================

    public async Task<LibraryResult<bool>> DeleteAsync(int id, int userId, CancellationToken cancellationToken)
    {
        var book = await _db.Books.FirstOrDefaultAsync(b => b.BookId == id, cancellationToken);
        if (book == null) return LibraryResult<bool>.Failure(404, "Book not found.");

        var hasLoans = await _db.BorrowTransactions.AnyAsync(t => t.BookId == id, cancellationToken);
        var hasReservations = await _db.Reservations.AnyAsync(r => r.BookId == id, cancellationToken);

        if (hasLoans || hasReservations)
            return LibraryResult<bool>.Failure(409,
                "This book has borrowing or reservation records and cannot be deleted. Keeping it preserves the library history.");

        var title = book.Title;
        var cover = book.CoverImage;
        var pdf = book.PdfFile;

        _db.Books.Remove(book);
        _activity.Add(userId, "LIBRARY_BOOK_DELETED", $"deleted the book \"{title}\"");

        await _db.SaveChangesAsync(cancellationToken);

        // Files are removed only after the database change succeeded.
        DeleteFile(cover);
        DeleteFile(pdf);

        return LibraryResult<bool>.Success(true);
    }

    // =====================================================
    // UPLOADS
    // =====================================================

    public async Task<LibraryResult<BookDto>> UploadCoverAsync(int id, IFormFile file, int userId, CancellationToken cancellationToken)
    {
        var book = await _db.Books.FirstOrDefaultAsync(b => b.BookId == id, cancellationToken);
        if (book == null) return LibraryResult<BookDto>.Failure(404, "Book not found.");

        var (path, error) = await SaveFileAsync(file, "covers", ImageExtensions, MaxCoverBytes, IsImage, cancellationToken);
        if (error != null) return LibraryResult<BookDto>.Failure(400, error);

        var old = book.CoverImage;
        book.CoverImage = path;
        book.UpdatedAt = DateTime.Now;

        _activity.Add(userId, "LIBRARY_BOOK_UPDATED", $"changed the cover of \"{book.Title}\"");
        await _db.SaveChangesAsync(cancellationToken);

        DeleteFile(old);

        return LibraryResult<BookDto>.Success((await FindDtoAsync(id, cancellationToken))!);
    }

    public async Task<LibraryResult<BookDto>> UploadPdfAsync(int id, IFormFile file, int userId, CancellationToken cancellationToken)
    {
        var book = await _db.Books.FirstOrDefaultAsync(b => b.BookId == id, cancellationToken);
        if (book == null) return LibraryResult<BookDto>.Failure(404, "Book not found.");

        var (path, error) = await SaveFileAsync(file, "ebooks", PdfExtensions, MaxPdfBytes, IsPdf, cancellationToken);
        if (error != null) return LibraryResult<BookDto>.Failure(400, error);

        var old = book.PdfFile;
        book.PdfFile = path;
        book.UpdatedAt = DateTime.Now;

        _activity.Add(userId, "LIBRARY_BOOK_UPDATED", $"uploaded the e-book of \"{book.Title}\"");
        await _db.SaveChangesAsync(cancellationToken);

        DeleteFile(old);

        return LibraryResult<BookDto>.Success((await FindDtoAsync(id, cancellationToken))!);
    }

    // =====================================================
    // HELPERS
    // =====================================================

    private IQueryable<BookDto> Project(IQueryable<Book> query) =>
        query.Select(b => new BookDto
        {
            BookId = b.BookId,
            BookCode = b.BookCode,
            ISBN = b.ISBN,
            Title = b.Title,
            Author = b.Author,
            Publisher = b.Publisher,
            Category = b.Category,
            Institute = b.Institute,
            YearLevel = b.YearLevel,
            Semester = b.Semester,
            DDC = b.DDC,
            CallNo = b.CallNo,
            Description = b.Description,
            Language = b.Language,
            Edition = b.Edition,
            PublishYear = b.PublishYear,
            CoverImage = b.CoverImage,
            PdfFile = b.PdfFile,
            ShelfLocation = b.ShelfLocation,
            TotalCopies = b.TotalCopies,
            AvailableCopies = b.AvailableCopies,
            Status = b.Status,
            BorrowCount = _db.BorrowTransactions.Count(t => t.BookId == b.BookId),
            CreatedAt = b.CreatedAt,
            UpdatedAt = b.UpdatedAt
        });

    private Task<BookDto?> FindDtoAsync(int id, CancellationToken cancellationToken) =>
        Project(_db.Books.AsNoTracking().Where(b => b.BookId == id))
            .FirstOrDefaultAsync(cancellationToken);

    private static string StatusFor(Book book) => book.AvailableCopies > 0 ? "Available" : "Unavailable";

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // Empty is allowed; otherwise the institute must be ICS, ITE or IBE
    // (the short code or the full institute name).
    private static bool IsAllowedInstitute(string? institute)
    {
        if (string.IsNullOrWhiteSpace(institute)) return true;

        var value = institute.Trim();

        return AllowedInstitutes.Any(a => a.Equals(value, StringComparison.OrdinalIgnoreCase))
            || AllowedInstituteNames.Any(n => value.Contains(n, StringComparison.OrdinalIgnoreCase));
    }

    private static void Apply(Book book, CreateBookDto d)
    {
        book.BookCode = Clean(d.BookCode);
        book.ISBN = d.ISBN.Trim();
        book.Title = d.Title.Trim();
        book.Author = d.Author.Trim();
        book.Publisher = Clean(d.Publisher);
        book.Category = Clean(d.Category);
        book.Institute = Clean(d.Institute);
        book.YearLevel = Clean(d.YearLevel);
        book.Semester = Clean(d.Semester);
        book.DDC = Clean(d.DDC);
        book.CallNo = Clean(d.CallNo);
        book.Description = Clean(d.Description);
        book.Language = Clean(d.Language);
        book.Edition = Clean(d.Edition);
        book.PublishYear = d.PublishYear;
        book.ShelfLocation = Clean(d.ShelfLocation);
        book.TotalCopies = d.TotalCopies;
    }

    private static string? Validate(CreateBookDto d)
    {
        if (string.IsNullOrWhiteSpace(d.Title)) return "Title is required.";
        if (string.IsNullOrWhiteSpace(d.Author)) return "Author is required.";
        if (string.IsNullOrWhiteSpace(d.ISBN)) return "ISBN is required.";

        if (!IsAllowedInstitute(d.Institute)) return "Institute must be one of ICS, ITE, or IBE.";

        if (d.Title.Trim().Length > 255) return "Title must be 255 characters or fewer.";
        if (d.Author.Trim().Length > 255) return "Author must be 255 characters or fewer.";
        if (d.ISBN.Trim().Length > 20) return "ISBN must be 20 characters or fewer.";
        if ((d.Publisher?.Length ?? 0) > 255) return "Publisher must be 255 characters or fewer.";
        if ((d.BookCode?.Length ?? 0) > 50) return "Book code must be 50 characters or fewer.";
        if ((d.CallNo?.Length ?? 0) > 100) return "Call number must be 100 characters or fewer.";
        if ((d.DDC?.Length ?? 0) > 50) return "DDC must be 50 characters or fewer.";
        if ((d.ShelfLocation?.Length ?? 0) > 100) return "Shelf location must be 100 characters or fewer.";

        if (d.TotalCopies < 1 || d.TotalCopies > 10000)
            return "Total copies must be between 1 and 10,000.";

        if (d.PublishYear is { } year && (year < 1000 || year > DateTime.Now.Year + 1))
            return "Publish year is not valid.";

        return null;
    }

    // ----- file signatures: never trust the extension alone -----

    private static bool IsPdf(byte[] h) =>
        h.Length >= 5 && h[0] == 0x25 && h[1] == 0x50 && h[2] == 0x44 && h[3] == 0x46 && h[4] == 0x2D; // %PDF-

    private static bool IsImage(byte[] h)
    {
        if (h.Length < 12) return false;

        var jpeg = h[0] == 0xFF && h[1] == 0xD8 && h[2] == 0xFF;
        var png = h[0] == 0x89 && h[1] == 0x50 && h[2] == 0x4E && h[3] == 0x47;
        var webp = h[0] == 0x52 && h[1] == 0x49 && h[2] == 0x46 && h[3] == 0x46 &&
                   h[8] == 0x57 && h[9] == 0x45 && h[10] == 0x42 && h[11] == 0x50;

        return jpeg || png || webp;
    }

    private async Task<(string? Path, string? Error)> SaveFileAsync(
        IFormFile? file,
        string folder,
        string[] allowedExtensions,
        long maxBytes,
        Func<byte[], bool> hasValidSignature,
        CancellationToken cancellationToken)
    {
        if (file == null || file.Length == 0) return (null, "No file was uploaded.");

        if (file.Length > maxBytes)
            return (null, $"The file is too large. The maximum size is {maxBytes / 1024 / 1024} MB.");

        var extension = System.IO.Path.GetExtension(file.FileName).ToLowerInvariant();

        if (!allowedExtensions.Contains(extension))
            return (null, $"Only {string.Join(", ", allowedExtensions)} files are allowed.");

        var header = new byte[12];

        await using (var stream = file.OpenReadStream())
        {
            await stream.ReadAsync(header.AsMemory(0, header.Length), cancellationToken);
        }

        if (!hasValidSignature(header))
            return (null, "The file content does not match its type.");

        var directory = System.IO.Path.Combine(WebRoot, "library", folder);
        Directory.CreateDirectory(directory);

        // The server picks the name. The uploaded name is never used on disk.
        var fileName = $"{Guid.NewGuid():N}{extension}";
        var fullPath = System.IO.Path.Combine(directory, fileName);

        await using (var output = new FileStream(fullPath, FileMode.CreateNew))
        {
            await file.CopyToAsync(output, cancellationToken);
        }

        return ($"library/{folder}/{fileName}", null);
    }

    private void DeleteFile(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return;

        try
        {
            var root = System.IO.Path.GetFullPath(System.IO.Path.Combine(WebRoot, "library"));

            var full = System.IO.Path.GetFullPath(System.IO.Path.Combine(
                WebRoot, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar)));

            // Path traversal guard: only files inside wwwroot/library may be deleted.
            if (!full.StartsWith(root + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return;

            if (File.Exists(full)) File.Delete(full);
        }
        catch (IOException)
        {
            // A leftover file must never fail the request.
        }
    }
}