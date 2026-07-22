using CDM_OneServe_API.Data;
using CDM_OneServe_API.DTOs;
using CDM_OneServe_API.Models;
using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Services;

public class BorrowService
{
    private readonly AppDbContext _context;

    public BorrowService(AppDbContext context)
    {
        _context = context;
    }

    // ============================
    // Get All Borrow Transactions
    // ============================
    public async Task<List<BorrowTransactionDto>> GetAllBorrowTransactionsAsync()
    {
        return await _context.BorrowTransactions
            .Include(b => b.Book)
            .Select(b => new BorrowTransactionDto
            {
                BorrowId = b.BorrowId,
                UserId = b.UserId,
                BookId = b.BookId,
                BookTitle = b.Book!.Title,
                Author = b.Book.Author,
                CoverImage = b.Book.CoverImage,
                BorrowDate = b.BorrowDate,
                DueDate = b.DueDate,
                ReturnDate = b.ReturnDate,
                RenewalCount = b.RenewalCount,
                Status = b.Status,
                Fine = b.Fine,
                Remarks = b.Remarks
            })
            .ToListAsync();
    }

    // ============================
    // Get Borrow By Id
    // ============================
    public async Task<BorrowTransactionDto?> GetBorrowByIdAsync(int borrowId)
    {
        return await _context.BorrowTransactions
            .Include(b => b.Book)
            .Where(b => b.BorrowId == borrowId)
            .Select(b => new BorrowTransactionDto
            {
                BorrowId = b.BorrowId,
                UserId = b.UserId,
                BookId = b.BookId,
                BookTitle = b.Book!.Title,
                Author = b.Book.Author,
                CoverImage = b.Book.CoverImage,
                BorrowDate = b.BorrowDate,
                DueDate = b.DueDate,
                ReturnDate = b.ReturnDate,
                RenewalCount = b.RenewalCount,
                Status = b.Status,
                Fine = b.Fine,
                Remarks = b.Remarks
            })
            .FirstOrDefaultAsync();
    }

    // ============================
    // Borrow History
    // ============================
    public async Task<List<BorrowTransactionDto>> GetBorrowHistoryAsync(int userId)
    {
        return await _context.BorrowTransactions
            .Include(b => b.Book)
            .Where(b => b.UserId == userId)
            .OrderByDescending(b => b.BorrowDate)
            .Select(b => new BorrowTransactionDto
            {
                BorrowId = b.BorrowId,
                UserId = b.UserId,
                BookId = b.BookId,
                BookTitle = b.Book!.Title,
                Author = b.Book.Author,
                CoverImage = b.Book.CoverImage,
                BorrowDate = b.BorrowDate,
                DueDate = b.DueDate,
                ReturnDate = b.ReturnDate,
                RenewalCount = b.RenewalCount,
                Status = b.Status,
                Fine = b.Fine,
                Remarks = b.Remarks
            })
            .ToListAsync();
    }

    // ============================
    // Borrow Book
    // ============================
    public async Task<ServiceResponse<BorrowTransactionDto>> BorrowBookAsync(BorrowTransaction transaction)
    {
        var response = new ServiceResponse<BorrowTransactionDto>();

        // Check if the book exists
        var book = await _context.Books.FindAsync(transaction.BookId);

        if (book == null)
        {
            response.Success = false;
            response.Message = "Book not found.";
            return response;
        }

        // Check available copies
        if (book.AvailableCopies <= 0)
        {
            response.Success = false;
            response.Message = "Book is currently unavailable.";
            return response;
        }

        // Check if user already borrowed this book
        var existingBorrow = await _context.BorrowTransactions
            .FirstOrDefaultAsync(x =>
                x.UserId == transaction.UserId &&
                x.BookId == transaction.BookId &&
                x.Status == "Borrowed");

        if (existingBorrow != null)
        {
            response.Success = false;
            response.Message = "You have already borrowed this book.";
            return response;
        }

        // Maximum borrowed books (3)ddot
        var borrowedCount = await _context.BorrowTransactions
            .CountAsync(x =>
                x.UserId == transaction.UserId &&
                x.Status == "Borrowed");

        if (borrowedCount >= 3)
        {
            response.Success = false;
            response.Message = "Maximum borrow limit reached (3 books).";
            return response;
        }

        // Set system-generated values
        transaction.BorrowDate = DateTime.Now;
        transaction.DueDate = DateTime.Now.AddDays(7);
        transaction.ReturnDate = null;
        transaction.RenewalCount = 0;
        transaction.Status = "Borrowed";
        transaction.Fine = 0;
        transaction.CreatedAt = DateTime.Now;

        // Update available copies
        book.AvailableCopies--;

        _context.BorrowTransactions.Add(transaction);

        AddActivity(
        transaction.UserId,
        transaction.BookId,
        "Borrow",
        "Book Borrowed",
        $"You borrowed '{book.Title}'.");

        await _context.SaveChangesAsync();

        response.Success = true;
        response.Message = "Book borrowed successfully.";

        response.Data = new BorrowTransactionDto
        {
            BorrowId = transaction.BorrowId,
            UserId = transaction.UserId,
            BookId = transaction.BookId,
            BookTitle = book.Title,
            Author = book.Author,
            CoverImage = book.CoverImage,
            BorrowDate = transaction.BorrowDate,
            DueDate = transaction.DueDate,
            ReturnDate = transaction.ReturnDate,
            RenewalCount = transaction.RenewalCount,
            Status = transaction.Status,
            Fine = transaction.Fine,
            Remarks = transaction.Remarks
        };

        return response;
    }

