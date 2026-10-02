using CDM_OneServe_API.Data;
using CDM_OneServe_API.DTOs;
using CDM_OneServe_API.DTOs.Library;
using CDM_OneServe_API.Models.Library;
using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Services.Library;

public class BookService
{
    private readonly AppDbContext _context;

    public BookService(AppDbContext context)
    {
        _context = context;
    }

    // =========================================================
    // MAP BOOK -> DTO
    // =========================================================
    private static BookDto MapToDto(Book book)
    {
        return new BookDto
        {
            BookId = book.BookId,

            BookCode = book.BookCode,

            ISBN = book.ISBN,

            Title = book.Title,

            Author = book.Author,

            Publisher = book.Publisher,

            Category = book.Category,

            Institute = book.Institute,

            YearLevel = book.YearLevel,

            Semester = book.Semester,

            DDC = book.DDC,

            CallNo = book.CallNo,

            Description = book.Description,

            Synopsis = book.Synopsis,

            Language = book.Language,

            Edition = book.Edition,

            PublishYear = book.PublishYear,

            CoverImage = book.CoverImage,

            ShelfLocation = book.ShelfLocation,

            TotalCopies = book.TotalCopies,

            AvailableCopies = book.AvailableCopies,

            Status = book.Status,

            CreatedAt = book.CreatedAt,

            UpdatedAt = book.UpdatedAt
        };
    }


    // =========================================================
    // GET ALL BOOKS
    // =========================================================
    public async Task<List<BookDto>> GetAllBooksAsync()
    {
        var books = await _context.Books
            .Where(b => b.Status != "Archived")
            .OrderBy(b => b.Title)
            .ToListAsync();

        return books
            .Select(MapToDto)
            .ToList();
    }


    // =========================================================
    // GET BOOK BY ID
    // =========================================================
    public async Task<BookDto?> GetBookByIdAsync(int bookId)
    {
        var book = await _context.Books
            .FirstOrDefaultAsync(b => b.BookId == bookId);

        if (book == null)
        {
            return null;
        }

        return MapToDto(book);
    }


    // =========================================================
    // CREATE BOOK
    // =========================================================
    public async Task<ServiceResponse<BookDto>> CreateBookAsync(
        CreateBookDto dto)
    {
        var response = new ServiceResponse<BookDto>();

        // -----------------------------------------------------
        // CHECK DUPLICATE ISBN
        // -----------------------------------------------------
        var existingBook = await _context.Books
            .FirstOrDefaultAsync(b => b.ISBN == dto.ISBN);

        if (existingBook != null)
        {
            response.Success = false;
            response.Message =
                "A book with the same ISBN already exists.";

            return response;
        }


        // -----------------------------------------------------
        // CREATE BOOK
        // -----------------------------------------------------
        var book = new Book
        {
            BookCode = dto.BookCode,

            ISBN = dto.ISBN,

            Title = dto.Title,

            Author = dto.Author,

            Publisher = dto.Publisher,

            Category = dto.Category,

            Institute = dto.Institute,

            YearLevel = dto.YearLevel,

            Semester = dto.Semester,

            DDC = dto.DDC,

            CallNo = dto.CallNo,

            Description = dto.Description,

            Synopsis = dto.Synopsis,

            Language = dto.Language,

            Edition = dto.Edition,

            PublishYear = dto.PublishedYear,

            CoverImage = dto.CoverImageUrl,

            ShelfLocation = dto.ShelfLocation,

            TotalCopies = dto.TotalCopies,

            AvailableCopies = dto.TotalCopies,

            Status = string.IsNullOrWhiteSpace(dto.Status)
                ? "Available"
                : dto.Status,

            CreatedAt = DateTime.Now,

            UpdatedAt = DateTime.Now
        };


        // -----------------------------------------------------
        // SAVE
        // -----------------------------------------------------
        _context.Books.Add(book);

        await _context.SaveChangesAsync();


        // -----------------------------------------------------
        // RESPONSE
        // -----------------------------------------------------
        response.Success = true;

        response.Message = "Book added successfully.";

        response.Data = MapToDto(book);

        return response;
    }


