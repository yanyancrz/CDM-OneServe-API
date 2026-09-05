using CDM_OneServe_API.Data;
using CDM_OneServe_API.DTOs.Library;
using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Services.Library;

public class LibraryService
{
    private readonly AppDbContext _context;

    public LibraryService(AppDbContext context)
    {
        _context = context;
    }

    // Get All Books
    public async Task<List<BookDto>> GetBooksAsync()
    {
        return await _context.Books
            .Select(book => new BookDto
            {
                BookId = book.BookId,
                ISBN = book.ISBN,
                Title = book.Title,
                Author = book.Author,
                Publisher = book.Publisher,
                Category = book.Category,
                Description = book.Description,
                Language = book.Language,
                Edition = book.Edition,
                PublishYear = book.PublishYear,
                CoverImage = book.CoverImage,
                ShelfLocation = book.ShelfLocation,
                TotalCopies = book.TotalCopies,
                AvailableCopies = book.AvailableCopies,
                Status = book.Status
            })
            .ToListAsync();
    }

    // Get Book By Id
    public async Task<BookDto?> GetBookByIdAsync(int id)
    {
        return await _context.Books
            .Where(book => book.BookId == id)
            .Select(book => new BookDto
            {
                BookId = book.BookId,
                ISBN = book.ISBN,
                Title = book.Title,
                Author = book.Author,
                Publisher = book.Publisher,
                Category = book.Category,
                Description = book.Description,
                Language = book.Language,
                Edition = book.Edition,
                PublishYear = book.PublishYear,
                CoverImage = book.CoverImage,
                ShelfLocation = book.ShelfLocation,
                TotalCopies = book.TotalCopies,
                AvailableCopies = book.AvailableCopies,
                Status = book.Status
            })
            .FirstOrDefaultAsync();
    }
}