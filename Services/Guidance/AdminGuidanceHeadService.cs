using System.Globalization;

using CDM_OneServe_API.Authorization;
using CDM_OneServe_API.Data;
using CDM_OneServe_API.DTOs.Guidance;
using CDM_OneServe_API.DTOs.Guidance.Admin;
using CDM_OneServe_API.Models;
using CDM_OneServe_API.Models.Guidance;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Services.Guidance;

/// <summary>Paged envelope, same shape the Library admin module returns.</summary>
public record GuidancePage<T>(List<T> Items, int Page, int TotalPages, int TotalItems, int PageSize);

/// <summary>
/// Everything the Guidance Head portal does: access check, dashboard KPIs,
/// counselor accounts, the read-only student directory, reports and settings.
///
/// Data source is the EXISTING tables - Users (Role = Counselor / Student /
/// Faculty), guidance_* tables and user_activities. Nothing here creates a
/// second user system.
/// </summary>
public class AdminGuidanceHeadService
{
    private static readonly string[] AccountStatuses = { "Active", "Pending", "Rejected", "Suspended" };

    private static readonly string[] DayLabels =
        { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" };

    private readonly AppDbContext _context;
    private readonly GuidanceSettingsService _settings;
    private readonly EmailService _email;
    private readonly ILogger<AdminGuidanceHeadService> _logger;

    public AdminGuidanceHeadService(
        AppDbContext context,
        GuidanceSettingsService settings,
        EmailService email,
        ILogger<AdminGuidanceHeadService> logger)
    {
        _context = context;
        _settings = settings;
        _email = email;
        _logger = logger;
    }

    // =====================================================
    // ACCESS  (GET /api/admin/guidance/me)
    // =====================================================
    public async Task<GuidanceResult<GuidanceHeadAccessDto>> GetMeAsync(
        int userId, string? tokenRole, CancellationToken ct)
    {
        var user = await _context.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, ct);

        if (user == null)
            return GuidanceResult<GuidanceHeadAccessDto>.Fail(401,
                "Your account could not be found. Please log in again.");

        // The token must carry a Guidance-head role AND the live row must too,
        // so a revoked role takes effect before the token expires.
        if (!IsHeadRole(tokenRole) || !IsHeadRole(user.Role))
            return GuidanceResult<GuidanceHeadAccessDto>.Fail(403,
                "This account does not have access to the Guidance Administration module.");

        if (user.AccountStatus != "Active")
            return GuidanceResult<GuidanceHeadAccessDto>.Fail(403,
                "Your account is not active. Please contact the administrator.");

        return GuidanceResult<GuidanceHeadAccessDto>.Ok(new GuidanceHeadAccessDto
        {
            UserId = user.Id,
            FullName = user.FullName,
            Email = user.Email,
            Role = user.Role,
            RoleLabel = GuidanceRoles.IsHead(user.Role) ? "Guidance Head" : "Administrator",
            AccountStatus = user.AccountStatus,
            AdminModule = user.AdminModule,
            LastLoginAt = await LastLoginAsync(user.Id, ct),
            Permissions = new Dictionary<string, bool>
            {
                ["canManageCounselors"] = true,
                ["canViewStudents"] = true,
                ["canViewReports"] = true,
                ["canManageSettings"] = true
            }
        });
    }

    private static bool IsHeadRole(string? role) =>
        role == GuidanceRoles.Head || role == "SuperAdmin" || role == "Admin";

    // =====================================================
    // ACCOUNT  (POST /api/admin/guidance/change-password)
    // The account is ALWAYS the one in the JWT - no userId in the request,
    // so a Guidance Head can only ever change their own password.
    // =====================================================
    public async Task<GuidanceResult<object>> ChangeOwnPasswordAsync(
        int userId,
        GuidanceChangePasswordRequest request,
        DateTime? currentTokenIssuedAt,
        CancellationToken ct)
    {
        var current = request.CurrentPassword ?? "";
        var next = request.NewPassword ?? "";

        if (current.Length == 0)
            return GuidanceResult<object>.Fail(400, "Current password is required.");

        if (next.Length == 0)
            return GuidanceResult<object>.Fail(400, "New password is required.");

        if (next.Length < 8)
            return GuidanceResult<object>.Fail(400, "New password must be at least 8 characters.");

        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == userId, ct);

        if (user == null)
            return GuidanceResult<object>.Fail(401,
                "Your account could not be found. Please log in again.");

        // A blank or non-BCrypt hash would make BCrypt.Verify throw, so it is
        // treated as "wrong password" instead of a 500.
        var hasHash = !string.IsNullOrWhiteSpace(user.PasswordHash)
                      && user.PasswordHash.StartsWith("$2", StringComparison.Ordinal);

        if (!hasHash || !BCrypt.Net.BCrypt.Verify(current, user.PasswordHash))
            return GuidanceResult<object>.Fail(400, "Current password is incorrect.");

