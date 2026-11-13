using CDM_OneServe_API.Data;
using CDM_OneServe_API.Models;

using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Services.Admin.Platform;

/// <summary>
/// Read-only oversight summaries for the four services.
///
/// These queries power the central service pages. They are deliberately
/// aggregate-first: the admin sees counts, statuses and recent activity, not
/// confidential contents. Guidance returns no narrative text, the Clinic
/// returns no medical data at all, and Lost &amp; Found returns no proof files.
/// </summary>
public class AdminServiceSummaryService
{
    private readonly AppDbContext _db;

    public AdminServiceSummaryService(AppDbContext db) => _db = db;

    // ---------------------------------------------------------
    // LIBRARY
    // ---------------------------------------------------------

    public async Task<LibraryOversight> GetLibraryAsync(CancellationToken ct = default)
    {
        var now = DateTime.Now;

        var bookTotals = await _db.Books
            .AsNoTracking()
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Titles = g.Count(),
                Copies = g.Sum(x => x.TotalCopies),
                Available = g.Sum(x => x.AvailableCopies)
            })
            .FirstOrDefaultAsync(ct);

        var statusCounts = await _db.Books
            .AsNoTracking()
            .Select(x => x.Status)
            .ToListAsync(ct);

        var activeLoans = await _db.BorrowTransactions
            .AsNoTracking()
            .CountAsync(x => x.ReturnDate == null, ct);

        var overdue = await _db.BorrowTransactions
            .AsNoTracking()
            .CountAsync(x => x.ReturnDate == null && x.DueDate < now, ct);

        var returnedThisWeek = await _db.BorrowTransactions
            .AsNoTracking()
            .CountAsync(x =>
                x.ReturnDate != null &&
                x.ReturnDate >= now.AddDays(-7), ct);

        var reservations = await _db.Reservations
            .AsNoTracking()
            .Select(x => x.Status)
            .ToListAsync(ct);

        var patronRows = await _db.BorrowTransactions
            .AsNoTracking()
            .Select(x => new { x.UserId, x.User!.Role })
            .Distinct()
            .ToListAsync(ct);

        var recentActivity = await _db.LibraryActivities
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedAt)
            .Take(10)
            .Select(x => new ServiceActivity(
                x.ActivityType,
                x.Title,
                x.CreatedAt,
                x.User != null ? x.User.FullName : null))
            .ToListAsync(ct);

        return new LibraryOversight(
            Titles: bookTotals?.Titles ?? 0,
            TotalCopies: bookTotals?.Copies ?? 0,
            AvailableCopies: bookTotals?.Available ?? 0,
            BorrowedCopies: Math.Max(0, (bookTotals?.Copies ?? 0) - (bookTotals?.Available ?? 0)),
            ActiveLoans: activeLoans,
            OverdueLoans: overdue,
            ReturnedThisWeek: returnedThisWeek,
            DamagedBooks: statusCounts.Count(s =>
                string.Equals(s, "Damaged", StringComparison.OrdinalIgnoreCase)),
            LostBooks: statusCounts.Count(s =>
                string.Equals(s, "Lost", StringComparison.OrdinalIgnoreCase)),
            ArchivedBooks: statusCounts.Count(s =>
                string.Equals(s, "Archived", StringComparison.OrdinalIgnoreCase)),
            ReservationStatuses: Group(reservations),
            StudentPatrons: patronRows.Count(x =>
                string.Equals(x.Role, "Student", StringComparison.OrdinalIgnoreCase)),
            FacultyPatrons: patronRows.Count(x =>
                string.Equals(x.Role, "Faculty", StringComparison.OrdinalIgnoreCase)),
            RecentActivity: recentActivity,
            GeneratedAt: DateTime.UtcNow);
    }

    // ---------------------------------------------------------
    // GUIDANCE
    // ---------------------------------------------------------

    public async Task<GuidanceOversight> GetGuidanceAsync(CancellationToken ct = default)
    {
        var counselors = await _db.Users
            .AsNoTracking()
            .Where(u => u.Role == "Counselor")
            .Select(u => u.AccountStatus)
            .ToListAsync(ct);

        var appointments = await _db.GuidanceAppointments
            .AsNoTracking()
            .Select(x => x.Status)
            .ToListAsync(ct);

        var appointmentCounts = Group(appointments);

        var pendingAppointments = Count(appointmentCounts, "Pending");
        var approvedAppointments = Count(appointmentCounts, "Approved") +
                                   Count(appointmentCounts, "Confirmed");
        var completedAppointments = Count(appointmentCounts, "Completed");
        var cancelledAppointments = Count(appointmentCounts, "Cancelled") +
                                    Count(appointmentCounts, "Rejected") +
                                    Count(appointmentCounts, "Expired");

        // Counts ONLY. Narrative contents and private notes are never read here.
        var narrativeTotal = await _db.GuidanceCounselingRecords
            .AsNoTracking()
            .CountAsync(ct);

        var narrativeFinalized = await _db.GuidanceCounselingRecords
            .AsNoTracking()
            .CountAsync(x => x.Status == "Finalized", ct);

        var chatConversations = await _db.GuidanceChatConversations
            .AsNoTracking()
            .CountAsync(ct);

        var messagesThisWeek = await _db.GuidanceChatMessages
            .AsNoTracking()
            .CountAsync(x => x.CreatedAt >= DateTime.UtcNow.AddDays(-7), ct);

        var upcoming = await _db.GuidanceAppointments
            .AsNoTracking()
            .Where(x => x.Date >= DateOnly.FromDateTime(DateTime.Today))
            .OrderBy(x => x.Date)
            .ThenBy(x => x.TimeSlot)
            .Take(10)
            .Select(x => new GuidanceAppointmentRow(
                x.Id,
                x.Date,
                x.TimeSlot,
                x.Status,
                null,
                null,
                null))
            .ToListAsync(ct);

        var recentAppointments = await _db.GuidanceAppointments
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedAt)
            .Take(10)
            .Select(x => new ServiceActivity(
                "Appointment",
                x.Status,
                x.CreatedAt,
                x.Counselor != null ? x.Counselor.FullName : null))
            .ToListAsync(ct);

        return new GuidanceOversight(
            TotalCounselors: counselors.Count,
            ActiveCounselors: counselors.Count(s =>
                string.Equals(s, "Active", StringComparison.OrdinalIgnoreCase)),
            TotalAppointments: appointments.Count,
            PendingAppointments: pendingAppointments,
            ApprovedAppointments: approvedAppointments,
            CompletedAppointments: completedAppointments,
            CancelledAppointments: cancelledAppointments,
            AppointmentStatuses: appointmentCounts,
            NarrativeReports: narrativeTotal,
            FinalizedNarrativeReports: narrativeFinalized,
            ChatConversations: chatConversations,
            ChatMessagesThisWeek: messagesThisWeek,
            UpcomingAppointments: upcoming,
            RecentActivity: recentAppointments,
            GeneratedAt: DateTime.UtcNow);
    }

    // ---------------------------------------------------------
    // LOST & FOUND
    // ---------------------------------------------------------

    public async Task<LostFoundOversight> GetLostFoundAsync(CancellationToken ct = default)
    {
        var reports = await _db.LostFoundReports
            .AsNoTracking()
            .Select(x => new { x.ReportType, x.Status })
            .ToListAsync(ct);

        var claims = await _db.ClaimRecords
            .AsNoTracking()
            .Select(x => x.Status)
            .ToListAsync(ct);

        var schedules = await _db.ClaimSchedules
            .AsNoTracking()
            .Select(x => x.Status)
            .ToListAsync(ct);

        var recoveriesCompleted = await _db.RecoveryRecords
            .AsNoTracking()
            .CountAsync(x => x.Status == "Completed", ct);

        var reportsThisWeek = await _db.LostFoundReports
            .AsNoTracking()
            .CountAsync(x => x.CreatedAt >= DateTime.Now.AddDays(-7), ct);

        var recentReports = await _db.LostFoundReports
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedAt)
            .Take(10)
            .Select(x => new LostFoundReportRow(
                x.ReportId,
                x.ItemName,
                x.Category,
                x.ReportType,
                x.Status,
                x.CreatedAt))
            .ToListAsync(ct);

        var claimCounts = Group(claims);
        var scheduleCounts = Group(schedules);

        return new LostFoundOversight(
            TotalReports: reports.Count,
            LostReports: reports.Count(x =>
                string.Equals(x.ReportType, "Lost", StringComparison.OrdinalIgnoreCase)),
            FoundReports: reports.Count(x =>
                string.Equals(x.ReportType, "Found", StringComparison.OrdinalIgnoreCase)),
            PendingReports: reports.Count(x =>
                string.Equals(x.Status, "Pending", StringComparison.OrdinalIgnoreCase)),
            ReportsThisWeek: reportsThisWeek,
            Claims: claims.Count,
            ActiveClaims: Count(claimCounts, "Processing") + Count(claimCounts, "Pending"),
            ApprovedClaims: Count(claimCounts, "Approved") + Count(claimCounts, "Claimed"),
            RejectedClaims: Count(claimCounts, "Rejected"),
            ClaimStatuses: claimCounts,
            PendingPickups: Count(scheduleCounts, "Scheduled") + Count(scheduleCounts, "Pending"),
            CompletedPickups: Count(scheduleCounts, "Completed"),
            CompletedRecoveries: recoveriesCompleted,
            ScheduleStatuses: scheduleCounts,
            RecentReports: recentReports,
            GeneratedAt: DateTime.UtcNow);
    }

    // ---------------------------------------------------------
    // HELPERS
    // ---------------------------------------------------------

    private static Dictionary<string, int> Group(IEnumerable<string?> values) =>
        values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .GroupBy(v => v!.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

    private static int Count(Dictionary<string, int> map, string key) =>
        map.TryGetValue(key, out var value) ? value : 0;
}

