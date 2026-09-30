using CDM_OneServe_API.Data;
using CDM_OneServe_API.DTOs.Library;
using CDM_OneServe_API.Models;
using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Services.Library;

public class LibraryScannerService
{
    private readonly AppDbContext _context;

    public LibraryScannerService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<LibraryScanResultDto?> VerifyQrAsync(string qrData)
    {
        if (string.IsNullOrWhiteSpace(qrData))
        {
            return new LibraryScanResultDto
            {
                Cleared = false,
                Status = "Invalid QR",
                Message = "No QR data was provided."
            };
        }

        User? user = null;

        // ==========================================
        // FACULTY QR
        // Format:
        // CDM-FACULTY:[Employee ID]:[Name]:[Department]
        // ==========================================

        if (qrData.StartsWith("CDM-FACULTY:", StringComparison.OrdinalIgnoreCase))
        {
            var parts = qrData.Split(':');

            if (parts.Length < 2)
            {
                return new LibraryScanResultDto
                {
                    Cleared = false,
                    Status = "Invalid QR",
                    Message = "Invalid faculty QR format."
                };
            }

            var employeeId = parts[1].Trim();

            user = await _context.Users
                .FirstOrDefaultAsync(u =>
                    u.IdNumber == employeeId &&
                    u.Role == "Faculty");
        }

        // ==========================================
        // STUDENT QR
        // Expected JSON:
        // {
        //   "userId": 25,
        //   "studentNumber": "22-12345",
        //   ...
        // }
        // ==========================================

        else
        {
            try
            {
                using var document =
                    System.Text.Json.JsonDocument.Parse(qrData);

                var root = document.RootElement;

                // First try userId
                if (root.TryGetProperty("userId", out var userIdElement) &&
                    userIdElement.TryGetInt32(out var userId))
                {
                    user = await _context.Users
                        .FirstOrDefaultAsync(u =>
                            u.Id == userId &&
                            u.Role == "Student");
                }

                // Fallback to studentNumber
                if (user == null &&
                    root.TryGetProperty("studentNumber", out var studentNumberElement))
                {
                    var studentNumber =
                        studentNumberElement.GetString();

                    if (!string.IsNullOrWhiteSpace(studentNumber))
                    {
                        user = await _context.Users
                            .FirstOrDefaultAsync(u =>
                                u.IdNumber == studentNumber &&
                                u.Role == "Student");
                    }
                }
            }
            catch
            {
                return new LibraryScanResultDto
                {
                    Cleared = false,
                    Status = "Invalid QR",
                    Message = "The QR code format is not recognized."
                };
            }
        }

        // ==========================================
        // ACCOUNT NOT FOUND
        // ==========================================

        if (user == null)
        {
            return new LibraryScanResultDto
            {
                Cleared = false,
                Status = "Account Not Found",
                Message = "No matching library account was found."
            };
        }

        // ==========================================
        // ACCOUNT STATUS
        // ==========================================

        if (!user.IsVerified)
        {
            return CreateResult(
                user,
                false,
                0,
                GetBorrowLimit(user),
                "Not Verified",
                "Account is not verified."
            );
        }

        if (!user.IsProfileComplete)
        {
            return CreateResult(
                user,
                false,
                0,
                GetBorrowLimit(user),
                "Incomplete Profile",
                "Account profile is incomplete."
            );
        }

        if (!string.Equals(
                user.AccountStatus,
                "Approved",
                StringComparison.OrdinalIgnoreCase))
        {
            return CreateResult(
                user,
                false,
                0,
                GetBorrowLimit(user),
                user.AccountStatus,
                "Account is not currently approved."
            );
        }

        // ==========================================
        // COUNT ACTIVE BORROWED BOOKS
        // ==========================================

        var activeBorrowedBooks =
            await _context.BorrowTransactions
                .CountAsync(b =>
                    b.UserId == user.Id &&
                    b.ReturnDate == null &&
                    (
                        b.Status == "Borrowed" ||
                        b.Status == "Overdue"
                    ));

        // ==========================================
        // BORROW LIMIT
        // ==========================================

        var borrowLimit = GetBorrowLimit(user);

        var remainingBooks =
            Math.Max(0, borrowLimit - activeBorrowedBooks);

        // ==========================================
        // BORROWING LIMIT CHECK
        // ==========================================

        if (activeBorrowedBooks >= borrowLimit)
        {
            return CreateResult(
                user,
                false,
                activeBorrowedBooks,
                borrowLimit,
                "Borrowing Limit Reached",
                $"Borrowing limit reached. " +
                $"{activeBorrowedBooks}/{borrowLimit} books currently borrowed."
            );
        }

        // ==========================================
        // CLEARED
        // ==========================================

        return CreateResult(
            user,
            true,
            activeBorrowedBooks,
            borrowLimit,
            "Cleared",
            $"Account is cleared for borrowing. " +
            $"{remainingBooks} borrowing slot(s) remaining."
        );
    }

    // ==========================================
    // BORROW LIMIT
    // ==========================================

    private int GetBorrowLimit(User user)
    {
        if (string.Equals(
                user.Role,
                "Faculty",
                StringComparison.OrdinalIgnoreCase))
        {
            // Current default faculty limit.
            // Can later be moved to database/settings.
            return 5;
        }

        return 3;
    }

    // ==========================================
    // RESULT BUILDER
    // ==========================================

    private LibraryScanResultDto CreateResult(
        User user,
        bool cleared,
        int borrowedBooks,
        int borrowLimit,
        string status,
        string message)
    {
        return new LibraryScanResultDto
        {
            Cleared = cleared,

            UserId = user.Id,

            IdNumber = user.IdNumber,

            Name = user.FullName,

            Role = user.Role,

            Institute = user.Institute,

            BorrowedBooks = borrowedBooks,

            BorrowLimit = borrowLimit,

            RemainingBooks =
                Math.Max(0, borrowLimit - borrowedBooks),

            Status = status,

            Message = message
        };
    }
}