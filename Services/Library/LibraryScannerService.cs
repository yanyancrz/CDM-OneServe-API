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

        Console.WriteLine($"[SCANNER v3] QR = {qrData}");

        User? user = null;

        // ==========================================
        // LEGACY FACULTY QR
        // Format: CDM-FACULTY:[Employee ID]:[Name]:[Department]
        // ==========================================

        if (qrData.StartsWith("CDM-FACULTY:", StringComparison.OrdinalIgnoreCase))
        {
            var parts = qrData.Split(':');

            if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[1]))
            {
                return Invalid("Invalid faculty QR format.");
            }

            user = await FindByIdNumberAsync(parts[1]);
        }

        // ==========================================
        // JSON QR (Student AND Faculty)
        // {
        //   "userId": 17,
        //   "idNumber": "23-13133",
        //   "name": "...",
        //   "role": "Faculty",
        //   "timestamp": 1790811540
        // }
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

                // 1. userId (number or string)
                if (TryGetInt(root, "userId", out var userId))
                {
                    user = await _context.Users
                        .FirstOrDefaultAsync(u => u.Id == userId);
                }

                // 2. Fallback: idNumber / studentNumber / employeeId
                if (user == null)
                {
                    var idNumber =
                        GetString(root, "idNumber") ??
                        GetString(root, "studentNumber") ??
                        GetString(root, "employeeId");

                    if (!string.IsNullOrWhiteSpace(idNumber))
                    {
                        user = await FindByIdNumberAsync(idNumber);
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
        // Only Student and Faculty accounts can borrow.
        // ==========================================

        Console.WriteLine(
            $"[SCANNER v3] user found = {user != null}, Id = {user?.Id}, Role = '{user?.Role}', IdNumber = '{user?.IdNumber}'");

        if (user == null ||
            !(string.Equals(user.Role, "Student", StringComparison.OrdinalIgnoreCase) ||
              string.Equals(user.Role, "Faculty", StringComparison.OrdinalIgnoreCase)))
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
        // ACTIVE RESERVATIONS (para sa Claim button)
        // Reserved / Pending / Approved at hindi pa expired
        // ==========================================

        var now = DateTime.Now;

        var reservedBooks = await _context.Reservations
            .Where(r =>
                r.UserId == user.Id &&
                (r.Status == "Reserved" ||
                 r.Status == "Pending" ||
                 r.Status == "Approved") &&
                r.ExpirationDate >= now)
            .OrderBy(r => r.ExpirationDate)
            .Select(r => new ScanReservationDto
            {
                ReservationId = r.ReservationId,
                BookId = r.BookId,
                BookTitle = r.Book!.Title,
                Author = r.Book.Author,
                CoverImage = r.Book.CoverImage,
                ReservationAt = r.ReservationAt,
                ExpirationDate = r.ExpirationDate,
                Status = r.Status,
                AvailableCopies = r.Book.AvailableCopies
            })
            .ToListAsync();

        Console.WriteLine(
            $"[SCANNER v3] active reservations = {reservedBooks.Count}");

        // ==========================================
        // ACCOUNT CHECKS
        // ==========================================

        if (!user.IsVerified)
        {
            return CreateResult(user, false, 0, borrowLimit,
                "Not Verified", "Account is not verified.",
                reservedBooks);
        }

        if (!user.IsProfileComplete)
        {
            return CreateResult(user, false, 0, borrowLimit,
                "Incomplete Profile", "Account profile is incomplete.",
                reservedBooks);
        }

        // The database uses "Approved"; the admin screens use "Active".
        // Both mean the account is allowed.
        var status = user.AccountStatus?.Trim() ?? "";

        var isAllowed =
            status.Equals("Active", StringComparison.OrdinalIgnoreCase) ||
            status.Equals("Approved", StringComparison.OrdinalIgnoreCase);

        if (!isAllowed)
        {
            return CreateResult(user, false, 0, borrowLimit,
                string.IsNullOrEmpty(status) ? "Unknown" : status,
                $"Account is not active (status: {status}).",
                reservedBooks);
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
                $"{activeBorrowedBooks}/{borrowLimit} books currently borrowed.",
                reservedBooks);
        }

        return CreateResult(user, true, activeBorrowedBooks, borrowLimit,
            "Cleared",
            $"Account is cleared for borrowing. " +
            $"{remainingBooks} borrowing slot(s) remaining.",
            reservedBooks);
    }

    // ==========================================
    // HELPERS
    // ==========================================

    private async Task<User?> FindByIdNumberAsync(string? rawId)
    {
        var id = NormalizeId(rawId);

        if (string.IsNullOrEmpty(id))
            return null;

        return await _context.Users
            .FirstOrDefaultAsync(u =>
                u.IdNumber != null &&
                u.IdNumber.Replace(" ", "").ToUpper() == id);
    }

    private static string NormalizeId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return "";

        return id.Trim().Replace(" ", "").ToUpperInvariant();
    }

    private static bool TryGetInt(JsonElement root, string name, out int value)
    {
        value = 0;

        if (!root.TryGetProperty(name, out var element))
            return false;

        if (element.ValueKind == JsonValueKind.Number)
            return element.TryGetInt32(out value);

        if (element.ValueKind == JsonValueKind.String)
            return int.TryParse(element.GetString(), out value);

        return false;
    }

    private static string? GetString(JsonElement root, string name)
    {
        if (root.TryGetProperty(name, out var element) &&
            element.ValueKind == JsonValueKind.String)
        {
            return element.GetString();
        }

        return null;
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
        string message,
        List<ScanReservationDto>? reservedBooks = null)
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
            Message = message,
            ReservedBooks = reservedBooks ?? new()
        };
    }
}