public sealed record ServiceActivity(
    string Type,
    string? Title,
    DateTime At,
    string? Actor);

public sealed record LibraryOversight(
    int Titles,
    int TotalCopies,
    int AvailableCopies,
    int BorrowedCopies,
    int ActiveLoans,
    int OverdueLoans,
    int ReturnedThisWeek,
    int DamagedBooks,
    int LostBooks,
    int ArchivedBooks,
    Dictionary<string, int> ReservationStatuses,
    int StudentPatrons,
    int FacultyPatrons,
    List<ServiceActivity> RecentActivity,
    DateTime GeneratedAt);

public sealed record GuidanceAppointmentRow(
    int Id,
    DateOnly Date,
    string TimeSlot,
    string Status,
    string? ConcernType,
    string? CounselorName,
    string? StudentName);

public sealed record GuidanceOversight(
    int TotalCounselors,
    int ActiveCounselors,
    int TotalAppointments,
    int PendingAppointments,
    int ApprovedAppointments,
    int CompletedAppointments,
    int CancelledAppointments,
    Dictionary<string, int> AppointmentStatuses,
    int NarrativeReports,
    int FinalizedNarrativeReports,
    int ChatConversations,
    int ChatMessagesThisWeek,
    List<GuidanceAppointmentRow> UpcomingAppointments,
    List<ServiceActivity> RecentActivity,
    DateTime GeneratedAt);

public sealed record LostFoundReportRow(
    int ReportId,
    string ItemName,
    string Category,
    string ReportType,
    string Status,
    DateTime CreatedAt);

public sealed record LostFoundOversight(
    int TotalReports,
    int LostReports,
    int FoundReports,
    int PendingReports,
    int ReportsThisWeek,
    int Claims,
    int ActiveClaims,
    int ApprovedClaims,
    int RejectedClaims,
    Dictionary<string, int> ClaimStatuses,
    int PendingPickups,
    int CompletedPickups,
    int CompletedRecoveries,
    Dictionary<string, int> ScheduleStatuses,
    List<LostFoundReportRow> RecentReports,
    DateTime GeneratedAt);
