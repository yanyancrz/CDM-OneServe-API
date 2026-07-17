using CDM_OneServe_API.Data;
using CDM_OneServe_API.DTOs;
using CDM_OneServe_API.Models;
using CDMOneServe.API.DTOs.Library;
using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Services;

public class BookService
{
    private readonly AppDbContext _context;

    public BookService(AppDbContext context)
    {
        _context = context;
    }

    // ============================
    // Map Book -> DTO
    // ============================
    private static BookDto MapToDto(Book book)
    {
        return new BookDto
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
        };
    }

    // ============================
    // Get All Books
    // ============================
    public async Task<List<BookDto>> GetAllBooksAsync()
    {
        var books = await _context.Books
            .OrderBy(b => b.Title)
            .Where(b => b.Status != "Archived")
            .ToListAsync();

        return books
            .Select(MapToDto)
            .ToList();


    }

    // ============================
    // Get Book By Id
    // ============================
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

    // ============================
    // Create Book
    // ============================
    public async Task<ServiceResponse<BookDto>> CreateBookAsync(CreateBookDto dto)
    {
        var response = new ServiceResponse<BookDto>();

        // Check duplicate ISBN
        var existingBook = await _context.Books
            .FirstOrDefaultAsync(b => b.ISBN == dto.ISBN);

        if (existingBook != null)
        {
            response.Success = false;
            response.Message = "A book with the same ISBN already exists.";
            return response;
        }

        var book = new Book
        {
            ISBN = dto.ISBN,
            Title = dto.Title,
            Author = dto.Author,
            Publisher = dto.Publisher,
            Category = dto.Category,
            Description = dto.Description,
            Language = dto.Language,
            Edition = dto.Edition,
            PublishYear = dto.PublishedYear,
            CoverImage = dto.CoverImageUrl,
            ShelfLocation = dto.ShelfLocation,

            TotalCopies = dto.TotalCopies,

            // Available copies should not exceed total copies
            AvailableCopies = dto.TotalCopies,

            Status = string.IsNullOrWhiteSpace(dto.Status)
                ? "Available"
                : dto.Status,

            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };

        _context.Books.Add(book);

        await _context.SaveChangesAsync();

        response.Success = true;
        response.Message = "Book added successfully.";
        response.Data = MapToDto(book);

        return response;
    }

    // ============================
    // Update Book
    // ============================
    public async Task<ServiceResponse<BookDto>> UpdateBookAsync(int bookId, UpdateBookDto dto)
    {
        var response = new ServiceResponse<BookDto>();

        var book = await _context.Books
            .FirstOrDefaultAsync(b => b.BookId == bookId);

        if (book == null)
        {
            response.Success = false;
            response.Message = "Book not found.";
            return response;
        }

        // Check duplicate ISBN
        var duplicateISBN = await _context.Books
            .FirstOrDefaultAsync(b =>
                b.ISBN == dto.ISBN &&
                b.BookId != bookId);

        if (duplicateISBN != null)
        {
            response.Success = false;
            response.Message = "Another book already uses this ISBN.";
            return response;
        }

        book.ISBN = dto.ISBN;
        book.Title = dto.Title;
        book.Author = dto.Author;
        book.Publisher = dto.Publisher;
        book.Category = dto.Category;
        book.Description = dto.Description;
        book.Language = dto.Language;
        book.Edition = dto.Edition;
        book.PublishYear = dto.PublishedYear;
        book.CoverImage = dto.CoverImageUrl;
        book.ShelfLocation = dto.ShelfLocation;
        book.TotalCopies = dto.TotalCopies;

        // Available copies cannot exceed total copies
        if (dto.AvailableCopies > dto.TotalCopies)
        {
            book.AvailableCopies = dto.TotalCopies;
        }
        else
        {
            book.AvailableCopies = dto.AvailableCopies;
        }

        book.Status = dto.Status;
        book.UpdatedAt = DateTime.Now;

        await _context.SaveChangesAsync();

        response.Success = true;
        response.Message = "Book updated successfully.";
        response.Data = MapToDto(book);

        return response;
    }

    // ============================
    // Delete Book
    // ============================
    public async Task<ServiceResponse<bool>> DeleteBookAsync(int bookId)
    {
        var response = new ServiceResponse<bool>();

        var book = await _context.Books
            .FirstOrDefaultAsync(b => b.BookId == bookId);

        if (book == null)
        {
            response.Success = false;
            response.Message = "Book not found.";
            response.Data = false;
            return response;
        }

        // Prevent deleting books that are currently borrowed
        var isBorrowed = await _context.BorrowTransactions
            .AnyAsync(b =>
                b.BookId == bookId &&
                b.Status == "Borrowed");

        if (isBorrowed)
        {
            response.Success = false;
            response.Message = "This book cannot be deleted because it is currently borrowed.";
            response.Data = false;
            return response;
        }

        book.Status = "Archived";
        book.UpdatedAt = DateTime.Now;

        await _context.SaveChangesAsync();

        response.Success = true;
        response.Message = "Book deleted successfully.";
        response.Data = true;

        return response;
    }   
}