    // =========================================================
    // UPDATE BOOK
    // =========================================================
    public async Task<ServiceResponse<BookDto>> UpdateBookAsync(
        int bookId,
        UpdateBookDto dto)
    {
        var response = new ServiceResponse<BookDto>();


        // -----------------------------------------------------
        // FIND BOOK
        // -----------------------------------------------------
        var book = await _context.Books
            .FirstOrDefaultAsync(b => b.BookId == bookId);

        if (book == null)
        {
            response.Success = false;

            response.Message = "Book not found.";

            return response;
        }


        // -----------------------------------------------------
        // CHECK DUPLICATE ISBN
        // -----------------------------------------------------
        var duplicateISBN = await _context.Books
            .FirstOrDefaultAsync(b =>
                b.ISBN == dto.ISBN &&
                b.BookId != bookId);

        if (duplicateISBN != null)
        {
            response.Success = false;

            response.Message =
                "Another book already uses this ISBN.";

            return response;
        }


        // -----------------------------------------------------
        // UPDATE BASIC INFORMATION
        // -----------------------------------------------------
        book.BookCode = dto.BookCode;

        book.ISBN = dto.ISBN;

        book.Title = dto.Title;

        book.Author = dto.Author;

        book.Publisher = dto.Publisher;

        book.Category = dto.Category;

        book.Institute = dto.Institute;

        book.YearLevel = dto.YearLevel;

        book.Semester = dto.Semester;

        book.DDC = dto.DDC;

        book.CallNo = dto.CallNo;

        book.Description = dto.Description;

        book.Synopsis = dto.Synopsis;

        book.Language = dto.Language;

        book.Edition = dto.Edition;

        book.PublishYear = dto.PublishedYear;

        book.CoverImage = dto.CoverImageUrl;

        book.ShelfLocation = dto.ShelfLocation;


        // -----------------------------------------------------
        // UPDATE COPIES
        // -----------------------------------------------------
        book.TotalCopies = dto.TotalCopies;

        if (dto.AvailableCopies > dto.TotalCopies)
        {
            book.AvailableCopies = dto.TotalCopies;
        }
        else if (dto.AvailableCopies < 0)
        {
            book.AvailableCopies = 0;
        }
        else
        {
            book.AvailableCopies = dto.AvailableCopies;
        }


        // -----------------------------------------------------
        // UPDATE STATUS
        // -----------------------------------------------------
        book.Status = string.IsNullOrWhiteSpace(dto.Status)
            ? "Available"
            : dto.Status;


        book.UpdatedAt = DateTime.Now;


        // -----------------------------------------------------
        // SAVE
        // -----------------------------------------------------
        await _context.SaveChangesAsync();


        // -----------------------------------------------------
        // RESPONSE
        // -----------------------------------------------------
        response.Success = true;

        response.Message = "Book updated successfully.";

        response.Data = MapToDto(book);

        return response;
    }


    // =========================================================
    // DELETE BOOK
    // =========================================================
    public async Task<ServiceResponse<bool>> DeleteBookAsync(
        int bookId)
    {
        var response = new ServiceResponse<bool>();


        // -----------------------------------------------------
        // FIND BOOK
        // -----------------------------------------------------
        var book = await _context.Books
            .FirstOrDefaultAsync(b => b.BookId == bookId);

        if (book == null)
        {
            response.Success = false;

            response.Message = "Book not found.";

            response.Data = false;

            return response;
        }


        // -----------------------------------------------------
        // CHECK IF CURRENTLY BORROWED
        // -----------------------------------------------------
        var isBorrowed = await _context.BorrowTransactions
            .AnyAsync(b =>
                b.BookId == bookId &&
                b.Status == "Borrowed");

        if (isBorrowed)
        {
            response.Success = false;

            response.Message =
                "This book cannot be deleted because it is currently borrowed.";

            response.Data = false;

            return response;
        }


        // -----------------------------------------------------
        // ARCHIVE INSTEAD OF PHYSICAL DELETE
        // -----------------------------------------------------
        book.Status = "Archived";

        book.UpdatedAt = DateTime.Now;

        await _context.SaveChangesAsync();


        // -----------------------------------------------------
        // RESPONSE
        // -----------------------------------------------------
        response.Success = true;

        response.Message = "Book deleted successfully.";

        response.Data = true;

        return response;
    }
}