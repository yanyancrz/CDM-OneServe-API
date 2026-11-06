using System.Globalization;
using System.Security.Cryptography;

using CDM_OneServe_API.Authorization;
using CDM_OneServe_API.Data;
using CDM_OneServe_API.DTOs.Guidance;
using CDM_OneServe_API.Models.Guidance;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Services.Guidance;

public record NarrativeAttachmentDownload(string FilePath, string FileName, string ContentType);

public record NarrativeExportFile(byte[] Bytes, string FileName, string ContentType);

/// <summary>
/// Canonical narrative reports. A report is a guidance_counseling_records row;
/// this service adds the counselor workflow (draft -> finalize -> archive),
/// attachments stored outside wwwroot, an audit trail, exports and the
/// cross-counselor views the Guidance Head uses.
///
/// Authorization is always enforced here. A counselor id comes from the JWT
/// and every counselor query is scoped to it; the admin side is read-only.
/// </summary>
public class GuidanceNarrativeService
{
    private static readonly string[] AllowedStatuses = { "Draft", "Finalized", "Archived" };
    private static readonly string[] AttachmentExtensions =
        { ".pdf", ".doc", ".docx", ".txt", ".jpg", ".jpeg", ".png" };

    private const long MaxAttachmentBytes = 10 * 1024 * 1024;

    private readonly AppDbContext _context;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<GuidanceNarrativeService> _logger;

    public GuidanceNarrativeService(
        AppDbContext context,
        IWebHostEnvironment env,
        ILogger<GuidanceNarrativeService> logger)
    {
        _context = context;
        _env = env;
        _logger = logger;
    }

    // =====================================================
    // SUBJECTS (Student or Faculty from the existing Users table)
    // =====================================================

    public async Task<List<NarrativeSubjectOptionDto>> GetSubjectsAsync(
        string? search, int take, CancellationToken ct)
    {
        var query = _context.Users.AsNoTracking()
            .Where(u => (u.Role == GuidanceRoles.Student || u.Role == GuidanceRoles.Faculty)
                        && u.AccountStatus != "Deleted");

        var term = search?.Trim();
        if (!string.IsNullOrEmpty(term))
        {
            query = query.Where(u =>
                u.FullName.Contains(term) ||
                u.IdNumber.Contains(term) ||
                u.Email.Contains(term));
        }

        return await query
            .OrderBy(u => u.FullName)
            .Take(Math.Clamp(take, 1, 200))
            .Select(u => new NarrativeSubjectOptionDto
            {
                UserId = u.Id,
                IdNumber = u.IdNumber,
                FullName = u.FullName,
                Role = u.Role,
                Course = u.Course,
                YearLevel = u.YearLevel,
                Institute = u.Institute,
                Position = u.Position
            })
            .ToListAsync(ct);
    }

    // =====================================================
    // COUNSELOR
    // =====================================================

    public async Task<GuidancePage<NarrativeReportListItemDto>> GetMineAsync(
        int counselorId, NarrativeReportFilterDto filter, CancellationToken ct)
    {
        var query = _context.GuidanceCounselingRecords.AsNoTracking()
            .Where(r => r.CounselorId == counselorId);

        query = ApplyFilter(query, filter);

        var total = await query.CountAsync(ct);
        var page = Math.Max(filter.Page, 1);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);

        var rows = await ApplySort(query, filter.Sort)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Include(r => r.Student)
            .Include(r => r.Counselor)
            .Include(r => r.Attachments)
            .ToListAsync(ct);

        var items = rows.Select(r => ToListItem(r, r.Attachments.Count)).ToList();