    // ============================
    // Return Book
    // ============================
    public async Task<ServiceResponse<BorrowTransactionDto>> ReturnBookAsync(int borrowId)
    {
        var response = new ServiceResponse<BorrowTransactionDto>();

        var borrow = await _context.BorrowTransactions
            .Include(x => x.Book)
            .FirstOrDefaultAsync(x => x.BorrowId == borrowId);

        if (borrow == null)
        {
            response.Success = false;
            response.Message = "Borrow transaction not found.";
            return response;
        }

        if (borrow.Status == "Returned")
        {
            response.Success = false;
            response.Message = "This book has already been returned.";
            return response;
        }

        if (borrow.Book == null)
        {
            response.Success = false;
            response.Message = "Book record not found.";
            return response;
        }

        // Set Return Date
        borrow.ReturnDate = DateTime.Now;

        // Compute Fine (₱10/day overdue)
        decimal fine = 0;

        if (borrow.ReturnDate.Value.Date > borrow.DueDate.Date)
        {
            int overdueDays = (borrow.ReturnDate.Value.Date - borrow.DueDate.Date).Days;
            fine = overdueDays * 10;
        }

        borrow.Fine = fine;

        // Update Status
        borrow.Status = "Returned";

        // Increase Available Copies
        borrow.Book.AvailableCopies++;

        AddActivity(
        borrow.UserId,
        borrow.BookId,
        "Return",
        "Book Returned",
        $"You returned '{borrow.Book!.Title}'.");

            await _context.SaveChangesAsync();

        response.Success = true;
        response.Message = "Book returned successfully.";

        response.Data = new BorrowTransactionDto
        {
            BorrowId = borrow.BorrowId,
            UserId = borrow.UserId,
            BookId = borrow.BookId,
            BookTitle = borrow.Book.Title,
            Author = borrow.Book.Author,
            CoverImage = borrow.Book.CoverImage,
            BorrowDate = borrow.BorrowDate,
            DueDate = borrow.DueDate,
            ReturnDate = borrow.ReturnDate,
            RenewalCount = borrow.RenewalCount,
            Status = borrow.Status,
            Fine = borrow.Fine,
            Remarks = borrow.Remarks
        };

        return response;
    }

