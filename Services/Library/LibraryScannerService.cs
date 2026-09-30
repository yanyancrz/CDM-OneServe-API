using System.Text.Json;

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
            return Invalid("No QR data was provided.");
        }

        qrData = qrData.Trim();

        User? user = null;

        // ==========================================
        // FACULTY QR
        // Format: CDM-FACULTY:[Employee ID]:[Name]:[Department]
        // ==========================================

        if (qrData.StartsWith("CDM-FACULTY:", StringComparison.OrdinalIgnoreCase))
        {
            var parts = qrData.Split(':');

            if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[1]))
            {
                return Invalid("Invalid faculty QR format.");
            }

            var employeeId = NormalizeId(parts[1]);

            user = await _context.Users
                .FirstOrDefaultAsync(u =>
                    u.IdNumber != null &&
                    u.IdNumber.Replace(" ", "").ToUpper() == employeeId &&
                    u.Role == "Faculty");
        }

        // ==========================================
        // STUDENT QR (JSON)
        // { "userId": 25, "studentNumber": "22-12345", ... }
        // ==========================================

        else
        {
            try
            {
                using var document = JsonDocument.Parse(qrData);
                var root = document.RootElement;

                if (root.ValueKind != JsonValueKind.Object)
                {
                    return Invalid("The QR code format is not recognized.");
                }

                // userId can be a number (25) or a string ("25")
                if (root.TryGetProperty("userId", out var userIdElement))
                {
                    int userId = 0;

                    var hasUserId =
                        (userIdElement.ValueKind == JsonValueKind.Number &&
                         userIdElement.TryGetInt32(out userId)) ||
                        (userIdElement.ValueKind == JsonValueKind.String &&
                         int.TryParse(userIdElement.GetString(), out userId));

                    if (hasUserId)
                    {
                        user = await _context.Users
                            .FirstOrDefaultAsync(u =>
                                u.Id == userId &&
                                u.Role == "Student");
                    }
                }

                // Fallback: studentNumber
                if (user == null &&
                    root.TryGetProperty("studentNumber", out var studentNumberElement) &&
                    studentNumberElement.ValueKind == JsonValueKind.String)
                {
                    var studentNumber =
                        NormalizeId(studentNumberElement.GetString());

                    if (!string.IsNullOrWhiteSpace(studentNumber))
                    {
                        user = await _context.Users
                            .FirstOrDefaultAsync(u =>
                                u.IdNumber != null &&
                                u.IdNumber.Replace(" ", "").ToUpper() == studentNumber &&
                                u.Role == "Student");
                    }
                }
            }
            catch (JsonException)
            {
                return Invalid("The QR code format is not recognized.");
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

        var borrowLimit = GetBorrowLimit(user);

        // ==========================================
        // ACCOUNT CHECKS
        // ==========================================

        if (!user.IsVerified)
        {
            return CreateResult(user, false, 0, borrowLimit,
                "Not Verified", "Account is not verified.");
        }

        if (!user.IsProfileComplete)
        {
            return CreateResult(user, false, 0, borrowLimit,
                "Incomplete Profile", "Account profile is incomplete.");
        }

        // FIX: the system uses "Active" (not "Approved").
        // AuthController sets: Active / Pending / Suspended / Rejected / Deleted
        // Accept both status names used by the system
        var status = user.AccountStatus?.Trim() ?? "";

        var isAllowed =
            status.Equals("Active", StringComparison.OrdinalIgnoreCase) ||
            status.Equals("Approved", StringComparison.OrdinalIgnoreCase);

        if (!isAllowed)
        {
            return CreateResult(user, false, 0, borrowLimit,
                string.IsNullOrEmpty(status) ? "Unknown" : status,
                $"Account is not active (status: {status}).");
        }

        // ==========================================
        // COUNT ACTIVE BORROWED BOOKS
        // ==========================================

        var activeBorrowedBooks =
            await _context.BorrowTransactions
                .CountAsync(b =>
                    b.UserId == user.Id &&
                    b.ReturnDate == null &&
                    (b.Status == "Borrowed" || b.Status == "Overdue"));

        var remainingBooks =
            Math.Max(0, borrowLimit - activeBorrowedBooks);

        if (activeBorrowedBooks >= borrowLimit)
        {
            return CreateResult(user, false, activeBorrowedBooks, borrowLimit,
                "Borrowing Limit Reached",
                $"Borrowing limit reached. " +
                $"{activeBorrowedBooks}/{borrowLimit} books currently borrowed.");
        }

        return CreateResult(user, true, activeBorrowedBooks, borrowLimit,
            "Cleared",
            $"Account is cleared for borrowing. " +
            $"{remainingBooks} borrowing slot(s) remaining.");
    }

    // ==========================================
    // HELPERS
    // ==========================================

    private static string NormalizeId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return "";

        return id.Trim().Replace(" ", "").ToUpperInvariant();
    }

    private static LibraryScanResultDto Invalid(string message)
    {
        return new LibraryScanResultDto
        {
            Cleared = false,
            Status = "Invalid QR",
            Message = message
        };
    }

    private static int GetBorrowLimit(User user)
    {
        return string.Equals(
            user.Role,
            "Faculty",
            StringComparison.OrdinalIgnoreCase)
            ? 5
            : 3;
    }

    private static LibraryScanResultDto CreateResult(
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
            RemainingBooks = Math.Max(0, borrowLimit - borrowedBooks),
            Status = status,
            Message = message
        };
    }
}