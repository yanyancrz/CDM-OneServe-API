using CDM_OneServe_API.Data;
using CDM_OneServe_API.DTOs.LostFound;
using CDM_OneServe_API.DTOs.Notifications;
using CDM_OneServe_API.Models.LostFound;
using CDM_OneServe_API.Services.Notifications;

using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Services.LostFound;

/// <summary>
/// Lost &amp; Found service - the FIND module ported into OneServe.
///
/// Everything here runs against the shared <see cref="AppDbContext"/>.
/// There is no FINDDB, no FIND user table and no FIND login: the caller
/// is the signed-in OneServe user, read from the JWT by the controllers.
///
/// The state machine is the one FIND shipped with:
///   report  Pending -> Published | Rejected
///   claim   Processing -> Awaiting Pickup -> Returned
///                       \-> Rejected / Cancelled
///   schedule AwaitingPickup -> Scheduled -> Completed | Cancelled
///   recovery Pending | AtAdminOffice -> Completed | Rejected
///
/// Approving a claim opens a pickup schedule; completing the handover
/// marks the report Claimed and closes every other active claim.
/// </summary>
public class LostFoundService
{
    /// <summary>Where handovers happen - the OneServe Student Affairs Office.</summary>
    public const string PickupLocation = "OneServe Student Affairs Office";

    private static readonly string[] AllowedImageExtensions = { ".jpg", ".jpeg", ".png", ".webp" };

    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _environment;
    private readonly IPushDispatcher _push;
    private readonly PushSubscriptionService _pushStore;

    public LostFoundService(
        AppDbContext db,
        IWebHostEnvironment environment,
        IPushDispatcher push,
        PushSubscriptionService pushStore)
    {
        _db = db;
        _environment = environment;
        _push = push;
        _pushStore = pushStore;
    }

    // =========================================================
    // WEB PUSH
    // =========================================================
    //
    // Lost & Found reports events into the shared push outbox so the
    // student's browser gets them even with the app closed. Delivery is
    // asynchronous - see PushDispatcher.
    // =========================================================

    private Task<bool> PushAsync(
        int userId,
        string type,
        string title,
        string message,
        int reportId,
        string dedupeKey) =>
        _push.EnqueueAsync(new PushNotification(
            UserId: userId,
            Module: "LostFound",
            Type: type,
            Title: title,
            Body: message,
            TargetUrl: PushRoutes.Resolve("LostFound", type),
            DedupeKey: dedupeKey,
            Tag: $"lf-report-{reportId}"));

    // =========================================================
    // REPORTS
    // =========================================================