        if (BCrypt.Net.BCrypt.Verify(next, user.PasswordHash))
            return GuidanceResult<object>.Fail(400,
                "New password cannot be the same as your current password.");

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(next);

        // End every session issued before this one; the caller's own token
        // is kept alive by stamping just before its iat.
        TokenRevocation.Stamp(user, currentTokenIssuedAt);

        _context.UserActivities.Add(new UserActivity
        {
            UserId = user.Id,
            Action = "Changed password",
            Description = "You changed your account password.",
            CreatedAt = DateTime.Now
        });

        await _context.SaveChangesAsync(ct);

        return GuidanceResult<object>.Ok(new { }, "Password changed successfully.");
    }

    // =====================================================
    // DASHBOARD  (GET /api/admin/guidance/dashboard/overview)
    // =====================================================
    public async Task<GuidanceHeadOverviewDto> GetOverviewAsync(CancellationToken ct)
    {
        var counselors = await _context.Users.AsNoTracking()
            .Where(u => u.Role == GuidanceRoles.Counselor && u.AccountStatus != "Deleted")
            .Select(u => new { u.Id, u.AccountStatus, u.CreatedAt })
            .ToListAsync(ct);

        var counselorIds = counselors.Select(c => c.Id).ToList();
        var signedIn = await SignedInUserIdsAsync(ct);

        var monthStart = new DateOnly(GuidanceClock.Today.Year, GuidanceClock.Today.Month, 1);

        var students = await _context.Users.AsNoTracking()
            .Where(u => (u.Role == GuidanceRoles.Student || u.Role == GuidanceRoles.Faculty)
                        && u.AccountStatus != "Deleted")
            .Select(u => new { u.Id, u.AccountStatus, u.CreatedAt })
            .ToListAsync(ct);

        var today = GuidanceClock.Today;
        var appointments = await _context.GuidanceAppointments.AsNoTracking()
            .Select(a => new { a.Status, a.Date, a.CreatedAt })
            .ToListAsync(ct);

        var sessions = await _context.GuidanceCounselingRecords.AsNoTracking()
            .Select(r => new { r.Status, r.IsConfidential, r.CreatedAt })
            .ToListAsync(ct);

        var pendingFollowUps = await _context.GuidanceFollowUps.AsNoTracking()
            .CountAsync(f => f.Status == "Pending", ct);

        var conversations = await _context.GuidanceChatConversations.AsNoTracking()
            .Select(c => new { c.Id, c.LastMessageAt })
            .ToListAsync(ct);

        var dayAgo = DateTime.UtcNow.AddHours(-24);
        var messages = await _context.GuidanceChatMessages.AsNoTracking()
            .Select(m => new { m.CreatedAt, m.ReadAt })
            .ToListAsync(ct);

        var overview = new GuidanceHeadOverviewDto
        {
            Counselors = new GuidanceCounselorStatDto
            {
                Total = counselors.Count,
                Active = counselors.Count(c => c.AccountStatus == "Active"),
                Inactive = counselors.Count(c => c.AccountStatus != "Active"),
                NeverSignedIn = counselorIds.Count(id => !signedIn.Contains(id))
            },
            Students = new GuidanceStudentStatDto
            {
                Total = students.Count,
                Active = students.Count(s => s.AccountStatus == "Active"),
                Pending = students.Count(s => s.AccountStatus == "Pending"),
                NewThisMonth = students.Count(s => DateOnly.FromDateTime(s.CreatedAt) >= monthStart),
                NeverSignedIn = students.Count(s => !signedIn.Contains(s.Id))
            },
            Appointments = new GuidanceAppointmentStatDto
            {
                Total = appointments.Count,
                Pending = appointments.Count(a => a.Status == "Pending"),
                Confirmed = appointments.Count(a => a.Status == "Confirmed"),
                Completed = appointments.Count(a => a.Status == "Completed"),
                Cancelled = appointments.Count(a => a.Status == "Cancelled"),
                Rejected = appointments.Count(a => a.Status == "Rejected"),
                Expired = appointments.Count(a => a.Status == "Expired"),
                Today = appointments.Count(a => a.Date == today)
            },
            Sessions = new GuidanceSessionStatDto
            {
                Records = sessions.Count,
                Draft = sessions.Count(s => s.Status == "Draft"),
                Finalized = sessions.Count(s => s.Status == "Finalized"),
                Confidential = sessions.Count(s => s.IsConfidential),
                FollowUpsPending = pendingFollowUps
            },
            Chat = new GuidanceChatStatDto
            {
                Conversations = conversations.Count,
                Messages = messages.Count,
                MessagesLast24Hours = messages.Count(m => m.CreatedAt >= dayAgo),
                UnreadMessages = messages.Count(m => m.ReadAt == null)
            }
        };

        overview.RecentActivity = await BuildRecentActivityAsync(ct);

        return overview;
    }

    private async Task<List<GuidanceActivityDto>> BuildRecentActivityAsync(CancellationToken ct)
    {
        var items = new List<GuidanceActivityDto>();

        var appointments = await _context.GuidanceAppointments.AsNoTracking()
            .Include(a => a.Student)
            .Include(a => a.Counselor)
            .OrderByDescending(a => a.CreatedAt)
            .Take(6)
            .ToListAsync(ct);

        foreach (var a in appointments)
        {
            items.Add(new GuidanceActivityDto
            {
                Kind = "appointment",
                Title = "Appointment request",
                Detail = $"{a.Student?.FullName ?? "A student"} → {a.Counselor?.FullName ?? "unassigned"} · {a.Status}",
                At = GuidanceClock.FromUtc(a.CreatedAt)
            });
        }

        var records = await _context.GuidanceCounselingRecords.AsNoTracking()
            .Include(r => r.Counselor)
            .OrderByDescending(r => r.UpdatedAt)
            .Take(6)
            .ToListAsync(ct);

        foreach (var r in records)
        {
            items.Add(new GuidanceActivityDto
            {
                Kind = "session",
                Title = "Counseling session",
                Detail = $"{r.Counselor?.FullName ?? "Counselor"} · {r.Status}",
                At = GuidanceClock.FromUtc(r.UpdatedAt)
            });
        }

        var logins = await _context.UserActivities.AsNoTracking()
            .Where(a => a.Action == UserActivityActions.Login)
            .OrderByDescending(a => a.CreatedAt)
            .Take(6)
            .ToListAsync(ct);

        var userIds = logins.Select(l => l.UserId).Distinct().ToList();
        var names = await _context.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FullName })
            .ToListAsync(ct);
        var nameById = names.ToDictionary(n => n.Id, n => n.FullName);

        foreach (var l in logins)
        {
            items.Add(new GuidanceActivityDto
            {
                Kind = "account",
                Title = "Signed in",
                Detail = nameById.GetValueOrDefault(l.UserId, $"User #{l.UserId}"),
                At = l.CreatedAt
            });
        }

        var messages = await _context.GuidanceChatMessages.AsNoTracking()
            .OrderByDescending(m => m.CreatedAt)
            .Take(6)
            .ToListAsync(ct);

        foreach (var m in messages)
        {
            var preview = m.Body.Length > 60 ? m.Body[..60] + "…" : m.Body;
            items.Add(new GuidanceActivityDto
            {
                Kind = "chat",
                Title = "Chat message",
                Detail = preview,
                At = GuidanceClock.FromUtc(m.CreatedAt)
            });
        }

        return items.OrderByDescending(i => i.At).Take(8).ToList();
    }

    // =====================================================
    // COUNSELORS
    // =====================================================
    public async Task<GuidancePage<GuidanceCounselorListItemDto>> GetCounselorsAsync(
        GuidanceCounselorFilterDto filter, CancellationToken ct)
    {
        var page = Math.Max(filter.Page, 1);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);

        var query = _context.Users.AsNoTracking()
            .Where(u => u.Role == GuidanceRoles.Counselor && u.AccountStatus != "Deleted");

        var search = filter.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            var term = search.ToLower();
            query = query.Where(u =>
                u.FullName.ToLower().Contains(term) ||
                u.Email.ToLower().Contains(term) ||
                u.IdNumber.ToLower().Contains(term));
        }

        var status = filter.Status?.Trim();
        if (!string.IsNullOrEmpty(status))
            query = query.Where(u => u.AccountStatus == status);

        var total = await query.CountAsync(ct);

        var users = await query
            .OrderBy(u => u.FullName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var items = await DecorateCounselorsAsync(users, ct);

        return new GuidancePage<GuidanceCounselorListItemDto>(
            items, page, (int)Math.Ceiling(total / (double)pageSize), total, pageSize);
    }

    public async Task<GuidanceResult<GuidanceCounselorDetailDto>> GetCounselorAsync(int id, CancellationToken ct)
    {
        var user = await _context.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == id && u.Role == GuidanceRoles.Counselor, ct);

        if (user == null)
            return GuidanceResult<GuidanceCounselorDetailDto>.Fail(404, "Counselor not found.");

        var list = await DecorateCounselorsAsync(new List<User> { user }, ct);
        var base3 = list[0];

        var profile = await _context.GuidanceCounselorProfiles.AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == id, ct);

        var availability = await _context.GuidanceAvailabilities.AsNoTracking()
            .Where(a => a.CounselorId == id)
            .OrderBy(a => a.DayOfWeek).ThenBy(a => a.StartTime)
            .ToListAsync(ct);

        var appts = await _context.GuidanceAppointments.AsNoTracking()
            .Where(a => a.CounselorId == id)
            .Select(a => new { a.Status, a.Date })
            .ToListAsync(ct);

        return GuidanceResult<GuidanceCounselorDetailDto>.Ok(new GuidanceCounselorDetailDto
        {
            UserId = base3.UserId,
            IdNumber = base3.IdNumber,
            FullName = base3.FullName,
            Email = base3.Email,
            ProfilePicture = base3.ProfilePicture,
            AccountStatus = base3.AccountStatus,
            Title = base3.Title,
            Room = base3.Room,
            Department = base3.Department,
            Phone = base3.Phone,
            Specializations = base3.Specializations,
            HasBio = base3.HasBio,
            AvailabilityDays = base3.AvailabilityDays,
            Appointments = base3.Appointments,
            CompletedSessions = base3.CompletedSessions,
            LastLoginAt = base3.LastLoginAt,
            CreatedAt = base3.CreatedAt,
            Bio = profile?.Bio,
            Availability = availability.Select(a => new GuidanceAvailabilitySlotDto
            {
                DayOfWeek = a.DayOfWeek,
                DayLabel = DayLabel(a.DayOfWeek),
                StartTime = GuidanceClock.FormatSlot((int)a.StartTime.TotalMinutes),
                EndTime = GuidanceClock.FormatSlot((int)a.EndTime.TotalMinutes),
                IsActive = a.IsActive
            }).ToList(),
            AppointmentsByStatus = new GuidanceAppointmentStatDto
            {
                Total = appts.Count,
                Pending = appts.Count(a => a.Status == "Pending"),
                Confirmed = appts.Count(a => a.Status == "Confirmed"),
                Completed = appts.Count(a => a.Status == "Completed"),
                Cancelled = appts.Count(a => a.Status == "Cancelled"),
                Rejected = appts.Count(a => a.Status == "Rejected"),
                Expired = appts.Count(a => a.Status == "Expired")
            }
        });
    }

    public async Task<GuidanceResult<GuidanceCounselorListItemDto>> CreateCounselorAsync(
        CreateCounselorRequest request, CancellationToken ct)
    {
        var email = request.Email.Trim();
        var idNumber = request.IdNumber.Trim().Replace(" ", "").ToUpperInvariant();
        var emailKey = email.ToLower();

        if (await _context.Users.AnyAsync(
                u => u.Email.ToLower() == emailKey && u.AccountStatus != "Deleted", ct))
            return GuidanceResult<GuidanceCounselorListItemDto>.Fail(400, "That email is already registered.");

        if (await _context.Users.AnyAsync(
                u => u.IdNumber == idNumber && u.AccountStatus != "Deleted", ct))
            return GuidanceResult<GuidanceCounselorListItemDto>.Fail(400, "That ID number is already registered.");

        var user = new User
        {
            IdNumber = idNumber,
            FullName = request.FullName.Trim(),
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            Role = GuidanceRoles.Counselor,
            AdminModule = "Guidance",
            IsVerified = true,
            IsProfileComplete = true,   // counselors never see the student profile-setup page
            AccountStatus = "Active"
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync(ct);   // need user.Id for the FK below

        _context.GuidanceCounselorProfiles.Add(new GuidanceCounselorProfile
        {
            UserId = user.Id,
            Title = "Registered Guidance Counselor",
            Room = "Guidance and Counseling Center",
            Phone = request.Phone,
            Department = request.Department,
            Specializations = JoinSpecializations(request.Specializations)
        });
        await _context.SaveChangesAsync(ct);

        var created = await DecorateCounselorsAsync(new List<User> { user }, ct);

        return GuidanceResult<GuidanceCounselorListItemDto>.Created(created[0], "Counselor account created.");
    }

    public async Task<GuidanceResult<GuidanceCounselorListItemDto>> UpdateCounselorAsync(
        int id, UpdateCounselorRequest request, CancellationToken ct)
    {
        var user = await _context.Users.FirstOrDefaultAsync(
            u => u.Id == id && u.Role == GuidanceRoles.Counselor, ct);

        if (user == null)
            return GuidanceResult<GuidanceCounselorListItemDto>.Fail(404, "Counselor not found.");

        var email = request.Email.Trim();
        var emailKey = email.ToLower();

        if (await _context.Users.AnyAsync(
                u => u.Id != id && u.Email.ToLower() == emailKey && u.AccountStatus != "Deleted", ct))
            return GuidanceResult<GuidanceCounselorListItemDto>.Fail(400, "That email is already registered.");

        var emailChanged = !string.Equals(
            user.Email,
            email,
            StringComparison.OrdinalIgnoreCase);

        user.FullName = request.FullName.Trim();
        user.Email = email;
        user.IsProfileComplete = true;

        if (emailChanged)
        {
            // An admin changed this counselor's identity. End all of the
            // counselor's sessions; the counselor is not the signed-in caller.
            TokenRevocation.Stamp(user, null);
        }

        var profile = await _context.GuidanceCounselorProfiles
            .FirstOrDefaultAsync(p => p.UserId == id, ct);

        if (profile == null)
        {
            profile = new GuidanceCounselorProfile { UserId = id };
            _context.GuidanceCounselorProfiles.Add(profile);
        }

        profile.Title = "Registered Guidance Counselor";
        profile.Room = "Guidance and Counseling Center";
        profile.Department = NullIfBlank(request.Department);
        profile.Phone = NullIfBlank(request.Phone);
        profile.Bio = NullIfBlank(request.Bio);
        profile.Specializations = JoinSpecializations(request.Specializations);
        profile.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(ct);

        var decorated = await DecorateCounselorsAsync(new List<User> { user }, ct);

        return GuidanceResult<GuidanceCounselorListItemDto>.Ok(decorated[0], "Counselor updated.");
    }

    public async Task<GuidanceResult<object>> UploadCounselorPhotoAsync(
        int id, IFormFile file, CancellationToken ct)
    {
        if (file == null || file.Length == 0)
            return GuidanceResult<object>.Fail(400, "No file uploaded.");

        var allowed = new[] { ".jpg", ".jpeg", ".png", ".webp" };
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!allowed.Contains(extension))
            return GuidanceResult<object>.Fail(400, "Only JPG, PNG, or WEBP images are allowed.");

        if (file.Length > 5 * 1024 * 1024)
            return GuidanceResult<object>.Fail(400, "Image must be 5 MB or smaller.");

        var user = await _context.Users.FirstOrDefaultAsync(
            u => u.Id == id && u.Role == GuidanceRoles.Counselor, ct);

        if (user == null)
            return GuidanceResult<object>.Fail(404, "Counselor not found.");

        var uploadPath = Path.Combine(
            Directory.GetCurrentDirectory(), "wwwroot", "uploads", "profile");

        if (!Directory.Exists(uploadPath))
            Directory.CreateDirectory(uploadPath);

        var fileName = Guid.NewGuid() + extension;
        var filePath = Path.Combine(uploadPath, fileName);

        await using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream, ct);
        }

        user.ProfilePicture = $"/uploads/profile/{fileName}";
        await _context.SaveChangesAsync(ct);

        return GuidanceResult<object>.Ok(
            new { imageUrl = user.ProfilePicture }, "Profile picture updated.");
    }

    public async Task<GuidanceResult<object>> SetCounselorStatusAsync(
        int id, string status, CancellationToken ct)
    {
        var normalized = AccountStatuses
            .FirstOrDefault(s => string.Equals(s, status, StringComparison.OrdinalIgnoreCase));

        if (normalized == null)
            return GuidanceResult<object>.Fail(400,
                "Status must be one of: Active, Suspended, Pending, Rejected.");

        var user = await _context.Users.FirstOrDefaultAsync(
            u => u.Id == id && u.Role == GuidanceRoles.Counselor, ct);

        if (user == null)
            return GuidanceResult<object>.Fail(404, "Counselor not found.");

        if (user.AccountStatus == normalized)
            return GuidanceResult<object>.Ok(new { }, $"Account is already {normalized}.");

        user.AccountStatus = normalized;
        await _context.SaveChangesAsync(ct);

        return GuidanceResult<object>.Ok(new { }, $"Account {normalized.ToLowerInvariant()}.");
    }

    /// <summary>
    /// Same flow the public forgot-password uses: an OTP is emailed to the
    /// counselor, who picks their own new password. The Head never sees it.
    /// </summary>
    public async Task<GuidanceResult<object>> SendPasswordResetAsync(int id, CancellationToken ct)
    {
        var user = await _context.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == id && u.Role == GuidanceRoles.Counselor, ct);

        if (user == null)
            return GuidanceResult<object>.Fail(404, "Counselor not found.");

        var otp = Random.Shared.Next(100000, 999999).ToString();

        var existing = await _context.PasswordResetOTPs
            .FirstOrDefaultAsync(x => x.Email == user.Email, ct);

        if (existing != null)
            _context.PasswordResetOTPs.Remove(existing);

        _context.PasswordResetOTPs.Add(new PasswordResetOTP
        {
            Email = user.Email,
            OTPCode = otp,
            ExpiryDate = DateTime.Now.AddMinutes(5)
        });

        await _context.SaveChangesAsync(ct);

        try
        {
            await _email.SendOtpAsync(user.Email, otp, "forgotpassword");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Guidance Head: password reset email failed for {Email}.", user.Email);
            return GuidanceResult<object>.Fail(500,
                "The reset code could not be sent. Please try again in a moment.");
        }

        return GuidanceResult<object>.Ok(new { email = user.Email },
            $"A password reset code was sent to {user.Email}.");
    }

    // =====================================================
    // STUDENTS  (read-only directory)
    // =====================================================
    public async Task<GuidancePage<GuidanceStudentListItemDto>> GetStudentsAsync(
        GuidanceStudentFilterDto filter, CancellationToken ct)
    {
        var page = Math.Max(filter.Page, 1);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);

        var query = _context.Users.AsNoTracking()
            .Where(u => u.Role == GuidanceRoles.Student || u.Role == GuidanceRoles.Faculty);

        var role = filter.Role?.Trim();
        if (!string.IsNullOrEmpty(role))
            query = query.Where(u => u.Role == role);

        var status = filter.Status?.Trim();
        if (!string.IsNullOrEmpty(status))
            query = query.Where(u => u.AccountStatus == status);
        else
            query = query.Where(u => u.AccountStatus != "Deleted");

        var search = filter.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            var term = search.ToLower();
            query = query.Where(u =>
                u.FullName.ToLower().Contains(term) ||
                u.Email.ToLower().Contains(term) ||
                u.IdNumber.ToLower().Contains(term));
        }

        var total = await query.CountAsync(ct);

        var users = await query
            .OrderBy(u => u.FullName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var items = await DecorateStudentsAsync(users, ct);

        return new GuidancePage<GuidanceStudentListItemDto>(
            items, page, (int)Math.Ceiling(total / (double)pageSize), total, pageSize);
    }

    public async Task<GuidanceResult<GuidanceStudentDetailDto>> GetStudentAsync(int id, CancellationToken ct)
    {
        var user = await _context.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == id &&
                                      (u.Role == GuidanceRoles.Student || u.Role == GuidanceRoles.Faculty), ct);

        if (user == null)
            return GuidanceResult<GuidanceStudentDetailDto>.Fail(404, "Account not found.");

        var list = await DecorateStudentsAsync(new List<User> { user }, ct);
        var base3 = list[0];

        var appointments = await _context.GuidanceAppointments.AsNoTracking()
            .Where(a => a.StudentId == id)
            .Include(a => a.Counselor)
            .OrderByDescending(a => a.Date)
            .Take(50)
            .ToListAsync(ct);

        return GuidanceResult<GuidanceStudentDetailDto>.Ok(new GuidanceStudentDetailDto
        {
            UserId = base3.UserId,
            IdNumber = base3.IdNumber,
            FullName = base3.FullName,
            Email = base3.Email,
            Role = base3.Role,
            AccountStatus = base3.AccountStatus,
            Course = base3.Course,
            YearLevel = base3.YearLevel,
            Institute = base3.Institute,
            ProfileComplete = base3.ProfileComplete,
            Appointments = base3.Appointments,
            ChatConversations = base3.ChatConversations,
            LastLoginAt = base3.LastLoginAt,
            CreatedAt = base3.CreatedAt,
            ContactNumber = user.ContactNumber,
            StudentStatus = user.StudentStatus,
            AppointmentHistory = appointments.Select(a => new GuidanceStudentAppointmentSummaryDto
            {
                Id = a.Id,
                Date = a.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                TimeSlot = a.TimeSlot,
                ConcernType = a.ConcernType ?? "",
                Status = a.Status,
                CounselorName = a.Counselor?.FullName,
                CreatedAt = GuidanceClock.FromUtc(a.CreatedAt)
            }).ToList()
        });
    }

    // =====================================================
    // REPORTS  (GET /api/admin/guidance/reports/overview)
    // =====================================================
    public async Task<GuidanceReportsDto> GetReportsAsync(int days, CancellationToken ct)
    {
        days = days is < 1 or > 730 ? 30 : days;

        var to = GuidanceClock.Today;
        var from = to.AddDays(-days);

        var appointments = await _context.GuidanceAppointments.AsNoTracking()
            .Where(a => a.Date >= from && a.Date <= to)
            .Select(a => new { a.Date, a.Status, a.ConcernType, a.CounselorId })
            .ToListAsync(ct);

        var sessions = await _context.GuidanceCounselingRecords.AsNoTracking()
            .Where(r => r.SessionDate >= from && r.SessionDate <= to)
            .Select(r => new { r.SessionDate, r.Status, r.CounselorId })
            .ToListAsync(ct);

        var counselors = await _context.Users.AsNoTracking()
            .Where(u => u.Role == GuidanceRoles.Counselor && u.AccountStatus != "Deleted")
            .Select(u => new { u.Id, u.FullName })
            .OrderBy(u => u.FullName)
            .ToListAsync(ct);
        var counselorName = counselors.ToDictionary(c => c.Id, c => c.FullName);

        var statusOrder = new[] { "Pending", "Confirmed", "Arrived", "NoShow", "Completed", "Cancelled", "Rejected", "Expired" };

        var report = new GuidanceReportsDto
        {
            Days = days,
            From = from,
            To = to,
            AppointmentsByStatus = statusOrder
                .Select(s => new GuidanceLabelCountDto
                {
                    Label = s,
                    Count = appointments.Count(a => a.Status == s)
                })
                .ToList(),
            AppointmentsByConcern = appointments
                .Where(a => !string.IsNullOrWhiteSpace(a.ConcernType))
                .GroupBy(a => a.ConcernType!)
                .Select(g => new GuidanceLabelCountDto { Label = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .Take(8)
                .ToList(),
            AppointmentsByMonth = MonthBuckets(appointments
                .GroupBy(a => MonthKey(a.Date))
                .ToDictionary(g => g.Key, g => g.Count()), from, to),
            SessionsByMonth = MonthBuckets(sessions
                .GroupBy(s => MonthKey(s.SessionDate))
                .ToDictionary(g => g.Key, g => g.Count()), from, to),
            Counselors = counselors.Select(c => new GuidanceCounselorPerformanceDto
            {
                FullName = c.FullName,
                Appointments = appointments.Count(a => a.CounselorId == c.Id),
                Completed = appointments.Count(a => a.CounselorId == c.Id && a.Status == "Completed"),
                Sessions = sessions.Count(s => s.CounselorId == c.Id)
            }).ToList()
        };

        report.Accounts = await GetAccountActivityAsync(counselors.Select(c => c.Id).ToList(), ct);

        return report;
    }

    private static string MonthKey(DateOnly date) =>
        date.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    /// <summary>One bucket per month in the range, so charts never show gaps.</summary>
    private static List<GuidanceLabelCountDto> MonthBuckets(
        IReadOnlyDictionary<string, int> counts, DateOnly from, DateOnly to)
    {
        var buckets = new List<GuidanceLabelCountDto>();

        var cursor = new DateOnly(from.Year, from.Month, 1);
        var last = new DateOnly(to.Year, to.Month, 1);

        while (cursor <= last)
        {
            var key = MonthKey(cursor);
            buckets.Add(new GuidanceLabelCountDto
            {
                Label = cursor.ToString("MMM yyyy", CultureInfo.InvariantCulture),
                Count = counts.GetValueOrDefault(key)
            });
            cursor = cursor.AddMonths(1);
        }

        return buckets;
    }

    private async Task<GuidanceAccountActivityDto> GetAccountActivityAsync(
        List<int> counselorIds, CancellationToken ct)
    {
        var signedIn = await SignedInUserIdsAsync(ct);

        var students = await _context.Users.AsNoTracking()
            .Where(u => (u.Role == GuidanceRoles.Student || u.Role == GuidanceRoles.Faculty)
                        && u.AccountStatus != "Deleted")
            .Select(u => u.Id)
            .ToListAsync(ct);

        var since7 = DateTime.Now.AddDays(-7);
        var since30 = DateTime.Now.AddDays(-30);

        var recent7 = await _context.UserActivities.AsNoTracking()
            .Where(a => a.Action == UserActivityActions.Login && a.CreatedAt >= since7)
            .Select(a => a.UserId)
            .Distinct()
            .CountAsync(ct);

        var recent30 = await _context.UserActivities.AsNoTracking()
            .Where(a => a.Action == UserActivityActions.Login && a.CreatedAt >= since30)
            .Select(a => a.UserId)
            .Distinct()
            .CountAsync(ct);

        return new GuidanceAccountActivityDto
        {
            NeverSignedInStudents = students.Count(id => !signedIn.Contains(id)),
            NeverSignedInCounselors = counselorIds.Count(id => !signedIn.Contains(id)),
            SignedInLast7Days = recent7,
            SignedInLast30Days = recent30
        };
    }

    // =====================================================
    // SETTINGS  (GET/PUT /api/admin/guidance/settings)
    // =====================================================
    public Task<GuidanceModuleSettings> GetSettingsAsync(CancellationToken ct) =>
        _settings.GetAsync(ct);

    public Task<GuidanceModuleSettings> SaveSettingsAsync(
        GuidanceModuleSettings settings, int userId, CancellationToken ct) =>
        _settings.SaveAsync(settings, userId, ct);

    // =====================================================
    // HELPERS
    // =====================================================
    private async Task<List<GuidanceCounselorListItemDto>> DecorateCounselorsAsync(
        List<User> users, CancellationToken ct)
    {
        if (users.Count == 0) return new List<GuidanceCounselorListItemDto>();

        var ids = users.Select(u => u.Id).ToList();

        var profiles = await _context.GuidanceCounselorProfiles.AsNoTracking()
            .Where(p => ids.Contains(p.UserId))
            .ToListAsync(ct);

        var availability = await _context.GuidanceAvailabilities.AsNoTracking()
            .Where(a => ids.Contains(a.CounselorId) && a.IsActive)
            .ToListAsync(ct);

        var appointmentCounts = await _context.GuidanceAppointments.AsNoTracking()
            .Where(a => a.CounselorId != null && ids.Contains(a.CounselorId.Value))
            .GroupBy(a => a.CounselorId)
            .Select(g => new { CounselorId = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var completed = await _context.GuidanceCounselingRecords.AsNoTracking()
            .Where(r => ids.Contains(r.CounselorId))
            .GroupBy(r => r.CounselorId)
            .Select(g => new { CounselorId = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var lastLogins = await LastLoginsAsync(ids, ct);

        var profileByUser = profiles.ToDictionary(p => p.UserId);

        return users.Select(u =>
        {
            profileByUser.TryGetValue(u.Id, out var profile);

            return new GuidanceCounselorListItemDto
            {
                UserId = u.Id,
                IdNumber = u.IdNumber,
                FullName = u.FullName,
                Email = u.Email,
                ProfilePicture = u.ProfilePicture,
                AccountStatus = u.AccountStatus,
                Title = profile?.Title,
                Room = profile?.Room,
                Department = profile?.Department,
                Phone = profile?.Phone,
                Specializations = profile?.Specializations,
                HasBio = !string.IsNullOrWhiteSpace(profile?.Bio),
                AvailabilityDays = availability
                    .Where(a => a.CounselorId == u.Id)
                    .Select(a => a.DayOfWeek)
                    .Distinct()
                    .Count(),
                Appointments = appointmentCounts.FirstOrDefault(a => a.CounselorId == u.Id)?.Count ?? 0,
                CompletedSessions = completed.FirstOrDefault(c => c.CounselorId == u.Id)?.Count ?? 0,
                LastLoginAt = lastLogins.GetValueOrDefault(u.Id),
                CreatedAt = u.CreatedAt
            };
        }).ToList();
    }

    private async Task<List<GuidanceStudentListItemDto>> DecorateStudentsAsync(
        List<User> users, CancellationToken ct)
    {
        if (users.Count == 0) return new List<GuidanceStudentListItemDto>();

        var ids = users.Select(u => u.Id).ToList();

        var appointmentCounts = await _context.GuidanceAppointments.AsNoTracking()
            .Where(a => ids.Contains(a.StudentId))
            .GroupBy(a => a.StudentId)
            .Select(g => new { StudentId = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var conversationCounts = await _context.GuidanceChatConversations.AsNoTracking()
            .Where(c => ids.Contains(c.StudentId))
            .GroupBy(c => c.StudentId)
            .Select(g => new { StudentId = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var lastLogins = await LastLoginsAsync(ids, ct);

        return users.Select(u => new GuidanceStudentListItemDto
        {
            UserId = u.Id,
            IdNumber = u.IdNumber,
            FullName = u.FullName,
            Email = u.Email,
            Role = u.Role,
            AccountStatus = u.AccountStatus,
            Course = u.Course,
            YearLevel = u.YearLevel,
            Institute = u.Institute,
            ProfileComplete = u.IsProfileComplete,
            Appointments = appointmentCounts.FirstOrDefault(a => a.StudentId == u.Id)?.Count ?? 0,
            ChatConversations = conversationCounts.FirstOrDefault(c => c.StudentId == u.Id)?.Count ?? 0,
            LastLoginAt = lastLogins.GetValueOrDefault(u.Id),
            CreatedAt = u.CreatedAt
        }).ToList();
    }

    private async Task<Dictionary<int, DateTime?>> LastLoginsAsync(List<int> ids, CancellationToken ct)
    {
        var map = ids.Distinct().ToDictionary(id => id, _ => (DateTime?)null);

        if (map.Count == 0) return map;

        var rows = await _context.UserActivities.AsNoTracking()
            .Where(a => a.Action == UserActivityActions.Login && ids.Contains(a.UserId))
            .GroupBy(a => a.UserId)
            .Select(g => new { UserId = g.Key, At = g.Max(x => x.CreatedAt) })
            .ToListAsync(ct);

        foreach (var row in rows)
            map[row.UserId] = row.At;

        return map;
    }

    private async Task<DateTime?> LastLoginAsync(int userId, CancellationToken ct) =>
        await _context.UserActivities.AsNoTracking()
            .Where(a => a.Action == UserActivityActions.Login && a.UserId == userId)
            .MaxAsync(a => (DateTime?)a.CreatedAt, ct);

    /// <summary>Every account that has signed in at least once (small set of ids).</summary>
    private async Task<HashSet<int>> SignedInUserIdsAsync(CancellationToken ct)
    {
        var ids = await _context.UserActivities.AsNoTracking()
            .Where(a => a.Action == UserActivityActions.Login)
            .Select(a => a.UserId)
            .Distinct()
            .ToListAsync(ct);

        return ids.ToHashSet();
    }

    private static string? JoinSpecializations(string[]? values) =>
        values == null
            ? null
            : string.Join(",", values
                .Select(s => s?.Trim() ?? "")
                .Where(s => s.Length > 0));

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string DayLabel(int dayOfWeek) =>
        dayOfWeek >= 0 && dayOfWeek < DayLabels.Length ? DayLabels[dayOfWeek] : dayOfWeek.ToString();
}
