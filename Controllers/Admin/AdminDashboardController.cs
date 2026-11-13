using CDM_OneServe_API.Data;
using CDM_OneServe_API.Controllers.Admin.Platform;
using CDM_OneServe_API.Models;
using CDM_OneServe_API.Models.Platform;
using CDM_OneServe_API.Services.Admin.Platform;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Controllers.Admin;

[Route("api/admin/dashboard")]
[Authorize(Roles = "Admin")]
public class AdminDashboardController : AdminPlatformControllerBase
{
    private readonly AppDbContext _context;
    private readonly ClinicIntegrationLogger _clinic;
    private readonly SystemHealthService _health;
    private readonly SystemSettingsService _settings;

    public AdminDashboardController(
        AppDbContext context,
        ClinicIntegrationLogger clinic,
        SystemHealthService health,
        SystemSettingsService settings,
        PermissionService permissions,
        AdminAuditService audit,
        SecurityEventService security)
        : base(permissions, audit, security)
    {
        _context = context;
        _clinic = clinic;
        _health = health;
        _settings = settings;
    }

    // GET: api/admin/dashboard?period=daily|weekly|monthly
    [HttpGet]
    public async Task<IActionResult> GetDashboard(
        [FromQuery] string period = "weekly",
        CancellationToken ct = default)
    {
        var auth = await AuthorizeAsync(
            PermissionCatalog.DashboardView, PermissionLevels.View, ct);
        if (auth.Failure is not null) return auth.Failure;

        period = period.Trim().ToLowerInvariant();
        if (period is not ("daily" or "weekly" or "monthly")) period = "weekly";

        try
        {
            var users = _context.Users.AsNoTracking()
                .Where(u => u.AccountStatus != "Deleted");
            var totalUsers = await users.CountAsync(ct);
            var totalStudents = await users.CountAsync(u => u.Role == "Student", ct);
            var totalFaculty = await users.CountAsync(u => u.Role == "Faculty", ct);
            var activeUsers = await users.CountAsync(u => u.AccountStatus == "Active", ct);
            var pendingRequests = await users.CountAsync(u => u.AccountStatus == "Pending", ct);
            var inactiveUsers = totalUsers - activeUsers;

            var adminRoles = new[]
            {
                "Admin", "LibraryAdmin", "GuidanceAdmin",
                "LostFoundAdmin", "LostFoundSuperAdmin", "LostFoundSubAdmin", "ClinicAdmin"
            };
            var totalAdministrators = await users.CountAsync(u => adminRoles.Contains(u.Role), ct);

            var (start, bucketCount, bucketSize, labelFormat) = period switch
            {
                "daily" => (DateTime.Now.Date, 24, TimeSpan.FromHours(1), "HH:00"),
                "monthly" => (DateTime.Now.Date.AddDays(-29), 30, TimeSpan.FromDays(1), "MMM d"),
                _ => (DateTime.Now.Date.AddDays(-6), 7, TimeSpan.FromDays(1), "ddd")
            };
            var end = start.AddTicks(bucketSize.Ticks * bucketCount);
            var registrations = await _context.Users.AsNoTracking()
                .Where(u => u.CreatedAt >= start && u.CreatedAt < end)
                .Select(u => u.CreatedAt)
                .ToListAsync(ct);
            var registrationTrends = Enumerable.Range(0, bucketCount)
                .Select(i =>
                {
                    var bucketStart = start.AddTicks(bucketSize.Ticks * i);
                    var bucketEnd = bucketStart.Add(bucketSize);
                    return new
                    {
                        label = bucketStart.ToString(labelFormat),
                        registrations = registrations.Count(x => x >= bucketStart && x < bucketEnd)
                    };
                })
                .ToList();

            // Count each registered, non-deleted user once per service.
            var userIds = await users.Select(u => u.Id).ToListAsync(ct);
            var userIdSet = userIds.ToHashSet();

            var libraryUserIds = new HashSet<int>();
            libraryUserIds.UnionWith(await _context.BorrowTransactions.Select(x => x.UserId).Distinct().ToListAsync(ct));
            libraryUserIds.UnionWith(await _context.Reservations.Select(x => x.UserId).Distinct().ToListAsync(ct));
            libraryUserIds.UnionWith(await _context.LibraryAttendances.Select(x => x.UserId).Distinct().ToListAsync(ct));
            libraryUserIds.UnionWith(await _context.LibraryActivities.Select(x => x.UserId).Distinct().ToListAsync(ct));

            var lostFoundUserIds = new HashSet<int>();
            lostFoundUserIds.UnionWith(await _context.LostFoundReports.Select(x => x.UserId).Distinct().ToListAsync(ct));
            lostFoundUserIds.UnionWith(await _context.ClaimRecords.Select(x => x.ClaimedByUserId).Distinct().ToListAsync(ct));

            var guidanceUserIds = new HashSet<int>();
            guidanceUserIds.UnionWith(await _context.GuidanceAppointments.Select(x => x.StudentId).Distinct().ToListAsync(ct));
            guidanceUserIds.UnionWith(await _context.GuidanceCounselingRecords.Select(x => x.StudentId).Distinct().ToListAsync(ct));
            guidanceUserIds.UnionWith(await _context.GuidanceChatConversations.Select(x => x.StudentId).Distinct().ToListAsync(ct));

            // This activity is recorded only after a non-empty medical-record response.
            var clinicUserIds = await _context.UserActivities.AsNoTracking()
                .Where(x => x.Action == UserActivityActions.ClinicRecordsViewed)
                .Select(x => x.UserId).Distinct().ToListAsync(ct);

            var health = await _health.CheckAllAsync(ct);
            var clinicHealth = health.Components
                .FirstOrDefault(x => x.Component == "clinic_cms")?.Status ?? "unknown";

            var modules = new[]
            {
                new { name = "Lost & Found", icon = "🔍", status = await ModuleStatusAsync("modules.lostfound.enabled", ct), userCount = (int?)lostFoundUserIds.Count(userIdSet.Contains), activityCount = (int?)null, usageNote = "users with a report or claim", path = (string?)"/lost-found/admin" },
                new { name = "Library", icon = "📚", status = await ModuleStatusAsync("modules.library.enabled", ct), userCount = (int?)libraryUserIds.Count(userIdSet.Contains), activityCount = (int?)null, usageNote = "users who attended, borrowed, reserved or used the library", path = (string?)"/admin/library" },
                new { name = "Clinic", icon = "🏥", status = clinicHealth, userCount = (int?)clinicUserIds.Count(userIdSet.Contains), activityCount = (int?)null, usageNote = "users with confirmed medical records in OneServe", path = (string?)null },
                new { name = "Guidance", icon = "💬", status = await ModuleStatusAsync("modules.guidance.enabled", ct), userCount = (int?)guidanceUserIds.Count(userIdSet.Contains), activityCount = (int?)null, usageNote = "users with an appointment, session or chat", path = (string?)"/admin/guidance" }
            };

            var recentActivities = await _context.AdminActivityLogs.AsNoTracking()
                .OrderByDescending(x => x.Timestamp)
                .Take(10)
                .Select(x => new
                {
                    x.Id,
                    x.Action,
                    x.Module,
                    x.Category,
                    x.ActorName,
                    x.ActorRole,
                    timestamp = x.Timestamp
                })
                .ToListAsync(ct);

            return Ok(new
            {
                totalUsers,
                totalStudents,
                totalFaculty,
                activeUsers,
                inactiveUsers,
                totalAdministrators,
                pendingRequests,
                period,
                registrationTrends,
                modules,
                recentActivities,
                systemHealth = new
                {
                    overallStatus = health.OverallStatus,
                    checkedAt = health.CheckedAt,
                    components = health.Components.Select(x => new
                    {
                        x.Component,
                        x.Label,
                        x.Status,
                        x.LatencyMs,
                        x.CheckedAt
                    })
                },
                generatedAt = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Admin dashboard error: {ex.Message}");
            return StatusCode(500, new { message = "Failed to load admin dashboard." });
        }
    }

    private async Task<string> ModuleStatusAsync(string key, CancellationToken ct) =>
        await _settings.GetBoolAsync(key, ct) ? "Enabled" : "Disabled";
}