    public async Task<List<ReportDto>> GetMyReportsAsync(int userId) =>
        await _db.LostFoundReports
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => ToReportDto(r))
            .ToListAsync();

    /// <summary>
    /// The public board. Only published reports - and not ones already
    /// handed over (a Returned claim) - are visible to the campus.
    /// Search covers the item name, description and category.
    /// </summary>
    public async Task<List<ReportDto>> GetPublicAsync(string? type, string? status, string? search) =>
        await _db.LostFoundReports
            .Where(r => (r.Status == "Published" || r.Status == "Found") &&
                        !_db.ClaimRecords.Any(c => c.ReportId == r.ReportId && c.Status == "Returned") &&
                        (string.IsNullOrEmpty(type) || r.ReportType == type) &&
                        (string.IsNullOrEmpty(status) || r.Status == status) &&
                        (string.IsNullOrEmpty(search) || r.ItemName.Contains(search) ||
                         r.Description.Contains(search) || r.Category.Contains(search))
            )
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => ToReportDto(r))
            .ToListAsync();

    public async Task<List<ReportDto>> GetRecentFoundAsync() =>
        await _db.LostFoundReports
            .Where(r => r.ReportType == "Found" && r.Status == "Published" &&
                        !_db.ClaimRecords.Any(c => c.ReportId == r.ReportId && c.Status == "Returned"))
            .OrderByDescending(r => r.CreatedAt)
            .Take(6)
            .Select(r => ToReportDto(r))
            .ToListAsync();

    public async Task<List<ReportDto>> GetRecentLostAsync() =>
        await _db.LostFoundReports
            .Where(r => r.ReportType == "Lost" && r.Status == "Published" &&
                        !_db.ClaimRecords.Any(c => c.ReportId == r.ReportId && c.Status == "Returned"))
            .OrderByDescending(r => r.CreatedAt)
            .Take(6)
            .Select(r => ToReportDto(r))
            .ToListAsync();

    /// <summary>A single report, if the caller may see it.</summary>
    public async Task<ReportDto?> GetReportAsync(int id)
    {
        var report = await _db.LostFoundReports
            .FirstOrDefaultAsync(r => r.ReportId == id);

        return report is null || !IsPubliclyVisible(report)
            ? null
            : ToReportDto(report);
    }

    public async Task<LostFoundResult<ReportDto>> CreateReportAsync(int userId, CreateReportDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.ItemName))
            return LostFoundResult<ReportDto>.BadRequest("Item name is required.");

        var reportType = dto.ReportType?.Trim() ?? "";
        var isLost = IsStatus(reportType, "Lost");
        var isFound = IsStatus(reportType, "Found");

        if (!isLost && !isFound)
            return LostFoundResult<ReportDto>.BadRequest("Report type must be Lost or Found.");

        // A lost item has no keeper yet; a found item goes straight to the
        // office - keepers outside the office no longer exist. Every physical
        // handover runs through the OneServe Student Affairs Office.
        var custody = isLost ? "None" : "AdminOffice";

        var report = new LostFoundReport
        {
            UserId = userId,
            ItemName = dto.ItemName.Trim(),
            Description = dto.Description ?? string.Empty,
            Category = dto.Category ?? string.Empty,
            Location = dto.Location ?? string.Empty,
            DateLostFound = ToPhWallClock(dto.DateLostFound),
            ReportType = isLost ? "Lost" : "Found",
            ImagePath = string.IsNullOrWhiteSpace(dto.ImagePath) ? null : dto.ImagePath,
            ItemCustody = custody,
            Status = "Pending",
            CreatedAt = PhNow(),
        };

        _db.LostFoundReports.Add(report);
        await _db.SaveChangesAsync();

        return LostFoundResult<ReportDto>.Created(ToReportDto(report), "Report submitted for review.");
    }

    public async Task<LostFoundResult<ReportDto>> UpdateReportAsync(int userId, int id, UpdateReportDto dto)
    {
        var report = await _db.LostFoundReports
            .FirstOrDefaultAsync(r => r.ReportId == id && r.UserId == userId);

        if (report is null)
            return LostFoundResult<ReportDto>.NotFound("Report not found.");

        if (!IsStatus(report.Status, "Pending"))
            return LostFoundResult<ReportDto>.BadRequest("Only pending reports can be edited.");

        report.ItemName = dto.ItemName.Trim();
        report.Description = dto.Description ?? string.Empty;
        report.Category = dto.Category ?? string.Empty;
        report.Location = dto.Location ?? string.Empty;
        report.DateLostFound = ToPhWallClock(dto.DateLostFound);
        report.ImagePath = string.IsNullOrWhiteSpace(dto.ImagePath) ? null : dto.ImagePath;

        await _db.SaveChangesAsync();

        return LostFoundResult<ReportDto>.Ok(ToReportDto(report));
    }

    public async Task<LostFoundResult> DeleteReportAsync(int userId, int id)
    {
        var report = await _db.LostFoundReports
            .FirstOrDefaultAsync(r => r.ReportId == id && r.UserId == userId);

        if (report is null)
            return LostFoundResult.NotFound("Report not found.");

        if (!IsStatus(report.Status, "Pending"))
            return LostFoundResult.BadRequest("Only pending reports can be deleted.");

        _db.LostFoundReports.Remove(report);
        await _db.SaveChangesAsync();

        return LostFoundResult.Ok("Report deleted.");
    }

    /// <summary>Admin review: publish or reject a pending report.</summary>
    public async Task<LostFoundResult<ReportDto>> UpdateReportStatusAsync(int id, string status)
    {
        var requested = status?.Trim() ?? "";

        if (!IsStatus(requested, "Published") && !IsStatus(requested, "Rejected"))
            return LostFoundResult<ReportDto>.BadRequest("Invalid status. Use Published or Rejected.");

        var report = await _db.LostFoundReports.FirstOrDefaultAsync(r => r.ReportId == id);
        if (report is null)
            return LostFoundResult<ReportDto>.NotFound("Report not found.");

        if (!IsStatus(report.Status, "Pending"))
            return LostFoundResult<ReportDto>.BadRequest("Only pending reports can be reviewed.");

        report.Status = IsStatus(requested, "Published") ? "Published" : "Rejected";

        await _db.SaveChangesAsync();

        // Publishing puts the report on the board - and publishing
        // recomputes its matches, so the matches page fills itself.
        // Rejecting retracts it: unconfirmed pairs involving it are withdrawn.
        if (IsStatus(report.Status, "Published"))
        {
            await RefreshAutoMatchesAsync(report);
        }
        else
        {
            var stale = await _db.MatchRecords
                .Where(m => !m.IsConfirmed &&
                            (m.LostReportId == report.ReportId || m.FoundReportId == report.ReportId))
                .ToListAsync();

            if (stale.Count > 0)
            {
                _db.MatchRecords.RemoveRange(stale);
                await _db.SaveChangesAsync();
            }
        }

        return LostFoundResult<ReportDto>.Ok(ToReportDto(report));
    }

    /// <summary>
    /// Stores a report photo under wwwroot/uploads/lostfound and returns
    /// the ROOT-RELATIVE path (the same convention the marketplace uses),
    /// so the frontend can resolve it against the API origin.
    /// </summary>
    public async Task<LostFoundResult<string>> UploadImageAsync(IFormFile? file)
    {
        if (file is null || file.Length == 0)
            return LostFoundResult<string>.BadRequest("No file uploaded.");

        const long maxFileSize = 5 * 1024 * 1024;
        if (file.Length > maxFileSize)
            return LostFoundResult<string>.BadRequest("Image must not exceed 5 MB.");

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (Array.IndexOf(AllowedImageExtensions, extension) < 0)
            return LostFoundResult<string>.BadRequest("Only JPG, JPEG, PNG, and WEBP images are allowed.");

        var folder = Path.Combine(_environment.WebRootPath, "uploads", "lostfound");
        Directory.CreateDirectory(folder);

        var fileName = $"{Guid.NewGuid()}{extension}";
        var filePath = Path.Combine(folder, fileName);

        await using var stream = new FileStream(filePath, FileMode.Create);
        await file.CopyToAsync(stream);

        return LostFoundResult<string>.Ok($"/uploads/lostfound/{fileName}");
    }

    // =========================================================
    // CLAIMS
    // =========================================================

    public async Task<List<MyClaimDto>> GetMyClaimsAsync(int userId) =>
        await _db.ClaimRecords
            .Where(c => c.ClaimedByUserId == userId)
            .Include(c => c.Report)
            .OrderByDescending(c => c.ClaimDate)
            .Select(c => new MyClaimDto
            {
                ClaimId = c.ClaimId,
                ReportId = c.ReportId,
                ImagePath = c.Report != null ? c.Report.ImagePath : null,
                ItemName = c.Report != null ? c.Report.ItemName : string.Empty,
                Category = c.Report != null ? c.Report.Category : string.Empty,
                Location = c.Report != null ? c.Report.Location : string.Empty,
                ReportType = c.Report != null ? c.Report.ReportType : string.Empty,
                ItemCustody = c.Report != null ? c.Report.ItemCustody : "None",
                ClaimDate = c.ClaimDate,
                Status = c.Status,
                VerificationNotes = c.VerificationNotes,
            })
            .ToListAsync();

    public async Task<LostFoundResult<MyClaimDto>> GetClaimForReportAsync(int userId, int reportId)
    {
        var claim = await _db.ClaimRecords
            .Where(c => c.ReportId == reportId && c.ClaimedByUserId == userId)
            .Include(c => c.Report)
            .OrderByDescending(c => c.ClaimDate)
            .Select(c => new MyClaimDto
            {
                ClaimId = c.ClaimId,
                ReportId = c.ReportId,
                ImagePath = c.Report != null ? c.Report.ImagePath : null,
                ItemName = c.Report != null ? c.Report.ItemName : string.Empty,
                Category = c.Report != null ? c.Report.Category : string.Empty,
                Location = c.Report != null ? c.Report.Location : string.Empty,
                ReportType = c.Report != null ? c.Report.ReportType : string.Empty,
                ItemCustody = c.Report != null ? c.Report.ItemCustody : "None",
                ClaimDate = c.ClaimDate,
                Status = c.Status,
                VerificationNotes = c.VerificationNotes,
            })
            .FirstOrDefaultAsync();

        return claim is null
            ? LostFoundResult<MyClaimDto>.NotFound("You have not submitted a claim for this report.")
            : LostFoundResult<MyClaimDto>.Ok(claim);
    }

    /// <summary>Who may see the claim list for a report: its finder, or the admin.</summary>
    public async Task<LostFoundResult<List<ClaimAdminDto>>> GetClaimsForReportAsync(int userId, int reportId)
    {
        var report = await _db.LostFoundReports.FirstOrDefaultAsync(r => r.ReportId == reportId);
        if (report is null)
            return LostFoundResult<List<ClaimAdminDto>>.NotFound("Report not found.");

        var finderId = await GetActualFinderUserIdAsync(report);
        if (finderId != userId)
            return LostFoundResult<List<ClaimAdminDto>>.Forbidden("Only the item's finder can view its claims.");

        var claims = await ToClaimAdminListAsync(reportId);
        return LostFoundResult<List<ClaimAdminDto>>.Ok(claims);
    }

    public async Task<LostFoundResult<MyClaimDto>> SubmitClaimAsync(int userId, CreateClaimDto dto)
    {
        if (dto.ReportId <= 0)
            return LostFoundResult<MyClaimDto>.BadRequest("A valid report is required.");

        var report = await _db.LostFoundReports.FirstOrDefaultAsync(r => r.ReportId == dto.ReportId);
        if (report is null)
            return LostFoundResult<MyClaimDto>.NotFound("Report not found.");

        if (!IsStatus(report.ReportType, "Found"))
            return LostFoundResult<MyClaimDto>.BadRequest("Only found item reports can be claimed.");

        if (report.UserId == userId)
            return LostFoundResult<MyClaimDto>.BadRequest("You cannot claim your own found item report.");

        if (await _db.ClaimRecords.AnyAsync(c => c.ReportId == dto.ReportId && c.Status == "Returned"))
            return LostFoundResult<MyClaimDto>.BadRequest("This item has already been returned to its owner.");

        var hasActiveClaim = await _db.ClaimRecords.AnyAsync(c =>
            c.ReportId == dto.ReportId &&
            c.ClaimedByUserId == userId &&
            (c.Status == "Processing" || c.Status == "Awaiting Pickup" || c.Status == "Scheduled"));

        if (hasActiveClaim)
            return LostFoundResult<MyClaimDto>.BadRequest("You already have an active claim for this item.");

        var claim = new ClaimRecord
        {
            ReportId = report.ReportId,
            ClaimedByUserId = userId,
            VerificationNotes = dto.VerificationNotes?.Trim() ?? string.Empty,
            ClaimDate = PhNow(),
            Status = "Processing",
        };

        _db.ClaimRecords.Add(claim);

        _db.LostFoundNotifications.Add(new Notification
        {
            UserId = report.UserId,
            ReportId = report.ReportId,
            Title = "New Claim",
            Message = $"A student submitted a claim for your found item: {report.ItemName}.",
            NotificationType = "ClaimSubmitted",
            IsRead = false,
            CreatedAt = PhNow(),
        });

        await PushAsync(
            report.UserId,
            "ClaimSubmitted",
            "New Claim",
            $"A student submitted a claim for your found item: {report.ItemName}.",
            report.ReportId,
            $"claimsubmitted:{report.ReportId}");

        await _db.SaveChangesAsync();

        var mine = await GetClaimForReportAsync(userId, report.ReportId);
        return LostFoundResult<MyClaimDto>.Created(mine.Data!, "Claim submitted successfully.");
    }

    /// <summary>
    /// The finder (or the admin) approves a claim for pickup or rejects it.
    /// Approving moves the claim straight to "Awaiting Pickup" (the same
    /// status the pickup queue shows), opens an "Awaiting Pickup" schedule
    /// and notifies the claimant; rejection closes the claim, cancels its
    /// schedules and - for a lost report - rejects the recovery and returns
    /// the report to the board.
    /// </summary>
    public async Task<LostFoundResult> UpdateClaimStatusAsync(int actorUserId, int id, string status)
    {
        var requested = status?.Trim() ?? "";

        // "Claimed" is accepted for older clients and means the same thing.
        var approve = IsStatus(requested, "Awaiting Pickup") || IsStatus(requested, "Claimed");

        if (!approve && !IsStatus(requested, "Rejected"))
            return LostFoundResult.BadRequest("Invalid status. Use Awaiting Pickup or Rejected.");

        var claim = await _db.ClaimRecords
            .Include(c => c.Report)
            .FirstOrDefaultAsync(c => c.ClaimId == id);

        if (claim is null)
            return LostFoundResult.NotFound("Claim not found.");

        if (claim.Report is null)
            return LostFoundResult.BadRequest("Associated report was not found.");

        var report = claim.Report;
        var finderId = await GetActualFinderUserIdAsync(report);

        if (actorUserId != finderId && !await IsLostFoundAdminAsync(actorUserId))
            return LostFoundResult.Forbidden("Only the item's finder or the Lost & Found admin can review claims.");

        if (approve)
        {
            if (!IsStatus(claim.Status, "Processing"))
                return LostFoundResult.BadRequest("Only processing claims can be approved for pickup.");

            if (await _db.ClaimRecords.AnyAsync(c => c.ReportId == claim.ReportId && c.Status == "Returned"))
                return LostFoundResult.BadRequest("This item has already been returned to its owner.");

            claim.Status = "Awaiting Pickup";

            var hasSchedule = await _db.ClaimSchedules
                .AnyAsync(s => s.ClaimId == claim.ClaimId && s.Status != "Cancelled");

            if (!hasSchedule)
            {
                var claimant = await _db.Users.FindAsync(claim.ClaimedByUserId);

                _db.ClaimSchedules.Add(new ClaimSchedule
                {
                    ClaimId = claim.ClaimId,
                    ClaimantUserId = claim.ClaimedByUserId,
                    StudentId = claimant?.IdNumber ?? string.Empty,
                    ScheduledDateTime = PhNow(),
                    PickupLocation = PickupLocation,
                    Status = "Awaiting Pickup",
                    CreatedAt = PhNow(),
                });
            }

            _db.LostFoundNotifications.Add(new Notification
            {
                UserId = claim.ClaimedByUserId,
                ReportId = claim.ReportId,
                Title = "Ready for Pickup",
                Message = $"Your claim for {report.ItemName} is approved. It is now awaiting pickup - visit the {PickupLocation} during office hours for the handover.",
                NotificationType = "ClaimApproved",
                IsRead = false,
                CreatedAt = PhNow(),
            });

            await PushAsync(
                claim.ClaimedByUserId,
                "ClaimApproved",
                "Ready for Pickup",
                $"Your claim for {report.ItemName} is approved and awaiting pickup at the {PickupLocation}.",
                claim.ReportId,
                $"claimapproved:{claim.ClaimId}");

            await _db.SaveChangesAsync();
            return LostFoundResult.Ok("Claim approved and queued for pickup.");
        }

        // ---- Rejected ----
        claim.Status = "Rejected";

        if (IsStatus(report.ReportType, "Lost"))
        {
            var recovery = await _db.RecoveryRecords
                .Where(r => r.ReportId == report.ReportId && r.Status != "Rejected" && r.Status != "Completed")
                .OrderByDescending(r => r.CreatedAt)
                .FirstOrDefaultAsync();

            if (recovery is not null)
                recovery.Status = "Rejected";

            report.Status = "Published";
            report.ItemCustody = "None";
        }

        var schedules = await _db.ClaimSchedules
            .Where(s => s.ClaimId == claim.ClaimId && s.Status != "Completed" && s.Status != "Cancelled")
            .ToListAsync();

        foreach (var schedule in schedules)
            schedule.Status = "Cancelled";

        _db.LostFoundNotifications.Add(new Notification
        {
            UserId = claim.ClaimedByUserId,
            ReportId = claim.ReportId,
            Title = "Claim Rejected",
            Message = $"Your claim for {report.ItemName} has been rejected.",
            NotificationType = "ClaimRejected",
            IsRead = false,
            CreatedAt = PhNow(),
        });

        await PushAsync(
            claim.ClaimedByUserId,
            "ClaimRejected",
            "Claim Rejected",
            $"Your claim for {report.ItemName} has been rejected.",
            claim.ReportId,
            $"claimrejected:{claim.ClaimId}");

        await _db.SaveChangesAsync();
        return LostFoundResult.Ok("Claim rejected successfully.");
    }

    public async Task<LostFoundResult> CancelClaimAsync(int userId, int id)
    {
        var claim = await _db.ClaimRecords
            .Include(c => c.Report)
            .FirstOrDefaultAsync(c => c.ClaimId == id);

        if (claim is null)
            return LostFoundResult.NotFound("Claim not found.");

        if (claim.ClaimedByUserId != userId)
            return LostFoundResult.Forbidden("You can only cancel your own claim.");

        if (!(IsStatus(claim.Status, "Processing") ||
              IsStatus(claim.Status, "Scheduled") || IsStatus(claim.Status, "Awaiting Pickup")))
            return LostFoundResult.BadRequest("Only Processing, Awaiting Pickup, or Scheduled claims can be cancelled.");

        claim.Status = "Cancelled";

        var schedules = await _db.ClaimSchedules
            .Where(s => s.ClaimId == id && s.Status != "Completed" && s.Status != "Cancelled")
            .ToListAsync();

        foreach (var schedule in schedules)
            schedule.Status = "Cancelled";

        if (claim.Report is not null && IsStatus(claim.Report.ReportType, "Lost"))
        {
            var recovery = await _db.RecoveryRecords
                .Where(r => r.ReportId == claim.ReportId && r.Status != "Rejected" && r.Status != "Completed")
                .OrderByDescending(r => r.CreatedAt)
                .FirstOrDefaultAsync();

            if (recovery is not null)
                recovery.Status = "Rejected";

            claim.Report.Status = "Published";
            claim.Report.ItemCustody = "None";
        }

        await _db.SaveChangesAsync();
        return LostFoundResult.Ok("Claim cancelled successfully.");
    }

    public async Task<LostFoundResult> DeleteClaimAsync(int userId, int id)
    {
        var claim = await _db.ClaimRecords.FirstOrDefaultAsync(c => c.ClaimId == id);

        if (claim is null)
            return LostFoundResult.NotFound("Claim not found.");

        if (claim.ClaimedByUserId != userId)
            return LostFoundResult.Forbidden("You can only delete your own claim.");

        if (!IsStatus(claim.Status, "Cancelled"))
            return LostFoundResult.BadRequest("Only cancelled claims can be deleted.");

        var schedules = await _db.ClaimSchedules.Where(s => s.ClaimId == id).ToListAsync();
        _db.ClaimSchedules.RemoveRange(schedules);
        _db.ClaimRecords.Remove(claim);

        await _db.SaveChangesAsync();
        return LostFoundResult.Ok("Cancelled claim deleted successfully.");
    }

    // =========================================================
    // PICKUP AVAILABILITY + SCHEDULES
    // =========================================================

    public async Task<List<PickupAvailabilityDto>> GetAvailabilityAsync()
    {
        var now = PhNow();

        return await _db.PickupAvailabilities
            .Where(a => a.IsActive && a.EndDateTime >= now)
            .OrderBy(a => a.StartDateTime)
            .Select(a => new PickupAvailabilityDto
            {
                AvailabilityId = a.AvailabilityId,
                StartDateTime = a.StartDateTime,
                EndDateTime = a.EndDateTime,
                IsActive = a.IsActive,
                CreatedAt = a.CreatedAt,
            })
            .ToListAsync();
    }

    public async Task<LostFoundResult<PickupAvailabilityDto>> CreateAvailabilityAsync(CreatePickupAvailabilityDto dto)
    {
        var start = NormalizeFutureDateTime(dto.StartDateTime);
        var end = NormalizeFutureDateTime(dto.EndDateTime);

        if (start >= end)
            return LostFoundResult<PickupAvailabilityDto>.BadRequest("The start time must be before the end time.");

        if (start <= PhNow())
            return LostFoundResult<PickupAvailabilityDto>.BadRequest("Pickup availability must start in the future.");

        var overlaps = await _db.PickupAvailabilities
            .AnyAsync(a => a.IsActive && start < a.EndDateTime && end > a.StartDateTime);

        if (overlaps)
            return LostFoundResult<PickupAvailabilityDto>.Conflict("The selected availability period overlaps an existing published period.");

        var availability = new PickupAvailability
        {
            StartDateTime = start,
            EndDateTime = end,
            IsActive = true,
            CreatedAt = PhNow(),
        };

        _db.PickupAvailabilities.Add(availability);
        await _db.SaveChangesAsync();

        return LostFoundResult<PickupAvailabilityDto>.Ok(ToAvailabilityDto(availability), "Pickup availability published.");
    }

    public async Task<LostFoundResult> CreateAvailabilityBatchAsync(CreatePickupAvailabilityBatchDto dto)
    {
        if (dto.Availability is null || dto.Availability.Count == 0)
            return LostFoundResult.BadRequest("At least one pickup availability period is required.");

        var periods = dto.Availability
            .Select(x => new { Start = NormalizeFutureDateTime(x.StartDateTime), End = NormalizeFutureDateTime(x.EndDateTime) })
            .OrderBy(x => x.Start)
            .ToList();

        if (periods.Any(x => x.Start >= x.End))
            return LostFoundResult.BadRequest("Every availability period must have a start time before its end time.");

        if (periods.Any(x => x.Start <= PhNow()))
            return LostFoundResult.BadRequest("All pickup availability periods must start in the future.");

        for (var i = 1; i < periods.Count; i++)
            if (periods[i].Start < periods[i - 1].End)
                return LostFoundResult.BadRequest("The submitted pickup availability periods overlap each other.");

        var existing = await _db.PickupAvailabilities
            .Where(a => a.IsActive)
            .Select(a => new { a.StartDateTime, a.EndDateTime })
            .ToListAsync();

        foreach (var period in periods)
        {
            var overlaps = existing.Any(e => period.Start < e.EndDateTime && period.End > e.StartDateTime);
            if (overlaps)
                return LostFoundResult.Conflict($"The period {period.Start:MMM dd, yyyy h:mm tt} - {period.End:h:mm tt} overlaps an existing published availability period.");
        }

        var entities = periods.Select(p => new PickupAvailability
        {
            StartDateTime = p.Start,
            EndDateTime = p.End,
            IsActive = true,
            CreatedAt = PhNow(),
        }).ToList();

        _db.PickupAvailabilities.AddRange(entities);
        await _db.SaveChangesAsync();

        return LostFoundResult.Ok($"Pickup availability periods published successfully. ({entities.Count} added)");
    }

    public async Task<LostFoundResult> DeleteAvailabilityAsync(int id)
    {
        var availability = await _db.PickupAvailabilities.FirstOrDefaultAsync(a => a.AvailabilityId == id);
        if (availability is null)
            return LostFoundResult.NotFound("Pickup availability was not found.");

        if (!availability.IsActive)
            return LostFoundResult.Ok("Pickup availability is already inactive.");

        var usedBySchedule = await _db.ClaimSchedules.AnyAsync(s =>
            (s.Status == "Scheduled" || s.Status == "Completed") &&
            s.ScheduledDateTime >= availability.StartDateTime &&
            s.ScheduledDateTime <= availability.EndDateTime);

        if (usedBySchedule)
            return LostFoundResult.BadRequest("This availability period cannot be removed because it already contains an existing pickup schedule.");

        availability.IsActive = false;
        await _db.SaveChangesAsync();

        return LostFoundResult.Ok("Pickup availability removed successfully.");
    }

    /// <summary>The schedule for one claim - visible to claimant, finder and admin.</summary>
    public async Task<LostFoundResult<ClaimScheduleDto>> GetScheduleForClaimAsync(int userId, int claimId)
    {
        var claim = await _db.ClaimRecords
            .Include(c => c.Report)
            .FirstOrDefaultAsync(c => c.ClaimId == claimId);

        if (claim is null)
            return LostFoundResult<ClaimScheduleDto>.NotFound("Claim not found.");

        var isAdmin = await IsLostFoundAdminAsync(userId);
        var isClaimant = claim.ClaimedByUserId == userId;
        var finderId = claim.Report is null ? null : await GetActualFinderUserIdAsync(claim.Report);
        var isFinder = finderId.HasValue && finderId.Value == userId;

        if (!isAdmin && !isClaimant && !isFinder)
            return LostFoundResult<ClaimScheduleDto>.Forbidden("You cannot view this pickup schedule.");

        await EnsureApprovedAdminOfficeSchedulesAsync();

        var schedule = await _db.ClaimSchedules
            .Include(s => s.Claim)
                .ThenInclude(c => c.Report)
            .Where(s => s.ClaimId == claimId)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync();

        if (schedule is null)
            return LostFoundResult<ClaimScheduleDto>.NotFound("No pickup schedule exists for this claim.");

        var dto = await MapScheduleAsync(schedule);
        return LostFoundResult<ClaimScheduleDto>.Ok(dto);
    }

    public async Task<List<ClaimScheduleDto>> GetAllSchedulesAsync()
    {
        await EnsureApprovedAdminOfficeSchedulesAsync();

        var schedules = await _db.ClaimSchedules
            .Include(s => s.Claim)
                .ThenInclude(c => c.Report)
            .OrderBy(s => s.Status == "Awaiting Pickup" ? 0 : s.Status == "Scheduled" ? 1 : s.Status == "Completed" ? 2 : 3)
            .ThenBy(s => s.ScheduledDateTime)
            .ToListAsync();

        var dtos = new List<ClaimScheduleDto>();
        foreach (var schedule in schedules)
            dtos.Add(await MapScheduleAsync(schedule));

        return dtos;
    }

    /// <summary>
    /// The handover itself. Marks the schedule Completed and the claim
    /// Returned, completes a lost-item recovery, notifies both
    /// parties and closes every other active claim on the report.
    /// </summary>
    public async Task<LostFoundResult> CompleteScheduleAsync(int adminId, int scheduleId, CompleteClaimScheduleDto dto)
    {
        var receivedName = NormalizeName(dto.ReceivedByName);
        var receivedStudentId = dto.ReceivedByStudentId?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(receivedName))
            return LostFoundResult.BadRequest("Complete recipient name is required.");

        if (string.IsNullOrWhiteSpace(receivedStudentId))
            return LostFoundResult.BadRequest("Recipient Student ID is required.");

        var schedule = await _db.ClaimSchedules
            .Include(s => s.Claim)
                .ThenInclude(c => c.Report)
            .FirstOrDefaultAsync(s => s.ScheduleId == scheduleId);

        if (schedule is null)
            return LostFoundResult.NotFound("Pickup schedule not found.");

        if (!(IsStatus(schedule.Status, "Scheduled") || IsStatus(schedule.Status, "Awaiting Pickup")))
            return LostFoundResult.BadRequest("Only scheduled or awaiting-pickup records can be completed.");

        if (schedule.Claim is null || schedule.Claim.Report is null)
            return LostFoundResult.BadRequest("The claim or associated report could not be found.");

        var report = schedule.Claim.Report;

        if (await _db.ClaimRecords.AnyAsync(c => c.ReportId == report.ReportId && c.Status == "Returned"))
            return LostFoundResult.BadRequest("This item has already been returned to its owner.");

        var actualFinderUserId = await GetActualFinderUserIdAsync(report);

        schedule.ReceivedByName = receivedName;
        schedule.ReceivedByStudentId = receivedStudentId;
        schedule.StudentId = receivedStudentId;
        schedule.VerifiedAt = PhNow();
        schedule.VerifiedByAdminId = adminId;
        schedule.CompletedAt = PhNow();
        schedule.Status = "Completed";

        // The item is back with its owner - the report stays Published but
        // drops off the board (board queries exclude Returned claims).
        schedule.Claim.Status = "Returned";

        if (IsStatus(report.ReportType, "Lost"))
        {
            var recovery = await _db.RecoveryRecords
                .Where(r => r.ReportId == schedule.Claim.ReportId && r.Status != "Rejected" && r.Status != "Completed")
                .OrderByDescending(r => r.CreatedAt)
                .FirstOrDefaultAsync();

            if (recovery is not null)
                recovery.Status = "Completed";
        }

        _db.LostFoundNotifications.Add(new Notification
        {
            UserId = schedule.Claim.ClaimedByUserId,
            ReportId = schedule.Claim.ReportId,
            Title = "Handover Completed",
            Message = $"The handover for {report.ItemName} has been completed at {PickupLocation}.",
            NotificationType = "RecoveryCompleted",
            IsRead = false,
            CreatedAt = PhNow(),
        });

        await PushAsync(
            schedule.Claim.ClaimedByUserId,
            "RecoveryCompleted",
            "Handover Completed",
            $"The handover for {report.ItemName} has been completed at {PickupLocation}.",
            schedule.Claim.ReportId,
            $"handover:{schedule.ClaimId}");

        if (actualFinderUserId.HasValue && actualFinderUserId.Value != schedule.Claim.ClaimedByUserId)
        {
            await PushAsync(
                actualFinderUserId.Value,
                "RecoveryCompleted",
                "Item Handover Completed",
                $"The handover for {report.ItemName} has been completed at {PickupLocation}.",
                schedule.Claim.ReportId,
                $"handoverfinder:{schedule.ClaimId}");
        }

        var otherClaims = await _db.ClaimRecords
            .Where(c => c.ReportId == schedule.Claim.ReportId &&
                        c.ClaimId != schedule.ClaimId &&
                        (c.Status == "Processing" ||
                         c.Status == "Scheduled" || c.Status == "Awaiting Pickup"))
            .ToListAsync();

        foreach (var otherClaim in otherClaims)
        {
            otherClaim.Status = "Rejected";

            _db.LostFoundNotifications.Add(new Notification
            {
                UserId = otherClaim.ClaimedByUserId,
                ReportId = otherClaim.ReportId,
                Title = "Claim Closed",
                Message = $"The item {report.ItemName} has already been handed over to another claimant.",
                NotificationType = "ClaimRejected",
                IsRead = false,
                CreatedAt = PhNow(),
            });

            await PushAsync(
                otherClaim.ClaimedByUserId,
                "ClaimRejected",
                "Claim Closed",
                $"The item {report.ItemName} has already been handed over to another claimant.",
                otherClaim.ReportId,
                $"claimclosed:{otherClaim.ClaimId}");

            var otherSchedules = await _db.ClaimSchedules
                .Where(s => s.ClaimId == otherClaim.ClaimId && s.Status != "Completed")
                .ToListAsync();

            foreach (var otherSchedule in otherSchedules)
                otherSchedule.Status = "Cancelled";
        }

        await _db.SaveChangesAsync();

        return LostFoundResult.Ok("Handover completed successfully.");
    }

    /// <summary>
    /// Older pickups predate the automatic schedule, so one is
    /// created on read. Cancelled schedules do not count: they are
    /// replaceable.
    /// </summary>
    private async Task EnsureApprovedAdminOfficeSchedulesAsync()
    {
        var approvedClaims = await _db.ClaimRecords
            .Include(c => c.Report)
            .Where(c => c.Status == "Awaiting Pickup" && c.Report != null)
            .ToListAsync();

        var addedAny = false;

        foreach (var claim in approvedClaims)
        {
            var hasActiveSchedule = await _db.ClaimSchedules
                .AnyAsync(s => s.ClaimId == claim.ClaimId && s.Status != "Cancelled");

            if (hasActiveSchedule)
                continue;

            var claimant = await _db.Users.FindAsync(claim.ClaimedByUserId);

            _db.ClaimSchedules.Add(new ClaimSchedule
            {
                ClaimId = claim.ClaimId,
                ClaimantUserId = claim.ClaimedByUserId,
                StudentId = claimant?.IdNumber ?? string.Empty,
                ScheduledDateTime = PhNow(),
                PickupLocation = PickupLocation,
                Status = "Awaiting Pickup",
                CreatedAt = PhNow(),
            });

            addedAny = true;
        }

        if (addedAny)
            await _db.SaveChangesAsync();
    }

    // =========================================================
    // MATCHES
    // =========================================================

    public async Task<List<MatchDto>> GetMyMatchesAsync(int userId) =>
        await _db.MatchRecords
            .Include(m => m.LostReport)
            .Include(m => m.FoundReport)
            .Where(m => (m.LostReport != null && m.LostReport.UserId == userId) ||
                        (m.FoundReport != null && m.FoundReport.UserId == userId))
            .OrderByDescending(m => m.MatchPercentage)
            .ThenByDescending(m => m.CreatedAt)
            .Select(m => ToMatchDto(m))
            .ToListAsync();

    public async Task<List<MatchDto>> GetMyMatchesForReportAsync(int userId, int reportId)
    {
        var ownsReport = await _db.LostFoundReports
            .AnyAsync(r => r.ReportId == reportId && r.UserId == userId);

        if (!ownsReport)
            return new List<MatchDto>();

        return await _db.MatchRecords
            .Include(m => m.LostReport)
            .Include(m => m.FoundReport)
            .Where(m => m.LostReportId == reportId || m.FoundReportId == reportId)
            .OrderByDescending(m => m.MatchPercentage)
            .ThenByDescending(m => m.CreatedAt)
            .Select(m => ToMatchDto(m))
            .ToListAsync();
    }

    public async Task<LostFoundResult<MatchDto>> GetMatchAsync(int userId, int id)
    {
        var match = await _db.MatchRecords
            .Include(m => m.LostReport)
            .Include(m => m.FoundReport)
            .FirstOrDefaultAsync(m => m.MatchRecordId == id);

        if (match is null)
            return LostFoundResult<MatchDto>.NotFound("Match not found.");

        if (match.LostReport is null || match.FoundReport is null)
            return LostFoundResult<MatchDto>.BadRequest("The reports connected to this match could not be found.");

        var ownsLost = match.LostReport.UserId == userId;
        var ownsFound = match.FoundReport.UserId == userId;

        if (!ownsLost && !ownsFound)
            return LostFoundResult<MatchDto>.Forbidden("You can only view matches for your own reports.");

        return LostFoundResult<MatchDto>.Ok(ToMatchDto(match));
    }

    /// <summary>
    /// Automatic matching: 25 points per dimension (Name, Category,
    /// Location, Date), stored once per pair. Runs whenever a report is
    /// approved, so the matches page and the home rail fill themselves -
    /// nobody has to pair reports by hand.
    /// </summary>
    public const int AutoMatchThreshold = 25;

    /// <summary>
    /// Scores a Lost report against a Found report. Pure function, so the
    /// same pair always scores the same - the percentage is computed, never
    /// guessed.
    /// </summary>
    public static (int Total, int Name, int Category, int Location, int Date) ComputeMatchScore(
        string? lostName, string? foundName,
        string? lostCategory, string? foundCategory,
        string? lostLocation, string? foundLocation,
        DateTime lostDate, DateTime foundDate)
    {
        // NAME (25): word overlap. "iPhone 11" vs "iphone 11 pro" shares
        // 2 of 3 words -> 17. Single letters are noise, not signal.
        var lostTokens = Tokenize(lostName);
        var foundTokens = Tokenize(foundName);

        int name = 0;
        if (lostTokens.Count > 0 && foundTokens.Count > 0)
        {
            var shared = lostTokens.Intersect(foundTokens).Count();
            var all = lostTokens.Union(foundTokens).Count();
            name = all == 0 ? 0 : (int)Math.Round(25.0 * shared / all);
        }

        // CATEGORY (25): exact, case-insensitive. A phone is never a jacket.
        var category = string.Equals(
            (lostCategory ?? "").Trim(), (foundCategory ?? "").Trim(),
            StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(lostCategory)
            ? 25 : 0;

        // LOCATION (25/12): same place is strong; one naming the other
        // ("Clinic" vs "Clinic Room 2") is half. Blank locations score
        // nothing - missing data must not manufacture a match.
        var lostLoc = (lostLocation ?? "").Trim();
        var foundLoc = (foundLocation ?? "").Trim();

        int location = 0;
        if (lostLoc.Length > 0 && foundLoc.Length > 0)
        {
            if (string.Equals(lostLoc, foundLoc, StringComparison.OrdinalIgnoreCase))
                location = 25;
            else if (lostLoc.Contains(foundLoc, StringComparison.OrdinalIgnoreCase) ||
                     foundLoc.Contains(lostLoc, StringComparison.OrdinalIgnoreCase))
                location = 12;
        }

        // DATE (25/15/8): same calendar day is strongest; nearby days fade.
        var days = Math.Abs((lostDate.Date - foundDate.Date).TotalDays);

        int date = days < 1 ? 25 : days <= 3 ? 15 : days <= 7 ? 8 : 0;

        return (name + category + location + date, name, category, location, date);
    }

    private static HashSet<string> Tokenize(string? value) =>
        new((value ?? "").ToLowerInvariant()
            .Split(Array.Empty<char>(), StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(part => part.Split(
                new[] { ' ', ',', '.', '-', '_', '/', '(', ')', ':', ';', '\'', '"' },
                StringSplitOptions.RemoveEmptyEntries))
            .Where(token => token.Length >= 2 && token.Any(char.IsLetterOrDigit)));

    /// <summary>
    /// Recomputes automatic matches for every published report. Confirmed
    /// pairs are never rescored - a human verdict beats the arithmetic.
    /// </summary>
    public async Task<int> RecomputeAllAutoMatchesAsync()
    {
        var reports = await _db.LostFoundReports
            .Where(r => r.Status == "Published")
            .ToListAsync();

        foreach (var report in reports)
            await RefreshAutoMatchesAsync(report);

        return reports.Count;
    }

    /// <summary>
    /// Recomputes every pair involving this report. New pairs at or above
    /// the threshold are stored and both owners are notified; confirmed
    /// pairs are never rescored - a human verdict beats the arithmetic.
    /// </summary>
    public async Task RefreshAutoMatchesAsync(LostFoundReport report)
    {
        var isLost = IsStatus(report.ReportType, "Lost");

        var candidates = await _db.LostFoundReports
            .Where(r => r.ReportId != report.ReportId &&
                        (r.Status == "Published" || r.Status == "Found") &&
                        !_db.ClaimRecords.Any(c => c.ReportId == r.ReportId && c.Status == "Returned") &&
                        (isLost ? r.ReportType == "Found" : r.ReportType == "Lost"))
            .ToListAsync();

        foreach (var other in candidates)
        {
            var lost = isLost ? report : other;
            var found = isLost ? other : report;

            var (total, name, category, location, date) = ComputeMatchScore(
                lost.ItemName, found.ItemName,
                lost.Category, found.Category,
                lost.Location, found.Location,
                lost.DateLostFound, found.DateLostFound);

            var existing = await _db.MatchRecords
                .FirstOrDefaultAsync(m => m.LostReportId == lost.ReportId &&
                                          m.FoundReportId == found.ReportId);

            if (existing is not null)
            {
                if (existing.IsConfirmed)
                    continue;

                if (total < AutoMatchThreshold)
                {
                    _db.MatchRecords.Remove(existing);
                    continue;
                }

                existing.MatchPercentage = total;
                existing.NameScore = name;
                existing.CategoryScore = category;
                existing.LocationScore = location;
                existing.DateScore = date;
                continue;
            }

            if (total < AutoMatchThreshold)
                continue;

            var match = new MatchRecord
            {
                LostReportId = lost.ReportId,
                FoundReportId = found.ReportId,
                MatchPercentage = total,
                NameScore = name,
                CategoryScore = category,
                LocationScore = location,
                DateScore = date,
                IsConfirmed = false,
                CreatedAt = PhNow(),
            };

            _db.MatchRecords.Add(match);

            // Both owners hear about it once. The dedupe key is per pair per
            // user, so a rescore never re-pushes.
            foreach (var (ownerId, ownerReportId) in new[]
                     {
                         (lost.UserId, lost.ReportId),
                         (found.UserId, found.ReportId),
                     })
            {
                var message =
                    $"Possible match ({total}%): {lost.ItemName} may be the same as {found.ItemName}.";

                _db.LostFoundNotifications.Add(new Notification
                {
                    UserId = ownerId,
                    ReportId = ownerReportId,
                    Title = "Possible match found",
                    Message = message,
                    NotificationType = "MatchFound",
                    IsRead = false,
                    CreatedAt = PhNow(),
                });

                await PushAsync(
                    ownerId,
                    "MatchFound",
                    "Possible match found",
                    message,
                    ownerReportId,
                    $"matchauto:{lost.ReportId}:{found.ReportId}:{ownerId}");
            }
        }

        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// Pairs a lost report with a found report. The caller must own one
    /// of the two - a match is a claim about someone else's report, so
    /// it is created by a party to it (or the admin, who owns neither
    /// but runs the board).
    /// </summary>
    public async Task<LostFoundResult<MatchDto>> CreateMatchAsync(int userId, CreateMatchDto dto)
    {
        var lostReport = await _db.LostFoundReports.FirstOrDefaultAsync(r => r.ReportId == dto.LostReportId);
        var foundReport = await _db.LostFoundReports.FirstOrDefaultAsync(r => r.ReportId == dto.FoundReportId);

        if (lostReport is null || foundReport is null)
            return LostFoundResult<MatchDto>.NotFound("Both the lost and the found report must exist.");

        if (!IsStatus(lostReport.ReportType, "Lost"))
            return LostFoundResult<MatchDto>.BadRequest("The first report must be a Lost report.");

        if (!IsStatus(foundReport.ReportType, "Found"))
            return LostFoundResult<MatchDto>.BadRequest("The second report must be a Found report.");

        var ownsLost = lostReport.UserId == userId;
        var ownsFound = foundReport.UserId == userId;
        var isAdmin = await IsLostFoundAdminAsync(userId);

        if (!ownsLost && !ownsFound && !isAdmin)
            return LostFoundResult<MatchDto>.Forbidden("You must own one of the two reports to create a match.");

        var percentage = dto.MatchPercentage;
        if (percentage < 0) percentage = 0;
        if (percentage > 100) percentage = 100;

        var match = new MatchRecord
        {
            LostReportId = lostReport.ReportId,
            FoundReportId = foundReport.ReportId,
            MatchPercentage = percentage,
            IsConfirmed = false,
            CreatedAt = PhNow(),
        };

        _db.MatchRecords.Add(match);
        await _db.SaveChangesAsync();

        return LostFoundResult<MatchDto>.Created(ToMatchDto(match), "Match created.");
    }

    public async Task<LostFoundResult> ConfirmMatchAsync(int userId, int id)
    {
        var match = await _db.MatchRecords
            .Include(m => m.LostReport)
            .Include(m => m.FoundReport)
            .FirstOrDefaultAsync(m => m.MatchRecordId == id);

        if (match is null)
            return LostFoundResult.NotFound("Match not found.");

        var ownsLost = match.LostReport is not null && match.LostReport.UserId == userId;
        var ownsFound = match.FoundReport is not null && match.FoundReport.UserId == userId;
        var isAdmin = await IsLostFoundAdminAsync(userId);

        if (!ownsLost && !ownsFound && !isAdmin)
            return LostFoundResult.Forbidden("You can only confirm matches for your own reports.");

        match.IsConfirmed = true;
        await _db.SaveChangesAsync();

        return LostFoundResult.Ok("Match confirmed.");
    }

    public async Task<List<MatchDto>> GetAllMatchesAsync() =>
        await _db.MatchRecords
            .Include(m => m.LostReport)
            .Include(m => m.FoundReport)
            .OrderByDescending(m => m.CreatedAt)
            .Select(m => ToMatchDto(m))
            .ToListAsync();

    // =========================================================
    // RECOVERY
    // =========================================================

    /// <summary>The recovery trail for a lost report - owner, finder or admin.</summary>
    public async Task<LostFoundResult<RecoveryDto>> GetRecoveryForReportAsync(int userId, int reportId)
    {
        var report = await _db.LostFoundReports.FirstOrDefaultAsync(r => r.ReportId == reportId);
        if (report is null)
            return LostFoundResult<RecoveryDto>.NotFound("Report not found.");

        if (!IsStatus(report.ReportType, "Lost"))
            return LostFoundResult<RecoveryDto>.BadRequest("Recovery information is only available for lost reports.");

        var recovery = await _db.RecoveryRecords
            .Where(r => r.ReportId == reportId && (r.Status != "Rejected" || r.FinderUserId == userId))
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync();

        if (recovery is null)
            return LostFoundResult<RecoveryDto>.NotFound("No recovery report exists for this item.");

        var isAdmin = await IsLostFoundAdminAsync(userId);
        var isOwner = report.UserId == userId;
        var isFinder = recovery.FinderUserId == userId;

        if (!isAdmin && !isOwner && !isFinder)
            return LostFoundResult<RecoveryDto>.Forbidden("You cannot view this recovery report.");

        var finder = await _db.Users.FindAsync(recovery.FinderUserId);

        return LostFoundResult<RecoveryDto>.Ok(new RecoveryDto
        {
            RecoveryId = recovery.RecoveryId,
            ReportId = recovery.ReportId,
            ItemName = report.ItemName,
            FinderUserId = recovery.FinderUserId,
            FinderName = finder?.FullName ?? string.Empty,
            FinderEmail = finder?.Email ?? string.Empty,
            RecoveryMethod = recovery.RecoveryMethod,
            Message = recovery.Message,
            ContactMethod = recovery.ContactMethod,
            ContactInfo = recovery.ContactInfo,
            Status = recovery.Status,
            CreatedAt = recovery.CreatedAt,
            CompletedAt = recovery.CompletedAt,
        });
    }

    public async Task<List<RecoveryDto>> GetMyRecoveryAsync(int userId)
    {
        var recoveries = await _db.RecoveryRecords
            .Where(r => r.FinderUserId == userId)
            .Include(r => r.Report)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();

        return recoveries.Select(r => new RecoveryDto
        {
            RecoveryId = r.RecoveryId,
            ReportId = r.ReportId,
            ItemName = r.Report != null ? r.Report.ItemName : string.Empty,
            FinderUserId = r.FinderUserId,
            RecoveryMethod = r.RecoveryMethod,
            Message = r.Message,
            ContactMethod = r.ContactMethod,
            ContactInfo = r.ContactInfo,
            Status = r.Status,
            CreatedAt = r.CreatedAt,
            CompletedAt = r.CompletedAt,
        }).ToList();
    }

    /// <summary>
    /// Someone recovered a lost item. The item MUST go to the office: the
    /// finder hands it to the OneServe Student Affairs Office, the report
    /// becomes Found, and a claim is auto-created for the owner so the
    /// pickup flow can run. Keeper handovers outside the office no longer
    /// exist, so no contact details are ever collected or published.
    /// </summary>
    public async Task<LostFoundResult<RecoveryDto>> CreateRecoveryAsync(int userId, CreateRecoveryDto dto)
    {
        if (dto.ReportId <= 0)
            return LostFoundResult<RecoveryDto>.BadRequest("A valid lost report is required.");

        var report = await _db.LostFoundReports.FirstOrDefaultAsync(r => r.ReportId == dto.ReportId);
        if (report is null)
            return LostFoundResult<RecoveryDto>.NotFound("Report not found.");

        if (!IsStatus(report.ReportType, "Lost"))
            return LostFoundResult<RecoveryDto>.BadRequest("Only lost reports can receive recovery reports.");

        if (report.UserId == userId)
            return LostFoundResult<RecoveryDto>.BadRequest("You cannot report recovering your own lost item.");

        if (await _db.ClaimRecords.AnyAsync(c => c.ReportId == report.ReportId && c.Status == "Returned"))
            return LostFoundResult<RecoveryDto>.BadRequest("This item has already been returned and is no longer available for recovery.");

        var recoveryMethod = dto.RecoveryMethod?.Trim() ?? "";

        if (!IsStatus(recoveryMethod, "Admin"))
            return LostFoundResult<RecoveryDto>.BadRequest(
                "A recovered item must be handed to the OneServe Student Affairs Office.");

        var message = dto.Message?.Trim() ?? string.Empty;

        // Office custody keeps the finder's details out of the open - contact
        // fields are always cleared, whatever the client sent.
        var contactMethod = string.Empty;
        var contactInfo = string.Empty;

        var activeRecovery = await _db.RecoveryRecords
            .AnyAsync(r => r.ReportId == dto.ReportId && r.Status != "Rejected" && r.Status != "Completed");

        if (activeRecovery)
            return LostFoundResult<RecoveryDto>.BadRequest("An active recovery report already exists for this item.");

        var recovery = new RecoveryRecord
        {
            ReportId = report.ReportId,
            FinderUserId = userId,
            RecoveryMethod = "Admin",
            ContactMethod = contactMethod,
            ContactInfo = contactInfo,
            Message = message,
            Status = "AtAdminOffice",
            CreatedAt = PhNow(),
        };

        _db.RecoveryRecords.Add(recovery);

        // The item is found, at the office.
        report.Status = "Found";
        report.ItemCustody = "AdminOffice";

        // Auto-create the owner's claim so the pickup flow can run.
        var ownerClaim = await _db.ClaimRecords
            .Where(c => c.ReportId == report.ReportId &&
                        c.ClaimedByUserId == report.UserId &&
                        (c.Status == "Processing" || c.Status == "Awaiting Pickup" || c.Status == "Scheduled"))
            .FirstOrDefaultAsync();

        if (ownerClaim is null)
        {
            ownerClaim = new ClaimRecord
            {
                ReportId = report.ReportId,
                ClaimedByUserId = report.UserId,
                ClaimDate = PhNow(),
                VerificationNotes = message,
                Status = "Processing",
            };

            _db.ClaimRecords.Add(ownerClaim);
        }
        else
        {
            ownerClaim.Status = "Processing";
            ownerClaim.ClaimDate = PhNow();
            ownerClaim.VerificationNotes = message;
        }

        var ownerMessage =
            $"Your lost item, {report.ItemName}, was reported as found and given to the {PickupLocation}. Your recovery claim has been created and is awaiting approval.";

        _db.LostFoundNotifications.Add(new Notification
        {
            UserId = report.UserId,
            ReportId = report.ReportId,
            Title = "Lost Item Found",
            Message = ownerMessage,
            NotificationType = "MatchFound",
            IsRead = false,
            CreatedAt = PhNow(),
        });

        await PushAsync(
            report.UserId,
            "MatchFound",
            "Lost Item Found",
            ownerMessage,
            report.ReportId,
            $"matchfound:{report.ReportId}");

        await _db.SaveChangesAsync();

        return LostFoundResult<RecoveryDto>.Created(new RecoveryDto
        {
            RecoveryId = recovery.RecoveryId,
            ReportId = recovery.ReportId,
            ItemName = report.ItemName,
            FinderUserId = recovery.FinderUserId,
            RecoveryMethod = recovery.RecoveryMethod,
            Message = recovery.Message,
            ContactMethod = recovery.ContactMethod,
            ContactInfo = recovery.ContactInfo,
            Status = recovery.Status,
            CreatedAt = recovery.CreatedAt,
        }, "Recovery report submitted successfully. A recovery claim has been created for the item owner.");
    }

    // =========================================================
    // NOTIFICATIONS + PUSH SUBSCRIPTIONS
    // =========================================================

    public async Task<List<NotificationDto>> GetNotificationsAsync(int userId) =>
        await _db.LostFoundNotifications
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt)
            .Select(n => new NotificationDto
            {
                NotificationId = n.NotificationId,
                UserId = n.UserId,
                ReportId = n.ReportId,
                Title = n.Title,
                Message = n.Message,
                NotificationType = n.NotificationType,
                TargetUrl = n.TargetUrl,
                IsRead = n.IsRead,
                CreatedAt = n.CreatedAt,
            })
            .ToListAsync();

    public async Task<int> GetUnreadCountAsync(int userId) =>
        await _db.LostFoundNotifications.CountAsync(n => n.UserId == userId && !n.IsRead);

    public async Task<LostFoundResult> MarkNotificationReadAsync(int userId, int id)
    {
        var notification = await _db.LostFoundNotifications
            .FirstOrDefaultAsync(n => n.NotificationId == id && n.UserId == userId);

        if (notification is null)
            return LostFoundResult.NotFound("Notification not found.");

        notification.IsRead = true;
        await _db.SaveChangesAsync();

        return LostFoundResult.Ok("Notification marked as read.");
    }

    public async Task MarkAllNotificationsReadAsync(int userId)
    {
        var unread = await _db.LostFoundNotifications
            .Where(n => n.UserId == userId && !n.IsRead)
            .ToListAsync();

        unread.ForEach(n => n.IsRead = true);
        await _db.SaveChangesAsync();
    }

    // =====================================================
    // WEB PUSH (VAPID)
    // =====================================================
    // Lost & Found keeps its own /api/lostfound/notifications/push/*
    // routes for backwards compatibility, but the store itself is the
    // shared PushSubscription table owned by PushSubscriptionService,
    // so a subscription enabled here also receives Library, Guidance and
    // Marketplace notifications. No Firebase anywhere in OneServe.
    // =====================================================

    public async Task<LostFoundResult> SubscribePushAsync(int userId, PushSubscriptionDto dto, string? userAgent)
    {
        var (success, message, _) =
            await _pushStore.SubscribeAsync(userId, dto, userAgent);

        return success
            ? LostFoundResult.Ok(message)
            : LostFoundResult.BadRequest(message);
    }

    public async Task<LostFoundResult> UnsubscribePushAsync(int userId, PushSubscriptionDto dto)
    {
        var (success, message, notFound) = await _pushStore.UnsubscribeAsync(userId,
            new PushUnsubscribeDto { Endpoint = dto.Endpoint });

        if (notFound) return LostFoundResult.NotFound(message);

        return success
            ? LostFoundResult.Ok(message)
            : LostFoundResult.BadRequest(message);
    }

    // =========================================================
    // ADMIN
    // =========================================================

    public async Task<AdminStatsDto> GetAdminStatsAsync()
    {
        var reports = _db.LostFoundReports;
        var claims = _db.ClaimRecords;
        var schedules = _db.ClaimSchedules;
        var recoveries = _db.RecoveryRecords;

        return new AdminStatsDto
        {
            TotalReports = await reports.CountAsync(),
            PendingReports = await reports.CountAsync(r => r.Status == "Pending"),
            PublishedReports = await reports.CountAsync(r => r.Status == "Published"),
            RejectedReports = await reports.CountAsync(r => r.Status == "Rejected"),
            ReturnedReports = await reports.CountAsync(r =>
                claims.Any(c => c.ReportId == r.ReportId && c.Status == "Returned")),
            TotalClaims = await claims.CountAsync(),
            ProcessingClaims = await claims.CountAsync(c => c.Status == "Processing"),
            AwaitingPickupClaims = await claims.CountAsync(c => c.Status == "Awaiting Pickup"),
            RejectedClaims = await claims.CountAsync(c => c.Status == "Rejected"),
            ReturnedClaims = await claims.CountAsync(c => c.Status == "Returned"),
            TotalSchedules = await schedules.CountAsync(),
            ScheduledPickups = await schedules.CountAsync(s => s.Status == "Scheduled"),
            CompletedPickups = await schedules.CountAsync(s => s.Status == "Completed"),
            TotalMatches = await _db.MatchRecords.CountAsync(),
            ConfirmedMatches = await _db.MatchRecords.CountAsync(m => m.IsConfirmed),
            TotalRecovery = await recoveries.CountAsync(),
            PendingRecovery = await recoveries.CountAsync(r => r.Status == "Pending"),
            AtAdminOfficeRecovery = await recoveries.CountAsync(r => r.Status == "AtAdminOffice"),
            CompletedRecovery = await recoveries.CountAsync(r => r.Status == "Completed"),
        };
    }

    public async Task<List<ReportDto>> GetAllReportsAsync() =>
        await _db.LostFoundReports
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => ToReportDto(r))
            .ToListAsync();

    /// <summary>
    /// One item's full console view: the report, its reporter, every
    /// claim on it, the latest recovery and every match it appears in.
    /// </summary>
    public async Task<LostFoundResult<AdminReportDetailDto>> GetAdminReportAsync(int id)
    {
        var report = await _db.LostFoundReports.FirstOrDefaultAsync(r => r.ReportId == id);
        if (report is null)
            return LostFoundResult<AdminReportDetailDto>.NotFound("Report not found.");

        var reporter = await _db.Users.FindAsync(report.UserId);

        var recoveryRecord = await _db.RecoveryRecords
            .Where(r => r.ReportId == id)
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync();

        RecoveryDto? recovery = null;
        if (recoveryRecord is not null)
        {
            var finder = await _db.Users.FindAsync(recoveryRecord.FinderUserId);
            recovery = new RecoveryDto
            {
                RecoveryId = recoveryRecord.RecoveryId,
                ReportId = recoveryRecord.ReportId,
                ItemName = report.ItemName,
                FinderUserId = recoveryRecord.FinderUserId,
                FinderName = finder?.FullName ?? string.Empty,
                FinderEmail = finder?.Email ?? string.Empty,
                RecoveryMethod = recoveryRecord.RecoveryMethod,
                Message = recoveryRecord.Message,
                ContactMethod = recoveryRecord.ContactMethod,
                ContactInfo = recoveryRecord.ContactInfo,
                Status = recoveryRecord.Status,
                CreatedAt = recoveryRecord.CreatedAt,
                CompletedAt = recoveryRecord.CompletedAt,
            };
        }

        var matches = await _db.MatchRecords
            .Include(m => m.LostReport)
            .Include(m => m.FoundReport)
            .Where(m => m.LostReportId == id || m.FoundReportId == id)
            .OrderByDescending(m => m.MatchPercentage)
            .Select(m => ToMatchDto(m))
            .ToListAsync();

        return LostFoundResult<AdminReportDetailDto>.Ok(new AdminReportDetailDto
        {
            Report = ToReportDto(report),
            ReporterName = reporter?.FullName ?? string.Empty,
            ReporterEmail = reporter?.Email ?? string.Empty,
            ReporterIdNumber = reporter?.IdNumber ?? string.Empty,
            Claims = await ToClaimAdminListAsync(id),
            Recovery = recovery,
            Matches = matches,
        });
    }

    public async Task<List<ClaimAdminDto>> GetAllClaimsAsync() =>
        await ToClaimAdminListAsync();

    // =========================================================
    // HELPERS
    // =========================================================

    private async Task<bool> IsLostFoundAdminAsync(int userId)
    {
        // The module owner role. OneServe's built-in Admin and
        // SuperAdmin also administer every module, so they pass too.
        if (userId <= 0)
            return false;

        var role = await _db.Users
            .Where(u => u.Id == userId)
            .Select(u => u.Role)
            .FirstOrDefaultAsync();

        return role == "LostFoundAdmin" || role == "Admin" || role == "SuperAdmin";
    }

    /// <summary>
    /// The keeper of the item a report describes: the report owner for
    /// a found item; the student who reported recovering it for a lost
    /// one (the recovery record is the handover's source of truth).
    /// </summary>
    private async Task<int?> GetActualFinderUserIdAsync(LostFoundReport report)
    {
        if (IsStatus(report.ReportType, "Found"))
            return report.UserId;

        if (IsStatus(report.ReportType, "Lost"))
        {
            var recovery = await _db.RecoveryRecords
                .Where(r => r.ReportId == report.ReportId && r.Status != "Rejected" && r.Status != "Completed")
                .OrderByDescending(r => r.CreatedAt)
                .FirstOrDefaultAsync();

            return recovery?.FinderUserId;
        }

        return null;
    }

    private async Task<List<ClaimAdminDto>> ToClaimAdminListAsync(int? reportId = null)
    {
        var query = _db.ClaimRecords
            .Include(c => c.Report)
            .AsQueryable();

        if (reportId.HasValue)
            query = query.Where(c => c.ReportId == reportId.Value);

        var claims = await query
            .OrderByDescending(c => c.ClaimDate)
            .ToListAsync();

        var claimantIds = claims.Select(c => c.ClaimedByUserId).Distinct().ToList();
        var claimants = await _db.Users
            .Where(u => claimantIds.Contains(u.Id))
            .ToListAsync();

        var finderIds = claims
            .Where(c => c.Report != null)
            .Select(c => c.Report.UserId)
            .Distinct()
            .ToList();

        var finders = await _db.Users
            .Where(u => finderIds.Contains(u.Id))
            .ToListAsync();

        return claims.Select(c =>
        {
            var claimant = claimants.FirstOrDefault(u => u.Id == c.ClaimedByUserId);
            var finder = c.Report is null ? null : finders.FirstOrDefault(u => u.Id == c.Report.UserId);

            return new ClaimAdminDto
            {
                ClaimId = c.ClaimId,
                ReportId = c.ReportId,
                ClaimedByUserId = c.ClaimedByUserId,
                ClaimantName = claimant?.FullName ?? string.Empty,
                ClaimantEmail = claimant?.Email ?? string.Empty,
                ItemName = c.Report != null ? c.Report.ItemName : string.Empty,
                Category = c.Report != null ? c.Report.Category : string.Empty,
                Location = c.Report != null ? c.Report.Location : string.Empty,
                ReportType = c.Report != null ? c.Report.ReportType : string.Empty,
                FinderName = finder?.FullName ?? string.Empty,
                FinderEmail = finder?.Email ?? string.Empty,
                ItemCustody = c.Report != null ? c.Report.ItemCustody : "None",
                ClaimDate = c.ClaimDate,
                VerificationNotes = c.VerificationNotes,
                Status = c.Status,
            };
        }).ToList();
    }

    private static bool IsPubliclyVisible(LostFoundReport report) =>
        IsStatus(report.Status, "Published") || IsStatus(report.Status, "Found");

    private static bool IsStatus(string? actual, string expected) =>
        string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);

    // =========================================================
    // PHILIPPINE WALL CLOCK
    //
    // Every datetime this module stores is a PH wall clock, and the PWA
    // parses it as local time - so what the admin picks is what the
    // student sees. PhNow() is NEVER written here: the database
    // has no time-zone column, so a UTC instant stored raw displays 8
    // hours early (a 9 AM window shows as 1 AM, "just now" reads as
    // "8 hours ago"). PH has no daylight saving, so +08:00 is permanent.
    // =========================================================

    private static DateTime PhNow() => DateTime.SpecifyKind(DateTime.UtcNow.AddHours(8), DateTimeKind.Unspecified);

    /// <summary>
    /// Incoming datetimes arrive as UTC instants (the PWA sends
    /// toISOString()). Store the PH wall clock, not the instant.
    /// </summary>
    private static DateTime ToPhWallClock(DateTime value)
    {
        var utc = value.Kind == DateTimeKind.Utc
            ? value
            : value.Kind == DateTimeKind.Local
                ? value.ToUniversalTime()
                : DateTime.SpecifyKind(value, DateTimeKind.Utc);

        return DateTime.SpecifyKind(utc.AddHours(8), DateTimeKind.Unspecified);
    }

    private static DateTime NormalizeDateTime(DateTime value) =>
        DateTime.SpecifyKind(
            new DateTime(value.Year, value.Month, value.Day, value.Hour, value.Minute, 0),
            DateTimeKind.Unspecified);

    /// <summary>
    /// Availability input: a UTC instant from the PWA, stored as the PH
    /// wall clock the admin actually picked.
    /// </summary>
    private static DateTime NormalizeFutureDateTime(DateTime value) =>
        ToPhWallClock(NormalizeDateTime(value));

    private static string NormalizeName(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : string.Join(" ", value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static ReportDto ToReportDto(LostFoundReport r) => new()
    {
        ReportId = r.ReportId,
        UserId = r.UserId,
        ItemName = r.ItemName,
        Description = r.Description,
        Category = r.Category,
        Location = r.Location,
        DateLostFound = r.DateLostFound,
        ReportType = r.ReportType,
        Status = r.Status,
        ImagePath = r.ImagePath,
        ItemCustody = string.IsNullOrWhiteSpace(r.ItemCustody) ? "None" : r.ItemCustody,
        CreatedAt = r.CreatedAt,
    };

    private static PickupAvailabilityDto ToAvailabilityDto(PickupAvailability a) => new()
    {
        AvailabilityId = a.AvailabilityId,
        StartDateTime = a.StartDateTime,
        EndDateTime = a.EndDateTime,
        IsActive = a.IsActive,
        CreatedAt = a.CreatedAt,
    };

    private async Task<ClaimScheduleDto> MapScheduleAsync(ClaimSchedule s)
    {
        var claimant = await _db.Users.FindAsync(s.ClaimantUserId);

        var report = s.Claim?.Report;
        var finder = report is null ? null : await _db.Users.FindAsync(report.UserId);

        return new ClaimScheduleDto
        {
            ScheduleId = s.ScheduleId,
            ClaimId = s.ClaimId,
            ClaimantName = claimant?.FullName ?? string.Empty,
            ClaimantEmail = claimant?.Email ?? string.Empty,
            AccountStudentId = claimant?.IdNumber ?? string.Empty,
            StudentId = s.StudentId,
            ItemName = report != null ? report.ItemName : string.Empty,
            ReportId = s.Claim?.ReportId ?? 0,
            ItemCustody = report != null ? report.ItemCustody : "None",
            FinderName = finder?.FullName ?? string.Empty,
            FinderEmail = finder?.Email ?? string.Empty,
            ScheduledDateTime = s.ScheduledDateTime,
            PickupLocation = s.PickupLocation,
            Status = s.Status,
            CreatedAt = s.CreatedAt,
            ReceivedByName = s.ReceivedByName,
            ReceivedByStudentId = s.ReceivedByStudentId,
            CompletedAt = s.CompletedAt,
            VerifiedAt = s.VerifiedAt,
            VerifiedByAdminId = s.VerifiedByAdminId,
        };
    }

    private static MatchDto ToMatchDto(MatchRecord m) => new()
    {
        MatchRecordId = m.MatchRecordId,
        LostReportId = m.LostReportId,
        FoundReportId = m.FoundReportId,
        LostItemName = m.LostReport != null ? m.LostReport.ItemName : string.Empty,
        FoundItemName = m.FoundReport != null ? m.FoundReport.ItemName : string.Empty,
        Location = m.FoundReport != null ? m.FoundReport.Location : (m.LostReport != null ? m.LostReport.Location : string.Empty),
        ImagePath = m.FoundReport != null ? m.FoundReport.ImagePath : null,
        CreatedAt = m.CreatedAt,
        IsConfirmed = m.IsConfirmed,
        MatchPercentage = m.MatchPercentage,
        NameScore = m.NameScore,
        CategoryScore = m.CategoryScore,
        LocationScore = m.LocationScore,
        DateScore = m.DateScore,
        LostReport = m.LostReport is null ? null : ToReportDto(m.LostReport),
        FoundReport = m.FoundReport is null ? null : ToReportDto(m.FoundReport),
    };
}