    // ============================
    // Renew Book
    // ============================
    public async Task<ServiceResponse<BorrowTransactionDto>> RenewBookAsync(int borrowId)
    {
        var response = new ServiceResponse<BorrowTransactionDto>();

        var borrow = await _context.BorrowTransactions
            .Include(x => x.Book)
            .FirstOrDefaultAsync(x => x.BorrowId == borrowId);

        if (borrow == null)
        {
            response.Success = false;
            response.Message = "Borrow transaction not found.";
            return response;
        }

        if (borrow.Status == "Returned")
        {
            response.Success = false;
            response.Message = "Returned books cannot be renewed.";
            return response;
        }

        // Cannot renew overdue books
        if (DateTime.Now.Date > borrow.DueDate.Date)
        {
            response.Success = false;
            response.Message = "This book is already overdue and cannot be renewed.";
            return response;
        }

        // Maximum of 2 renewals
        if (borrow.RenewalCount >= 2)
        {
            response.Success = false;
            response.Message = "Maximum renewal limit reached.";
            return response;
        }

        // Extend due date by 7 days
        borrow.DueDate = borrow.DueDate.AddDays(7);

        // Increase renewal count
        borrow.RenewalCount++;

        AddActivity(
         borrow.UserId,
         borrow.BookId,
         "Renew",
         "Book Renewed",
         $"You renewed '{borrow.Book!.Title}'.");

        await _context.SaveChangesAsync();

        response.Success = true;
        response.Message = "Book renewed successfully.";

        response.Data = new BorrowTransactionDto
        {
            BorrowId = borrow.BorrowId,
            UserId = borrow.UserId,
            BookId = borrow.BookId,

            BookTitle = borrow.Book!.Title,
            Author = borrow.Book.Author,
            CoverImage = borrow.Book.CoverImage,

            BorrowDate = borrow.BorrowDate,
            DueDate = borrow.DueDate,
            ReturnDate = borrow.ReturnDate,

            RenewalCount = borrow.RenewalCount,

            MaxRenewals = 2,

            CanRenew =
        borrow.RenewalCount < 2 &&
        borrow.DueDate.Date >= DateTime.Now.Date,

            RenewStatus =
        borrow.RenewalCount >= 2
            ? "Not Eligible"
            : "Eligible",

            Status = borrow.Status,
            Fine = borrow.Fine,
            Remarks = borrow.Remarks
        };

        return response;
    }

    // ============================
    // Current Borrowed Books
    // ============================
    public async Task<List<BorrowTransactionDto>> GetCurrentBorrowedBooksAsync(int userId)
    {
        return await _context.BorrowTransactions
            .Include(x => x.Book)
            .Where(x =>
                x.UserId == userId &&
                x.Status == "Borrowed")
            .OrderBy(x => x.DueDate)
            .Select(x => new BorrowTransactionDto
            {
                BorrowId = x.BorrowId,
                UserId = x.UserId,
                BookId = x.BookId,

                BookTitle = x.Book!.Title,
                Author = x.Book.Author,
                CoverImage = x.Book.CoverImage,

                BorrowDate = x.BorrowDate,
                DueDate = x.DueDate,
                ReturnDate = x.ReturnDate,

                RenewalCount = x.RenewalCount,

                MaxRenewals = 2,

                CanRenew =
        x.Status == "Borrowed" &&
        x.RenewalCount < 2 &&
        x.DueDate.Date >= DateTime.Now.Date,

                RenewStatus =
        x.Status != "Borrowed"
            ? "Not Eligible"
            : x.DueDate.Date < DateTime.Now.Date
                ? "Not Eligible"
                : x.RenewalCount >= 2
                    ? "Not Eligible"
                    : "Eligible",

                Status = x.Status,
                Fine = x.Fine,
                Remarks = x.Remarks
            })
            .ToListAsync();
    }

    // ============================
    // Overdue Books
    // ============================
    public async Task<List<BorrowTransactionDto>> GetOverdueBooksAsync()
    {
        var borrowList = await _context.BorrowTransactions
            .Include(x => x.Book)
            .Where(x =>
                x.Status == "Borrowed" &&
                x.DueDate < DateTime.Now)
            .OrderBy(x => x.DueDate)
            .ToListAsync();

        var result = borrowList.Select(x =>
        {
            var overdueDays = (DateTime.Now.Date - x.DueDate.Date).Days;

            return new BorrowTransactionDto
            {
                BorrowId = x.BorrowId,
                UserId = x.UserId,
                BookId = x.BookId,
                BookTitle = x.Book!.Title,
                Author = x.Book.Author,
                CoverImage = x.Book.CoverImage,
                BorrowDate = x.BorrowDate,
                DueDate = x.DueDate,
                ReturnDate = x.ReturnDate,
                RenewalCount = x.RenewalCount,
                Status = x.Status,
                OverdueDays = overdueDays,
                Fine = overdueDays * 10,
                Remarks = x.Remarks
            };
        }).ToList();

        return result;
    }

    private void AddActivity(
    int userId,
    int? bookId,
    string type,
    string title,
    string description)
    {
        _context.LibraryActivities.Add(new LibraryActivity
        {
            UserId = userId,
            BookId = bookId,
            ActivityType = type,
            Title = title,
            Description = description,
            CreatedAt = DateTime.Now
        });
    }
}