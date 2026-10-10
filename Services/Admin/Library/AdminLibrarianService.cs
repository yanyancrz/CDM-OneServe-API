using System.Security.Cryptography;

using CDM_OneServe_API.Data;
using CDM_OneServe_API.DTOs.Admin.Library.Common;
using CDM_OneServe_API.DTOs.Admin.Library.Librarians;
using CDM_OneServe_API.Interfaces.Admin.Library;
using CDM_OneServe_API.Models;

using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Services.Admin.Library;

public class AdminLibrarianService : IAdminLibrarianService
{
    private const string StaffRole = "LibraryStaff";
    private const string ModuleName = "Library";
    private const string ActiveStatus = "Active";
    private const string DisabledStatus = "Suspended";
    private const string DeletedStatus = "Deleted";

    private const int ActivityLimit = 30;
    private const int MinPasswordLength = 8;

    // No 0/O/1/l/I, so a printed temporary password is easy to read.
    private const string PasswordAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789";

    private readonly AppDbContext _db;
    private readonly IAdminLibraryActivityLogger _activity;

    public AdminLibrarianService(AppDbContext db, IAdminLibraryActivityLogger activity)
    {
        _db = db;
        _activity = activity;
    }

    // Only LibraryStaff accounts that are not deleted. Nothing else is reachable from here.
    private IQueryable<User> Staff() =>
        _db.Users.Where(u => u.Role == StaffRole && (u.AccountStatus == null || u.AccountStatus != DeletedStatus));

    // Projection only: the password hash is never selected.
    private static IQueryable<LibrarianDto> Project(IQueryable<User> query) =>
        query.Select(u => new LibrarianDto
        {
            UserId = u.Id,
            IdNumber = u.IdNumber,
            FullName = u.FullName,
            Email = u.Email,
            Role = u.Role,
            AccountStatus = u.AccountStatus,
            IsDisabled = u.AccountStatus == DisabledStatus,
            CreatedAt = (DateTime?)u.CreatedAt
        });

    private Task<LibrarianDto?> FindDtoAsync(int id, CancellationToken cancellationToken) =>
        Project(Staff().AsNoTracking().Where(u => u.Id == id)).FirstOrDefaultAsync(cancellationToken);

    // =====================================================
    // LIST / DETAILS
    // =====================================================