        return new GuidancePage<NarrativeReportListItemDto>(
            items, page, (int)Math.Ceiling(total / (double)pageSize), total, pageSize);
    }

    public async Task<NarrativeReportStatsDto> GetMineStatsAsync(int counselorId, CancellationToken ct)
    {
        var query = _context.GuidanceCounselingRecords.AsNoTracking()
            .Where(r => r.CounselorId == counselorId);

        return new NarrativeReportStatsDto
        {
            Total = await query.CountAsync(ct),
            Draft = await query.CountAsync(r => r.Status == "Draft", ct),
            Finalized = await query.CountAsync(r => r.Status == "Finalized", ct),
            Archived = await query.CountAsync(r => r.Status == "Archived", ct),
            Confidential = await query.CountAsync(r => r.IsConfidential, ct),
            Counselors = 1,
            Subjects = await query.Select(r => r.StudentId).Distinct().CountAsync(ct)
        };
    }

    public async Task<GuidanceResult<NarrativeReportDetailDto>> GetMineDetailAsync(
        int counselorId, int id, CancellationToken ct)
    {
        var record = await _context.GuidanceCounselingRecords.AsNoTracking()
            .Include(r => r.Student)
            .Include(r => r.Counselor)
            .Include(r => r.Attachments).ThenInclude(a => a.UploadedByUser)
            .Include(r => r.Events).ThenInclude(e => e.Actor)
            .FirstOrDefaultAsync(r => r.Id == id && r.CounselorId == counselorId, ct);

        if (record == null)
            return GuidanceResult<NarrativeReportDetailDto>.Fail(404, "Report not found.");

        return GuidanceResult<NarrativeReportDetailDto>.Ok(
            ToDetail(record, includePrivateNotes: true, isCounselor: true));
    }

    public async Task<GuidanceResult<NarrativeReportDetailDto>> CreateAsync(
        int counselorId, CreateNarrativeReportRequest request, CancellationToken ct)
    {
        var status = NormalizeStatus(request.Status);
        if (status == null)
            return GuidanceResult<NarrativeReportDetailDto>.Fail(400, "Status must be Draft, Finalized or Archived.");

        if (status == "Archived")
            return GuidanceResult<NarrativeReportDetailDto>.Fail(400, "A new report cannot be created as archived.");

        if (status == "Finalized" &&
            string.IsNullOrWhiteSpace(request.SessionSummary) &&
            string.IsNullOrWhiteSpace(request.Observations) &&
            string.IsNullOrWhiteSpace(request.Interventions))
        {
            return GuidanceResult<NarrativeReportDetailDto>.Fail(400,
                "Write the session summary, observations or interventions before finalizing.");
        }

        var subject = await _context.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == request.StudentId &&
                                      (u.Role == GuidanceRoles.Student || u.Role == GuidanceRoles.Faculty), ct);

        if (subject == null)
            return GuidanceResult<NarrativeReportDetailDto>.Fail(404, "The selected student or faculty member was not found.");

        DateOnly sessionDate;
        if (string.IsNullOrWhiteSpace(request.SessionDate))
        {
            sessionDate = GuidanceClock.Today;
        }
        else if (!TryDate(request.SessionDate, out sessionDate))
        {
            return GuidanceResult<NarrativeReportDetailDto>.Fail(400, "Session date must be yyyy-MM-dd.");
        }

        if (sessionDate > GuidanceClock.Today)
            return GuidanceResult<NarrativeReportDetailDto>.Fail(400, "Session date cannot be in the future.");

        // An appointment link, when supplied, must belong to this counselor and subject.
        if (request.AppointmentId.HasValue)
        {
            var apptOk = await _context.GuidanceAppointments.AsNoTracking()
                .AnyAsync(a => a.Id == request.AppointmentId.Value &&
                               a.CounselorId == counselorId &&
                               a.StudentId == request.StudentId, ct);

            if (!apptOk)
                return GuidanceResult<NarrativeReportDetailDto>.Fail(400,
                    "The linked appointment does not belong to you and this subject.");
        }

        var record = new GuidanceCounselingRecord
        {
            AppointmentId = request.AppointmentId,
            StudentId = request.StudentId,
            CounselorId = counselorId,
            SessionDate = sessionDate,
            ConcernType = Trim(request.ConcernType),
            ReportTitle = Trim(request.ReportTitle) ?? DefaultTitle(subject.FullName, sessionDate),
            SessionSummary = Trim(request.SessionSummary),
            SessionObjectives = Trim(request.SessionObjectives),
            Observations = Trim(request.Observations),
            Interventions = Trim(request.Interventions),
            Recommendations = Trim(request.Recommendations),
            FollowUpPlan = Trim(request.FollowUpPlan),
            PrivateNotes = Trim(request.PrivateNotes),
            IsConfidential = request.IsConfidential,
            Status = "Draft",
            LastEditedBy = counselorId
        };

        _context.GuidanceCounselingRecords.Add(record);
        await _context.SaveChangesAsync(ct);

        if (status == "Finalized")
        {
            record.Status = "Finalized";
            record.FinalizedAt = DateTime.UtcNow;
            record.FinalizedBy = counselorId;
            record.ReportNumber = $"GR-{GuidanceClock.Today.Year}-{record.Id:D5}";
            record.UpdatedAt = DateTime.UtcNow;
        }

        AddEvent(record.Id, counselorId, GuidanceRoles.Counselor,
            status == "Finalized" ? "Finalized" : "Created",
            status == "Finalized" ? "Report created and finalized." : "Report created as a draft.");

        await _context.SaveChangesAsync(ct);

        return GuidanceResult<NarrativeReportDetailDto>.Created(
            await ReloadDetailAsync(counselorId, record.Id, includePrivateNotes: true, ct),
            status == "Finalized" ? "Report finalized." : "Draft saved.");
    }

    public async Task<GuidanceResult<NarrativeReportDetailDto>> UpdateAsync(
        int counselorId, int id, UpdateNarrativeReportRequest request, CancellationToken ct)
    {
        var record = await _context.GuidanceCounselingRecords
            .FirstOrDefaultAsync(r => r.Id == id && r.CounselorId == counselorId, ct);

        if (record == null)
            return GuidanceResult<NarrativeReportDetailDto>.Fail(404, "Report not found.");

        if (record.Status != "Draft")
            return GuidanceResult<NarrativeReportDetailDto>.Fail(409,
                $"This report is {record.Status.ToLowerInvariant()} and can no longer be edited. Create a new report instead.");

        if (!string.Equals(request.Status, "Draft", StringComparison.OrdinalIgnoreCase))
            return GuidanceResult<NarrativeReportDetailDto>.Fail(400,
                "Use the finalize endpoint to finalize a draft.");

        record.ReportTitle = Trim(request.ReportTitle);
        record.ConcernType = Trim(request.ConcernType);
        record.SessionSummary = Trim(request.SessionSummary);
        record.SessionObjectives = Trim(request.SessionObjectives);
        record.Observations = Trim(request.Observations);
        record.Interventions = Trim(request.Interventions);
        record.Recommendations = Trim(request.Recommendations);
        record.FollowUpPlan = Trim(request.FollowUpPlan);
        record.PrivateNotes = Trim(request.PrivateNotes);
        record.IsConfidential = request.IsConfidential;
        record.Version += 1;
        record.LastEditedBy = counselorId;
        record.UpdatedAt = DateTime.UtcNow;

        AddEvent(record.Id, counselorId, GuidanceRoles.Counselor, "Updated", $"Version {record.Version} saved.");

        await _context.SaveChangesAsync(ct);

        return GuidanceResult<NarrativeReportDetailDto>.Ok(
            await ReloadDetailAsync(counselorId, record.Id, includePrivateNotes: true, ct),
            "Draft updated.");
    }

    public async Task<GuidanceResult<NarrativeReportDetailDto>> FinalizeAsync(
        int counselorId, int id, CancellationToken ct)
    {
        var record = await _context.GuidanceCounselingRecords
            .FirstOrDefaultAsync(r => r.Id == id && r.CounselorId == counselorId, ct);

        if (record == null)
            return GuidanceResult<NarrativeReportDetailDto>.Fail(404, "Report not found.");

        if (record.Status == "Archived")
            return GuidanceResult<NarrativeReportDetailDto>.Fail(409, "This report is archived.");

        if (record.Status == "Finalized")
            return GuidanceResult<NarrativeReportDetailDto>.Ok(
                await ReloadDetailAsync(counselorId, record.Id, includePrivateNotes: true, ct),
                "Report is already finalized.");

        if (string.IsNullOrWhiteSpace(record.SessionSummary) &&
            string.IsNullOrWhiteSpace(record.Observations) &&
            string.IsNullOrWhiteSpace(record.Interventions))
        {
            return GuidanceResult<NarrativeReportDetailDto>.Fail(400,
                "Write the session summary, observations or interventions before finalizing.");
        }

        record.Status = "Finalized";
        record.FinalizedAt = DateTime.UtcNow;
        record.FinalizedBy = counselorId;
        record.ReportNumber = $"GR-{GuidanceClock.Today.Year}-{record.Id:D5}";
        record.UpdatedAt = DateTime.UtcNow;

        AddEvent(record.Id, counselorId, GuidanceRoles.Counselor, "Finalized",
            $"Report {record.ReportNumber} finalized.");

        await _context.SaveChangesAsync(ct);

        return GuidanceResult<NarrativeReportDetailDto>.Ok(
            await ReloadDetailAsync(counselorId, record.Id, includePrivateNotes: true, ct),
            "Report finalized. It is now read-only.");
    }

    public async Task<GuidanceResult<NarrativeReportDetailDto>> ArchiveAsync(
        int counselorId, int id, CancellationToken ct)
    {
        var record = await _context.GuidanceCounselingRecords
            .FirstOrDefaultAsync(r => r.Id == id && r.CounselorId == counselorId, ct);

        if (record == null)
            return GuidanceResult<NarrativeReportDetailDto>.Fail(404, "Report not found.");

        if (record.Status == "Archived")
            return GuidanceResult<NarrativeReportDetailDto>.Ok(
                await ReloadDetailAsync(counselorId, record.Id, includePrivateNotes: true, ct),
                "Report is already archived.");

        if (record.Status != "Finalized")
            return GuidanceResult<NarrativeReportDetailDto>.Fail(409,
                "Only a finalized report can be archived. Finalize the draft first.");

        record.Status = "Archived";
        record.ArchivedAt = DateTime.UtcNow;
        record.ArchivedBy = counselorId;
        record.UpdatedAt = DateTime.UtcNow;

        AddEvent(record.Id, counselorId, GuidanceRoles.Counselor, "Archived", "Report archived.");

        await _context.SaveChangesAsync(ct);

        return GuidanceResult<NarrativeReportDetailDto>.Ok(
            await ReloadDetailAsync(counselorId, record.Id, includePrivateNotes: true, ct),
            "Report archived.");
    }

    public async Task<GuidanceResult<object>> DeleteAsync(int counselorId, int id, CancellationToken ct)
    {
        var record = await _context.GuidanceCounselingRecords
            .Include(r => r.Attachments)
            .FirstOrDefaultAsync(r => r.Id == id && r.CounselorId == counselorId, ct);

        if (record == null)
            return GuidanceResult<object>.Fail(404, "Report not found.");

        if (record.Status != "Draft")
            return GuidanceResult<object>.Fail(409,
                "Only a draft can be deleted. Finalized and archived reports are retained for audit.");

        foreach (var attachment in record.Attachments)
            TryDeleteStoredFile(attachment);

        _context.GuidanceCounselingRecords.Remove(record);
        await _context.SaveChangesAsync(ct);

        return GuidanceResult<object>.Ok(new { }, "Draft deleted.");
    }

    // =====================================================
    // ATTACHMENTS
    // =====================================================

    public async Task<GuidanceResult<NarrativeAttachmentDto>> AddAttachmentAsync(
        int counselorId, int recordId, IFormFile file, CancellationToken ct)
    {
        var record = await _context.GuidanceCounselingRecords.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == recordId && r.CounselorId == counselorId, ct);

        if (record == null)
            return GuidanceResult<NarrativeAttachmentDto>.Fail(404, "Report not found.");

        if (record.Status != "Draft")
            return GuidanceResult<NarrativeAttachmentDto>.Fail(409,
                "Attachments can only be added while the report is a draft.");

        var validation = await ValidateAttachmentAsync(file, ct);
        if (validation != null)
            return GuidanceResult<NarrativeAttachmentDto>.Fail(400, validation);

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var storedName = Guid.NewGuid().ToString("N") + extension;
        var directory = GetAttachmentDirectory(recordId);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, storedName);

        byte[] content;
        await using (var input = file.OpenReadStream())
        await using (var buffer = new MemoryStream())
        {
            await input.CopyToAsync(buffer, ct);
            content = buffer.ToArray();
        }

        await File.WriteAllBytesAsync(path, content, ct);
        var hash = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

        var attachment = new GuidanceNarrativeAttachment
        {
            RecordId = recordId,
            FileName = Path.GetFileName(file.FileName),
            StoredName = storedName,
            ContentType = SafeContentType(file.ContentType, extension),
            SizeBytes = file.Length,
            Sha256 = hash,
            UploadedBy = counselorId
        };

        _context.GuidanceNarrativeAttachments.Add(attachment);
        AddEvent(recordId, counselorId, GuidanceRoles.Counselor, "AttachmentAdded",
            $"Attached {attachment.FileName} ({FormatSize(file.Length)}).");

        await _context.SaveChangesAsync(ct);

        var uploaderName = await _context.Users.AsNoTracking()
            .Where(u => u.Id == counselorId)
            .Select(u => u.FullName)
            .FirstOrDefaultAsync(ct) ?? "";

        return GuidanceResult<NarrativeAttachmentDto>.Created(new NarrativeAttachmentDto
        {
            Id = attachment.Id,
            RecordId = recordId,
            FileName = attachment.FileName,
            ContentType = attachment.ContentType,
            SizeBytes = attachment.SizeBytes,
            UploadedAt = attachment.UploadedAt,
            UploadedByName = uploaderName,
            DownloadUrl = $"/api/guidance/counselor/narrative-reports/{recordId}/attachments/{attachment.Id}/download"
        }, "Attachment uploaded.");
    }

    public async Task<GuidanceResult<object>> DeleteAttachmentAsync(
        int counselorId, int recordId, int attachmentId, CancellationToken ct)
    {
        var record = await _context.GuidanceCounselingRecords.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == recordId && r.CounselorId == counselorId, ct);

        if (record == null)
            return GuidanceResult<object>.Fail(404, "Report not found.");

        if (record.Status != "Draft")
            return GuidanceResult<object>.Fail(409, "Attachments are locked once the report is finalized.");

        var attachment = await _context.GuidanceNarrativeAttachments
            .FirstOrDefaultAsync(a => a.Id == attachmentId && a.RecordId == recordId, ct);

        if (attachment == null)
            return GuidanceResult<object>.Fail(404, "Attachment not found.");

        TryDeleteStoredFile(attachment);
        _context.GuidanceNarrativeAttachments.Remove(attachment);
        AddEvent(recordId, counselorId, GuidanceRoles.Counselor, "AttachmentDeleted",
            $"Removed {attachment.FileName}.");

        await _context.SaveChangesAsync(ct);
        return GuidanceResult<object>.Ok(new { }, "Attachment removed.");
    }

    public async Task<GuidanceResult<NarrativeAttachmentDownload>> DownloadAttachmentAsync(
        int actorId, string actorRole, bool isAdmin, int recordId, int attachmentId, CancellationToken ct)
    {
        var recordQuery = _context.GuidanceCounselingRecords.AsNoTracking().AsQueryable();

        // A counselor can only download their own report's files; the admin
        // side may read any report (read-only) and the download is audited.
        if (!isAdmin)
            recordQuery = recordQuery.Where(r => r.CounselorId == actorId);

        var record = await recordQuery.FirstOrDefaultAsync(r => r.Id == recordId, ct);
        if (record == null)
            return GuidanceResult<NarrativeAttachmentDownload>.Fail(404, "Report not found.");

        var attachment = await _context.GuidanceNarrativeAttachments.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == attachmentId && a.RecordId == recordId, ct);

        if (attachment == null)
            return GuidanceResult<NarrativeAttachmentDownload>.Fail(404, "Attachment not found.");

        var path = Path.Combine(GetAttachmentDirectory(recordId), attachment.StoredName);
        if (!File.Exists(path))
            return GuidanceResult<NarrativeAttachmentDownload>.Fail(404, "The stored file could not be found.");

        AddEvent(recordId, actorId, actorRole, "AttachmentDownloaded", $"Downloaded {attachment.FileName}.");
        await _context.SaveChangesAsync(ct);

        return GuidanceResult<NarrativeAttachmentDownload>.Ok(
            new NarrativeAttachmentDownload(path, attachment.FileName, attachment.ContentType));
    }

    // =====================================================
    // EXPORT (counselor)
    // =====================================================

    public async Task<GuidanceResult<NarrativeExportFile>> ExportMineAsync(
        int counselorId, int id, string format, CancellationToken ct)
    {
        var record = await LoadForExportAsync(id, counselorId, includePrivateNotes: true, ct);
        if (record == null)
            return GuidanceResult<NarrativeExportFile>.Fail(404, "Report not found.");

        var file = BuildExport(record, format, includePrivateNotes: true);
        if (file == null)
            return GuidanceResult<NarrativeExportFile>.Fail(400, "Format must be docx or pdf.");

        AddEvent(id, counselorId, GuidanceRoles.Counselor, "Exported",
            $"Exported {format.ToUpperInvariant()} with private notes.");
        await _context.SaveChangesAsync(ct);

        return GuidanceResult<NarrativeExportFile>.Ok(file);
    }

    // =====================================================
    // ADMIN (Guidance Head) - read only
    // =====================================================

    public async Task<GuidancePage<NarrativeReportListItemDto>> GetAdminListAsync(
        NarrativeReportFilterDto filter, CancellationToken ct)
    {
        var query = _context.GuidanceCounselingRecords.AsNoTracking().AsQueryable();
        query = ApplyFilter(query, filter);

        var total = await query.CountAsync(ct);
        var page = Math.Max(filter.Page, 1);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);

        var rows = await ApplySort(query, filter.Sort)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Include(r => r.Student)
            .Include(r => r.Counselor)
            .Include(r => r.Attachments)
            .ToListAsync(ct);

        var items = rows.Select(r => ToListItem(r, r.Attachments.Count)).ToList();

        return new GuidancePage<NarrativeReportListItemDto>(
            items, page, (int)Math.Ceiling(total / (double)pageSize), total, pageSize);
    }

    public async Task<NarrativeReportStatsDto> GetAdminStatsAsync(CancellationToken ct)
    {
        var query = _context.GuidanceCounselingRecords.AsNoTracking();

        return new NarrativeReportStatsDto
        {
            Total = await query.CountAsync(ct),
            Draft = await query.CountAsync(r => r.Status == "Draft", ct),
            Finalized = await query.CountAsync(r => r.Status == "Finalized", ct),
            Archived = await query.CountAsync(r => r.Status == "Archived", ct),
            Confidential = await query.CountAsync(r => r.IsConfidential, ct),
            Counselors = await query.Select(r => r.CounselorId).Distinct().CountAsync(ct),
            Subjects = await query.Select(r => r.StudentId).Distinct().CountAsync(ct)
        };
    }

    public async Task<GuidanceResult<NarrativeReportDetailDto>> GetAdminDetailAsync(
        int adminId, string? role, int id, bool recordView, CancellationToken ct)
    {
        var record = await _context.GuidanceCounselingRecords.AsNoTracking()
            .Include(r => r.Student)
            .Include(r => r.Counselor)
            .Include(r => r.Attachments).ThenInclude(a => a.UploadedByUser)
            .Include(r => r.Events).ThenInclude(e => e.Actor)
            .FirstOrDefaultAsync(r => r.Id == id, ct);

        if (record == null)
            return GuidanceResult<NarrativeReportDetailDto>.Fail(404, "Report not found.");

        if (recordView)
        {
            AddEvent(id, adminId, role ?? "GuidanceAdmin", "Viewed", "Guidance Head opened the report.");
            await _context.SaveChangesAsync(ct);

            // The entity above is AsNoTracking; reload only the events.
            var events = await _context.GuidanceNarrativeReportEvents.AsNoTracking()
                .Include(e => e.Actor)
                .Where(e => e.RecordId == id)
                .OrderByDescending(e => e.CreatedAt)
                .ToListAsync(ct);

            var dto = ToDetail(record, includePrivateNotes: false, isCounselor: false);
            dto.Events = events.Select(ToEventDto).ToList();
            return GuidanceResult<NarrativeReportDetailDto>.Ok(dto);
        }

        return GuidanceResult<NarrativeReportDetailDto>.Ok(
            ToDetail(record, includePrivateNotes: false, isCounselor: false));
    }

    public async Task<List<NarrativeReportEventDto>> GetEventsAsync(int id, CancellationToken ct)
    {
        return await _context.GuidanceNarrativeReportEvents.AsNoTracking()
            .Include(e => e.Actor)
            .Where(e => e.RecordId == id)
            .OrderByDescending(e => e.CreatedAt)
            .Select(e => new NarrativeReportEventDto
            {
                Id = e.Id,
                Action = e.Action,
                Detail = e.Detail,
                ActorName = e.Actor != null ? e.Actor.FullName : "System",
                ActorRole = e.ActorRole,
                CreatedAt = e.CreatedAt
            })
            .ToListAsync(ct);
    }

    public async Task<GuidanceResult<NarrativeExportFile>> ExportAdminAsync(
        int adminId, string? role, int id, string format, CancellationToken ct)
    {
        // Private notes are never included in a Guidance Head export.
        var record = await LoadForExportAsync(id, null, includePrivateNotes: false, ct);
        if (record == null)
            return GuidanceResult<NarrativeExportFile>.Fail(404, "Report not found.");

        var file = BuildExport(record, format, includePrivateNotes: false);
        if (file == null)
            return GuidanceResult<NarrativeExportFile>.Fail(400, "Format must be docx or pdf.");

        AddEvent(id, adminId, role ?? "GuidanceAdmin", "Exported",
            $"Guidance Head exported {format.ToUpperInvariant()}.");
        await _context.SaveChangesAsync(ct);

        return GuidanceResult<NarrativeExportFile>.Ok(file);
    }

    // =====================================================
    // HELPERS
    // =====================================================

    private async Task<NarrativeReportDetailDto> ReloadDetailAsync(
        int counselorId, int id, bool includePrivateNotes, CancellationToken ct)
    {
        var record = await _context.GuidanceCounselingRecords.AsNoTracking()
            .Include(r => r.Student)
            .Include(r => r.Counselor)
            .Include(r => r.Attachments).ThenInclude(a => a.UploadedByUser)
            .Include(r => r.Events).ThenInclude(e => e.Actor)
            .FirstAsync(r => r.Id == id && r.CounselorId == counselorId, ct);

        return ToDetail(record, includePrivateNotes, isCounselor: true);
    }

    private async Task<NarrativeReportDetailDto?> LoadForExportAsync(
        int id, int? counselorId, bool includePrivateNotes, CancellationToken ct)
    {
        var query = _context.GuidanceCounselingRecords.AsNoTracking()
            .Include(r => r.Student)
            .Include(r => r.Counselor)
            .Include(r => r.Attachments)
            .Where(r => r.Id == id);

        if (counselorId.HasValue)
            query = query.Where(r => r.CounselorId == counselorId.Value);

        var record = await query.FirstOrDefaultAsync(ct);
        return record == null ? null : ToDetail(record, includePrivateNotes, isCounselor: includePrivateNotes);
    }

    private static IQueryable<GuidanceCounselingRecord> ApplyFilter(
        IQueryable<GuidanceCounselingRecord> query, NarrativeReportFilterDto filter)
    {
        var status = filter.Status?.Trim();
        if (!string.IsNullOrEmpty(status) && !string.Equals(status, "All", StringComparison.OrdinalIgnoreCase))
            query = query.Where(r => r.Status == status);

        if (filter.StudentId.HasValue)
            query = query.Where(r => r.StudentId == filter.StudentId.Value);

        if (filter.CounselorId.HasValue)
            query = query.Where(r => r.CounselorId == filter.CounselorId.Value);

        var role = filter.SubjectRole?.Trim();
        if (!string.IsNullOrEmpty(role) && !string.Equals(role, "All", StringComparison.OrdinalIgnoreCase))
            query = query.Where(r => r.Student != null && r.Student.Role == role);

        if (TryDate(filter.From, out var from))
            query = query.Where(r => r.SessionDate >= from);

        if (TryDate(filter.To, out var to))
            query = query.Where(r => r.SessionDate <= to);

        var search = filter.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            query = query.Where(r =>
                (r.ReportTitle != null && r.ReportTitle.Contains(search)) ||
                (r.ReportNumber != null && r.ReportNumber.Contains(search)) ||
                (r.ConcernType != null && r.ConcernType.Contains(search)) ||
                (r.SessionSummary != null && r.SessionSummary.Contains(search)) ||
                (r.Student != null && (r.Student.FullName.Contains(search) || r.Student.IdNumber.Contains(search))) ||
                (r.Counselor != null && r.Counselor.FullName.Contains(search)));
        }

        return query;
    }

    private static IQueryable<GuidanceCounselingRecord> ApplySort(
        IQueryable<GuidanceCounselingRecord> query, string? sort) =>
        sort?.Trim().ToLowerInvariant() switch
        {
            "oldest" => query.OrderBy(r => r.SessionDate).ThenBy(r => r.Id),
            "subject" => query.OrderBy(r => r.Student!.FullName).ThenByDescending(r => r.SessionDate),
            "counselor" => query.OrderBy(r => r.Counselor!.FullName).ThenByDescending(r => r.SessionDate),
            _ => query.OrderByDescending(r => r.SessionDate).ThenByDescending(r => r.Id)
        };

    private static NarrativeReportListItemDto ToListItem(GuidanceCounselingRecord r, int attachmentCount) => new()
    {
        Id = r.Id,
        ReportNumber = r.ReportNumber,
        ReportTitle = r.ReportTitle,
        SubjectId = r.StudentId,
        SubjectName = r.Student?.FullName ?? "Unknown",
        SubjectIdNumber = r.Student?.IdNumber,
        SubjectRole = r.Student?.Role ?? "Student",
        CounselorId = r.CounselorId,
        CounselorName = r.Counselor?.FullName ?? "Counselor",
        SessionDate = D(r.SessionDate),
        ConcernType = r.ConcernType,
        Status = r.Status,
        IsConfidential = r.IsConfidential,
        HasPrivateNotes = !string.IsNullOrWhiteSpace(r.PrivateNotes),
        AttachmentCount = attachmentCount,
        Version = r.Version,
        FinalizedAt = r.FinalizedAt,
        CreatedAt = r.CreatedAt,
        UpdatedAt = r.UpdatedAt
    };

    private static NarrativeReportDetailDto ToDetail(
        GuidanceCounselingRecord r, bool includePrivateNotes, bool isCounselor) => new()
    {
        Id = r.Id,
        ReportNumber = r.ReportNumber,
        ReportTitle = r.ReportTitle,
        AppointmentId = r.AppointmentId,
        SubjectId = r.StudentId,
        SubjectName = r.Student?.FullName ?? "Unknown",
        SubjectIdNumber = r.Student?.IdNumber,
        SubjectRole = r.Student?.Role ?? "Student",
        CounselorId = r.CounselorId,
        CounselorName = r.Counselor?.FullName ?? "Counselor",
        SessionDate = D(r.SessionDate),
        ConcernType = r.ConcernType,
        Status = r.Status,
        IsConfidential = r.IsConfidential,
        HasPrivateNotes = !string.IsNullOrWhiteSpace(r.PrivateNotes),
        PrivateNotes = includePrivateNotes ? r.PrivateNotes : null,
        PrivateNotesRedacted = !includePrivateNotes && !string.IsNullOrWhiteSpace(r.PrivateNotes),
        AttachmentCount = r.Attachments.Count,
        Version = r.Version,
        FinalizedAt = r.FinalizedAt,
        ArchivedAt = r.ArchivedAt,
        CreatedAt = r.CreatedAt,
        UpdatedAt = r.UpdatedAt,
        SessionSummary = r.SessionSummary,
        SessionObjectives = r.SessionObjectives,
        Observations = r.Observations,
        Interventions = r.Interventions,
        Recommendations = r.Recommendations,
        FollowUpPlan = r.FollowUpPlan,
        Attachments = r.Attachments.Select(a => new NarrativeAttachmentDto
        {
            Id = a.Id,
            RecordId = a.RecordId,
            FileName = a.FileName,
            ContentType = a.ContentType,
            SizeBytes = a.SizeBytes,
            UploadedAt = a.UploadedAt,
            UploadedByName = a.UploadedByUser?.FullName ?? "Counselor",
            DownloadUrl = isCounselor
                ? $"/api/guidance/counselor/narrative-reports/{r.Id}/attachments/{a.Id}/download"
                : $"/api/admin/guidance/narrative-reports/{r.Id}/attachments/{a.Id}/download"
        }).ToList(),
        Events = r.Events.OrderByDescending(e => e.CreatedAt).Select(ToEventDto).ToList()
    };

    private static NarrativeReportEventDto ToEventDto(GuidanceNarrativeReportEvent e) => new()
    {
        Id = e.Id,
        Action = e.Action,
        Detail = e.Detail,
        ActorName = e.Actor?.FullName ?? "System",
        ActorRole = e.ActorRole,
        CreatedAt = e.CreatedAt
    };

    private void AddEvent(int recordId, int? actorId, string actorRole, string action, string? detail)
    {
        _context.GuidanceNarrativeReportEvents.Add(new GuidanceNarrativeReportEvent
        {
            RecordId = recordId,
            ActorId = actorId,
            ActorRole = actorRole,
            Action = action,
            Detail = detail
        });
    }

    private static NarrativeExportFile? BuildExport(
        NarrativeReportDetailDto report, string format, bool includePrivateNotes)
    {
        var normalized = format?.Trim().ToLowerInvariant();
        return normalized switch
        {
            "docx" => new NarrativeExportFile(
                GuidanceNarrativeExportService.BuildDocx(report, includePrivateNotes),
                GuidanceNarrativeExportService.FileName(report, "docx"),
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document"),
            "pdf" => new NarrativeExportFile(
                GuidanceNarrativeExportService.BuildPdf(report, includePrivateNotes),
                GuidanceNarrativeExportService.FileName(report, "pdf"),
                "application/pdf"),
            _ => null
        };
    }

    private string GetAttachmentDirectory(int recordId) =>
        Path.Combine(_env.ContentRootPath, "App_Data", "guidance-narrative", recordId.ToString(CultureInfo.InvariantCulture));

    private void TryDeleteStoredFile(GuidanceNarrativeAttachment attachment)
    {
        try
        {
            var path = Path.Combine(GetAttachmentDirectory(attachment.RecordId), attachment.StoredName);
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Guidance narrative: could not delete stored attachment {Id}.", attachment.Id);
        }
    }

    private static async Task<string?> ValidateAttachmentAsync(IFormFile file, CancellationToken ct)
    {
        if (file == null || file.Length == 0) return "No file uploaded.";
        if (file.Length > MaxAttachmentBytes) return "Attachment must be 10 MB or smaller.";

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AttachmentExtensions.Contains(extension))
            return "Only PDF, DOC, DOCX, TXT, JPG and PNG files are allowed.";

        // Light magic-byte validation so a renamed executable is rejected.
        var header = new byte[8];
        await using var stream = file.OpenReadStream();
        var read = await stream.ReadAsync(header.AsMemory(0, header.Length), ct);

        bool StartsWith(params byte[] signature) =>
            read >= signature.Length && signature.Select((b, i) => header[i] == b).All(ok => ok);

        var valid = extension switch
        {
            ".pdf" => StartsWith(0x25, 0x50, 0x44, 0x46),                       // %PDF
            ".docx" => StartsWith(0x50, 0x4B),                                   // ZIP
            ".doc" => StartsWith(0xD0, 0xCF, 0x11, 0xE0),                        // OLE
            ".png" => StartsWith(0x89, 0x50, 0x4E, 0x47),
            ".jpg" or ".jpeg" => StartsWith(0xFF, 0xD8, 0xFF),
            ".txt" => true,
            _ => false
        };

        return valid ? null : "The file content does not match its extension.";
    }

    private static string SafeContentType(string? contentType, string extension)
    {
        // Never trust the browser-supplied content type: it can be text/html and
        // would make the authenticated download endpoint render active content.
        // The stored extension is validated against the allow-list already.
        _ = contentType;

        return extension switch
        {
            ".pdf" => "application/pdf",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".doc" => "application/msword",
            ".txt" => "text/plain",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            _ => "application/octet-stream"
        };
    }

    private static string? NormalizeStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return "Draft";
        return AllowedStatuses.FirstOrDefault(
            s => string.Equals(s, status.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string DefaultTitle(string subjectName, DateOnly date) =>
        $"Counseling Narrative Report - {subjectName} ({date:yyyy-MM-dd})";

    private static string D(DateOnly d) =>
        d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static bool TryDate(string? value, out DateOnly date) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    private static string FormatSize(long bytes) =>
        bytes >= 1024 * 1024
            ? $"{bytes / (1024d * 1024d):0.0} MB"
            : $"{Math.Max(1, Math.Round(bytes / 1024d)):0} KB";
}
