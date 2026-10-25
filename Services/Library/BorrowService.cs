using CDM_OneServe_API.Data;
using CDM_OneServe_API.DTOs;
using CDM_OneServe_API.DTOs.Admin.Library;
using CDM_OneServe_API.DTOs.Admin.Library.Returns;
using CDM_OneServe_API.Models;
using CDM_OneServe_API.Models.Library;
using CDM_OneServe_API.Services.Admin.Library;
using CDM_OneServe_API.Services.Notifications;
using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Services.Library;

public class BorrowService
{
    private readonly AppDbContext _context;
    private readonly IPushDispatcher _push;

    public BorrowService(AppDbContext context, IPushDispatcher push)
    {
        _context = context;
        _push = push;
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
                Remarks = b.Remarks,
                ReturnCondition = b.ReturnCondition
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
                Remarks = b.Remarks,
                ReturnCondition = b.ReturnCondition
            })
            .FirstOrDefaultAsync();
    }

    // ============================
    // Successful Borrowed Count
    // Only books successfully claimed at the library counter
    // ForClaiming is NOT included.
    // Returned books are NOT included.
    // ============================
    public async Task<int> GetSuccessfulBorrowedCountAsync(int userId)
    {
        return await _context.BorrowTransactions
            .CountAsync(x =>
                x.UserId == userId &&
                x.Status == "Borrowed"
            );
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
                Remarks = b.Remarks,
                ReturnCondition = b.ReturnCondition
            })
            .ToListAsync();
    }

    // ============================
    // Borrow Book
    // Creates a claim request first.
    // The book becomes officially borrowed after library verification.
    // ============================
    public async Task<ServiceResponse<BorrowTransactionDto>> BorrowBookAsync(
        BorrowTransaction transaction)
    {
        var response = new ServiceResponse<BorrowTransactionDto>();

        // ============================
        // CHECK BOOK
        // ============================

        var book = await _context.Books
            .FindAsync(transaction.BookId);

        if (book == null)
        {
            response.Success = false;
            response.Message = "Book not found.";
            return response;
        }


        // ============================
        // CHECK AVAILABLE COPIES
        // ============================

        if (book.AvailableCopies <= 0)
        {
            response.Success = false;
            response.Message = "Book is currently unavailable.";
            return response;
        }


        // ============================
        // CHECK EXISTING TRANSACTION
        // ============================

        var existingBorrow =
            await _context.BorrowTransactions
                .FirstOrDefaultAsync(x =>
                    x.UserId == transaction.UserId &&
                    x.BookId == transaction.BookId &&
                    (
                        x.Status == "ForClaiming" ||
                        x.Status == "Borrowed"
                    )
                );

        if (existingBorrow != null)
        {
            response.Success = false;

            response.Message =
                existingBorrow.Status == "ForClaiming"
                    ? "You already have a claim request for this book."
                    : "You have already borrowed this book.";

            return response;
        }


        // ============================
        // UNRESOLVED DAMAGED RETURN
        // The patron must replace or settle the damaged copy first.
        // ============================

        var damagedBlock = await LibraryBorrowBlock.GetBlockMessageAsync(
            _context, transaction.UserId);

        if (damagedBlock != null)
        {
            response.Success = false;
            response.Message = damagedBlock;
            return response;
        }


        // ============================
        // MAXIMUM BORROW LIMIT
        // Count both pending claim passes
        // and physically borrowed books
        // ============================

        var borrowedCount =
            await _context.BorrowTransactions
                .CountAsync(x =>
                    x.UserId == transaction.UserId &&
                    (
                        x.Status == "ForClaiming" ||
                        x.Status == "Borrowed"
                    )
                );

        if (borrowedCount >= 3)
        {
            response.Success = false;
            response.Message =
                "Maximum borrow limit reached (3 books).";

            return response;
        }


        // ============================
        // CREATE CLAIM TRANSACTION
        // ============================

        transaction.BorrowDate = DateTime.Now;

        // The last copy on the shelf is for inside-library use only. The
        // final due date is still confirmed by the librarian at the counter.
        var insideUse = book.AvailableCopies <= 1;

        transaction.DueDate = insideUse
            ? AdminBorrowingService.InsideUseDueDate(DateTime.Now)
            : DateTime.Now.AddDays(7);

        if (insideUse)
            transaction.Remarks = LibraryLoanFlags.InsideUse;

        transaction.ReturnDate = null;
        transaction.RenewalCount = 0;

        // IMPORTANT:
        // Not officially borrowed yet.
        transaction.Status = "ForClaiming";

        transaction.CreatedAt = DateTime.Now;


        // Hold one available copy
        book.AvailableCopies--;

        // Automatically update book status
        if (book.AvailableCopies <= 0)
        {
            book.AvailableCopies = 0;
            book.Status = "Unavailable";
        }
        else
        {
            book.Status = "Available";
        }


        // ============================
        // SAVE BORROW CLAIM REQUEST
        // ============================

        _context.BorrowTransactions.Add(transaction);


        // ============================
        // RECENT ACTIVITY
        // ============================

        _context.LibraryActivities.Add(
            new LibraryActivity
            {
                UserId = transaction.UserId,
                BookId = transaction.BookId,

                ActivityType = "BorrowRequest",

                Title = "Borrow Request Submitted",

                Description =
                    $"You requested to borrow '{book.Title}'. " +
                    "Present your claim pass at the library counter.",

                CreatedAt = DateTime.Now
            }
        );


        // ============================
        // NOTIFICATION
        // ============================

        _context.LibraryNotifications.Add(
            new LibraryNotification
            {
                UserId = transaction.UserId,

                Type = "borrow-claim-ready",

                Title = "Borrow Claim Pass Ready",

                Message =
                    $"Your claim pass for '{book.Title}' is ready. " +
                    "Present it at the library counter to claim the book.",

                IsRead = false,

                CreatedAt = DateTime.Now
            }
        );

        await _push.EnqueueAsync(new PushNotification(
            UserId: transaction.UserId,
            Module: "Library",
            Type: "borrow-claim-ready",
            Title: "Borrow Claim Pass Ready",
            Body: $"Your claim pass for '{book.Title}' is ready. Present it at the library counter to claim the book.",
            TargetUrl: PushRoutes.Resolve("Library", "borrow-claim-ready"),
            DedupeKey: $"borrowclaimready:{transaction.BookId}:{transaction.UserId}"));


        // ============================
        // SAVE
        // ============================

        await _context.SaveChangesAsync();


        // At this point transaction.BorrowId
        // already contains the generated ID.


        // ============================
        // RESPONSE
        // ============================

        response.Success = true;

        response.Message =
            "Borrow request submitted successfully. " +
            "Your claim pass is ready.";


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

            Remarks = transaction.Remarks
        };


        return response;
    }

    // ============================
    // Return Book
    // ============================
    private static readonly string[] ReturnConditions = { "Good", "Damaged", "Lost" };

    public async Task<ServiceResponse<BorrowTransactionDto>> ReturnBookAsync(
        int borrowId, string? condition = null)
    {
        var response = new ServiceResponse<BorrowTransactionDto>();

        var requested = string.IsNullOrWhiteSpace(condition) ? "Good" : condition.Trim();
        var chosen = ReturnConditions.FirstOrDefault(c =>
            c.Equals(requested, StringComparison.OrdinalIgnoreCase));

        if (chosen == null)
        {
            response.Success = false;
            response.Message = "Choose the book condition: Good, Damaged, or Lost.";
            return response;
        }

        var borrow = await _context.BorrowTransactions
            .Include(x => x.Book)
            .Include(x => x.User)
            .FirstOrDefaultAsync(x => x.BorrowId == borrowId);

        if (borrow == null)
        {
            response.Success = false;
            response.Message = "Borrow transaction not found.";
            return response;
        }

        if (borrow.ReturnDate != null || borrow.Status == "Returned")
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

        var now = DateTime.Now;
        var damaged = chosen == "Damaged";
        var lost = chosen == "Lost";
        var replacementDue = LibraryClock.Now.Date.AddDays(LibraryDamagedReturns.DefaultReplacementDays);

        // Set Return Date
        borrow.ReturnDate = now;
        borrow.Status = "Returned";
        borrow.ReturnCondition = chosen;

        // A late return suspends the patron for the rest of the semester.
        // Keep an existing, later ban rather than shortening it.
        if (borrow.ReturnDate.Value.Date > borrow.DueDate.Date && borrow.User != null)
        {
            var newBan = LibraryDefaults.ResolveSemesterEnd(borrow.ReturnDate.Value)
                ?? borrow.ReturnDate.Value.Date.AddDays(180);

            var current = borrow.User.BorrowBanUntil;

            if (current == null || newBan > current.Value)
                borrow.User.BorrowBanUntil = newBan;
        }

        var note = $"Returned: {chosen}";
        borrow.Remarks = string.IsNullOrWhiteSpace(borrow.Remarks)
            ? note
            : $"{borrow.Remarks} | {note}";

        if (damaged)
        {
            // The copy stays off the shelf until the patron replaces it or
            // the library repairs it. Record the deadline the library can
            // later change on the Damaged Returns page.
            borrow.Remarks = LibraryDamagedReturns.WithDueDate(borrow.Remarks, replacementDue);
        }
        else if (lost)
        {
            // The copy is gone for good: the catalog total goes down by one.
            borrow.Book.TotalCopies = Math.Max(borrow.Book.TotalCopies - 1, 0);
            borrow.Book.AvailableCopies = Math.Min(
                borrow.Book.AvailableCopies,
                borrow.Book.TotalCopies);
        }
        else
        {
            // Good: put one copy back on the shelf (never above the total).
            borrow.Book.AvailableCopies = Math.Min(
                borrow.Book.AvailableCopies + 1,
                borrow.Book.TotalCopies);
        }

        if (!damaged)
        {
            borrow.Book.Status = borrow.Book.AvailableCopies > 0
                ? "Available"
                : "Unavailable";
            borrow.Book.UpdatedAt = now;
        }

        var returnMessage = damaged
            ? $"'{borrow.Book.Title}' was returned damaged. Please return or replace it by {replacementDue:MMM d, yyyy}."
            : lost
                ? $"'{borrow.Book.Title}' was reported lost. Please coordinate with the library for its replacement."
                : $"'{borrow.Book.Title}' was returned. Thank you!";

        var notificationType = damaged ? "book-damaged" : lost ? "book-lost" : "book-returned";
        var notificationTitle = damaged
            ? "Damaged Book Returned"
            : lost
                ? "Lost Book Reported"
                : "Book Returned";

        _context.LibraryNotifications.Add(new LibraryNotification
        {
            UserId = borrow.UserId,
            Type = notificationType,
            Title = notificationTitle,
            Message = returnMessage,
            IsRead = false,
            CreatedAt = now
        });

        AddActivity(
            borrow.UserId,
            borrow.BookId,
            "Return",
            notificationTitle,
            $"You returned '{borrow.Book.Title}' ({chosen}).");

        await _context.SaveChangesAsync();

        await _push.EnqueueAsync(new PushNotification(
            UserId: borrow.UserId,
            Module: "Library",
            Type: notificationType,
            Title: notificationTitle,
            Body: returnMessage,
            TargetUrl: PushRoutes.LibraryLoans,
            DedupeKey: $"bookreturned:{borrow.BorrowId}"), CancellationToken.None);

        response.Success = true;
        response.Message = returnMessage;

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
            Remarks = borrow.Remarks,
            ReturnCondition = borrow.ReturnCondition
        };

        return response;
    }

    // ============================
    // Open damaged-return obligations for one patron
    // ============================
    public async Task<ServiceResponse<List<DamagedReturnDto>>> GetDamagedReturnsAsync(int userId)
    {
        var items = await LibraryBorrowBlock.GetOpenDamagedReturnsAsync(
            _context, userId);

        return new ServiceResponse<List<DamagedReturnDto>>
        {
            Success = true,
            Message = items.Count == 0
                ? "No damaged-book obligations."
                : "Damaged-book obligations loaded.",
            Data = items
        };
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
            Remarks = borrow.Remarks,
            ReturnCondition = borrow.ReturnCondition
        };

        return response;
    }

    // ============================
    // Current Borrowed / For Claiming Books
    // ============================
    public async Task<List<BorrowTransactionDto>> GetCurrentBorrowedBooksAsync(int userId)
    {
        return await _context.BorrowTransactions
            .Include(x => x.Book)

            .Where(x =>
                x.UserId == userId &&
                x.ReturnDate == null &&
                (
                    x.Status == "Borrowed" ||
                    x.Status == "ForClaiming"
                )
            )

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

                // ForClaiming cannot be renewed yet.
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
                Remarks = x.Remarks,
                ReturnCondition = x.ReturnCondition
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
                x.ReturnDate == null &&
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
                Remarks = x.Remarks,
                ReturnCondition = x.ReturnCondition
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