    public async Task<PagedResult<LibrarianDto>> GetLibrariansAsync(LibrarianFilterDto f, CancellationToken cancellationToken)
    {
        var page = Math.Max(f.Page, 1);
        var pageSize = Math.Clamp(f.PageSize, 1, 100);

        var query = Staff().AsNoTracking();

        if (!string.IsNullOrWhiteSpace(f.Search))
        {
            var s = f.Search.Trim();
            query = query.Where(u =>
                u.FullName.Contains(s) ||
                (u.IdNumber != null && u.IdNumber.Contains(s)) ||
                (u.Email != null && u.Email.Contains(s)));
        }

        query = (f.Status ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "active" => query.Where(u => u.AccountStatus != DisabledStatus),
            "disabled" => query.Where(u => u.AccountStatus == DisabledStatus),
            _ => query
        };

        var total = await query.CountAsync(cancellationToken);

        var items = await Project(query.OrderBy(u => u.FullName).Skip((page - 1) * pageSize).Take(pageSize))
            .ToListAsync(cancellationToken);

        return new PagedResult<LibrarianDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalItems = total,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize)
        };
    }

    public async Task<LibraryResult<LibrarianDto>> GetLibrarianAsync(int id, CancellationToken cancellationToken)
    {
        var dto = await FindDtoAsync(id, cancellationToken);

        return dto == null
            ? LibraryResult<LibrarianDto>.Failure(404, "Library Staff account not found.")
            : LibraryResult<LibrarianDto>.Success(dto);
    }

    // =====================================================
    // CREATE
    // =====================================================

    public async Task<LibraryResult<LibrarianDto>> CreateAsync(CreateLibrarianDto dto, int headId, CancellationToken cancellationToken)
    {
        var first = dto.FirstName?.Trim() ?? string.Empty;
        var last = dto.LastName?.Trim() ?? string.Empty;
        var idNumber = dto.IdNumber?.Trim() ?? string.Empty;
        var email = dto.Email?.Trim() ?? string.Empty;
        var password = dto.Password ?? string.Empty;

        if (first.Length == 0) return LibraryResult<LibrarianDto>.Failure(400, "First name is required.");
        if (last.Length == 0) return LibraryResult<LibrarianDto>.Failure(400, "Last name is required.");
        if (idNumber.Length == 0) return LibraryResult<LibrarianDto>.Failure(400, "Employee ID is required.");

        var fullName = $"{first} {last}";

        var invalid = ValidateCommon(fullName, idNumber, email) ?? ValidatePassword(password);
        if (invalid != null) return LibraryResult<LibrarianDto>.Failure(400, invalid);

        var duplicate = await FindDuplicateAsync(email, idNumber, null, cancellationToken);
        if (duplicate != null) return LibraryResult<LibrarianDto>.Failure(409, duplicate);

        // Same fields the existing "Add module head" uses. The role is fixed here.
        var user = new User
        {
            IdNumber = idNumber,
            FullName = fullName,
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            Role = StaffRole,
            AdminModule = ModuleName,
            IsVerified = true,
            IsProfileComplete = true,
            AccountStatus = ActiveStatus
        };

        _db.Users.Add(user);
        _activity.Add(headId, "LIBRARY_LIBRARIAN_CREATED", $"created the Library Staff account of {fullName}");

        await _db.SaveChangesAsync(cancellationToken);

        return LibraryResult<LibrarianDto>.Success((await FindDtoAsync(user.Id, cancellationToken))!, 201);
    }

    // =====================================================
    // UPDATE
    // =====================================================

    public async Task<LibraryResult<LibrarianDto>> UpdateAsync(int id, UpdateLibrarianDto dto, int headId, CancellationToken cancellationToken)
    {
        var fullName = dto.FullName?.Trim() ?? string.Empty;
        var idNumber = dto.IdNumber?.Trim() ?? string.Empty;
        var email = dto.Email?.Trim() ?? string.Empty;

        if (fullName.Length == 0) return LibraryResult<LibrarianDto>.Failure(400, "Full name is required.");
        if (idNumber.Length == 0) return LibraryResult<LibrarianDto>.Failure(400, "Employee ID is required.");

        var invalid = ValidateCommon(fullName, idNumber, email);
        if (invalid != null) return LibraryResult<LibrarianDto>.Failure(400, invalid);

        var user = await Staff().FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (user == null) return LibraryResult<LibrarianDto>.Failure(404, "Library Staff account not found.");

        var duplicate = await FindDuplicateAsync(email, idNumber, id, cancellationToken);
        if (duplicate != null) return LibraryResult<LibrarianDto>.Failure(409, duplicate);

        user.FullName = fullName;
        user.IdNumber = idNumber;
        user.Email = email;

        _activity.Add(headId, "LIBRARY_LIBRARIAN_UPDATED", $"updated the Library Staff account of {fullName}");

        await _db.SaveChangesAsync(cancellationToken);

        return LibraryResult<LibrarianDto>.Success((await FindDtoAsync(id, cancellationToken))!);
    }

    // =====================================================
    // DISABLE / ENABLE
    // =====================================================

    public Task<LibraryResult<LibrarianDto>> DisableAsync(int id, int headId, CancellationToken cancellationToken) =>
        SetStatusAsync(id, headId, DisabledStatus, "LIBRARY_LIBRARIAN_DISABLED", "disabled", cancellationToken);

    public Task<LibraryResult<LibrarianDto>> EnableAsync(int id, int headId, CancellationToken cancellationToken) =>
        SetStatusAsync(id, headId, ActiveStatus, "LIBRARY_LIBRARIAN_ENABLED", "re-enabled", cancellationToken);

    private async Task<LibraryResult<LibrarianDto>> SetStatusAsync(
        int id, int headId, string newStatus, string logCode, string verb, CancellationToken cancellationToken)
    {
        var user = await Staff().FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (user == null) return LibraryResult<LibrarianDto>.Failure(404, "Library Staff account not found.");

        if (user.AccountStatus == newStatus)
            return LibraryResult<LibrarianDto>.Failure(409, $"This account is already {(newStatus == DisabledStatus ? "disabled" : "active")}.");

        user.AccountStatus = newStatus;

        _activity.Add(headId, logCode, $"{verb} the Library Staff account of {user.FullName}");

        await _db.SaveChangesAsync(cancellationToken);

        return LibraryResult<LibrarianDto>.Success((await FindDtoAsync(id, cancellationToken))!);
    }

    // =====================================================
    // RESET PASSWORD
    // =====================================================

    public async Task<LibraryResult<ResetPasswordResultDto>> ResetPasswordAsync(int id, int headId, CancellationToken cancellationToken)
    {
        var user = await Staff().FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (user == null) return LibraryResult<ResetPasswordResultDto>.Failure(404, "Library Staff account not found.");

        var temporary = GeneratePassword();

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(temporary);

        // The password itself is never written to the log.
        _activity.Add(headId, "LIBRARY_LIBRARIAN_PASSWORD_RESET", $"reset the password of {user.FullName}");

        await _db.SaveChangesAsync(cancellationToken);

        return LibraryResult<ResetPasswordResultDto>.Success(new ResetPasswordResultDto { TemporaryPassword = temporary });
    }

    // =====================================================
    // ACTIVITY
    // =====================================================

    public async Task<LibraryResult<List<LibrarianActivityDto>>> GetActivityAsync(int id, CancellationToken cancellationToken)
    {
        if (!await Staff().AsNoTracking().AnyAsync(u => u.Id == id, cancellationToken))
            return LibraryResult<List<LibrarianActivityDto>>.Failure(404, "Library Staff account not found.");

        var rows = await _db.AdminActivityLogs.AsNoTracking()
            .Where(l => l.UserId == id && l.Action.StartsWith("LIBRARY_"))
            .OrderByDescending(l => l.Timestamp)
            .Take(ActivityLimit)
            .Select(l => new { l.Id, l.Action, l.Timestamp })
            .ToListAsync(cancellationToken);

        var items = rows.Select(r =>
        {
            var raw = r.Action ?? string.Empty;
            var idx = raw.IndexOf(':');

            return new LibrarianActivityDto
            {
                Id = r.Id,
                Code = idx > 0 ? raw[..idx].Trim() : raw.Trim(),
                Message = idx > 0 ? raw[(idx + 1)..].Trim() : string.Empty,
                CreatedAt = r.Timestamp
            };
        }).ToList();

        return LibraryResult<List<LibrarianActivityDto>>.Success(items);
    }

    // =====================================================
    // HELPERS
    // =====================================================

    private static string? ValidateCommon(string fullName, string idNumber, string email)
    {
        if (fullName.Length > 150) return "Name must be 150 characters or fewer.";
        if (idNumber.Length > 50) return "Employee ID must be 50 characters or fewer.";

        if (email.Length == 0) return "Email is required.";
        if (email.Length > 150) return "Email must be 150 characters or fewer.";

        var at = email.IndexOf('@');
        if (at < 1 || email.LastIndexOf('.') < at + 2 || email.EndsWith('.') || email.Contains(' '))
            return "Enter a valid email address.";

        return null;
    }

    private static string? ValidatePassword(string password)
    {
        if (password.Length < MinPasswordLength)
            return $"The password must be at least {MinPasswordLength} characters.";

        if (password.Length > 72)
            return "The password must be 72 characters or fewer.";

        if (!password.Any(char.IsLetter) || !password.Any(char.IsDigit))
            return "The password must contain at least one letter and one number.";

        return null;
    }

    // Returns a message when the email or ID number belongs to another account.
    private async Task<string?> FindDuplicateAsync(string email, string idNumber, int? exceptId, CancellationToken cancellationToken)
    {
        var emailLower = email.ToLower();
        var normalizedId = idNumber.Replace(" ", "").ToUpper();

        var emailTaken = await _db.Users.AsNoTracking().AnyAsync(x =>
            (exceptId == null || x.Id != exceptId) &&
            x.Email != null && x.Email.Trim().ToLower() == emailLower &&
            (x.AccountStatus == null || x.AccountStatus != DeletedStatus), cancellationToken);

        if (emailTaken) return "This email is already being used by another account.";

        var idTaken = await _db.Users.AsNoTracking().AnyAsync(x =>
            (exceptId == null || x.Id != exceptId) &&
            x.IdNumber != null && x.IdNumber.Replace(" ", "").ToUpper() == normalizedId &&
            (x.AccountStatus == null || x.AccountStatus != DeletedStatus), cancellationToken);

        return idTaken ? "This ID number is already being used by another account." : null;
    }

    // Cryptographic RNG. Always contains at least one letter and one number.
    private static string GeneratePassword()
    {
        while (true)
        {
            var chars = new char[12];

            for (var i = 0; i < chars.Length; i++)
                chars[i] = PasswordAlphabet[RandomNumberGenerator.GetInt32(PasswordAlphabet.Length)];

            var text = new string(chars);

            if (text.Any(char.IsLetter) && text.Any(char.IsDigit)) return text;
        }
    }
}