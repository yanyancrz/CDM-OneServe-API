using System.ComponentModel.DataAnnotations;

namespace CDM_OneServe_API.DTOs.Guidance;

// =====================================================
// Narrative reports
//
// The canonical report lives in guidance_counseling_records. These DTOs are
// shared by the counselor (mobile) and Guidance Head (desktop) endpoints.
// =====================================================

public class NarrativeReportListItemDto
{
    public int Id { get; set; }
    public string? ReportNumber { get; set; }
    public string? ReportTitle { get; set; }

    public int SubjectId { get; set; }
    public string SubjectName { get; set; } = "";
    public string? SubjectIdNumber { get; set; }
    /// <summary>Student or Faculty (the subject's live Users.Role).</summary>
    public string SubjectRole { get; set; } = "Student";

    public int CounselorId { get; set; }
    public string CounselorName { get; set; } = "";

    public string SessionDate { get; set; } = "";
    public string? ConcernType { get; set; }
    public string Status { get; set; } = "Draft";
    public bool IsConfidential { get; set; }
    public bool HasPrivateNotes { get; set; }
    public int AttachmentCount { get; set; }
    public int Version { get; set; }

    public DateTime? FinalizedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class NarrativeReportDetailDto : NarrativeReportListItemDto
{
    public int? AppointmentId { get; set; }

    public string? SessionSummary { get; set; }
    public string? SessionObjectives { get; set; }
    public string? Observations { get; set; }
    public string? Interventions { get; set; }
    public string? Recommendations { get; set; }
    public string? FollowUpPlan { get; set; }

    /// <summary>Null when the caller is not allowed to read private notes.</summary>
    public string? PrivateNotes { get; set; }
    public bool PrivateNotesRedacted { get; set; }

    public DateTime? ArchivedAt { get; set; }
    public List<NarrativeAttachmentDto> Attachments { get; set; } = new();
    public List<NarrativeReportEventDto> Events { get; set; } = new();
}

public class NarrativeAttachmentDto
{
    public int Id { get; set; }
    public int RecordId { get; set; }
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long SizeBytes { get; set; }
    public DateTime UploadedAt { get; set; }
    public string UploadedByName { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
}

public class NarrativeReportEventDto
{
    public long Id { get; set; }
    public string Action { get; set; } = "";
    public string? Detail { get; set; }
    public string ActorName { get; set; } = "";
    public string ActorRole { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

// ---------------- requests ----------------

public class CreateNarrativeReportRequest
{
    [Required]
    public int StudentId { get; set; }

    /// <summary>Optional link to one of the counselor's existing appointments.</summary>
    public int? AppointmentId { get; set; }

    /// <summary>yyyy-MM-dd. Defaults to today.</summary>
    public string? SessionDate { get; set; }

    [StringLength(200)]
    public string? ReportTitle { get; set; }

    [StringLength(120)]
    public string? ConcernType { get; set; }

    [StringLength(8000)]
    public string? SessionSummary { get; set; }

    [StringLength(8000)]
    public string? SessionObjectives { get; set; }

    [StringLength(8000)]
    public string? Observations { get; set; }

    [StringLength(8000)]
    public string? Interventions { get; set; }

    [StringLength(8000)]
    public string? Recommendations { get; set; }

    [StringLength(8000)]
    public string? FollowUpPlan { get; set; }

    [StringLength(8000)]
    public string? PrivateNotes { get; set; }

    public bool IsConfidential { get; set; }

    /// <summary>Draft (default) or Finalized. Finalizing here also stamps the report.</summary>
    public string Status { get; set; } = "Draft";
}

public class UpdateNarrativeReportRequest
{
    [StringLength(200)]
    public string? ReportTitle { get; set; }

    [StringLength(120)]
    public string? ConcernType { get; set; }

    [StringLength(8000)]
    public string? SessionSummary { get; set; }

    [StringLength(8000)]
    public string? SessionObjectives { get; set; }

    [StringLength(8000)]
    public string? Observations { get; set; }

    [StringLength(8000)]
    public string? Interventions { get; set; }

    [StringLength(8000)]
    public string? Recommendations { get; set; }

    [StringLength(8000)]
    public string? FollowUpPlan { get; set; }

    [StringLength(8000)]
    public string? PrivateNotes { get; set; }

    public bool IsConfidential { get; set; }

    /// <summary>Only Draft is accepted here; use /finalize to finalize.</summary>
    public string Status { get; set; } = "Draft";
}

public class NarrativeReportFilterDto
{
    public string? Search { get; set; }
    /// <summary>empty = any, otherwise Draft / Finalized / Archived.</summary>
    public string? Status { get; set; }
    public int? StudentId { get; set; }
    public int? CounselorId { get; set; }
    /// <summary>Student or Faculty.</summary>
    public string? SubjectRole { get; set; }
    /// <summary>yyyy-MM-dd, matches SessionDate.</summary>
    public string? From { get; set; }
    /// <summary>yyyy-MM-dd, matches SessionDate.</summary>
    public string? To { get; set; }
    /// <summary>newest (default) | oldest | subject | counselor.</summary>
    public string? Sort { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class NarrativeSubjectOptionDto
{
    public int UserId { get; set; }
    public string IdNumber { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Role { get; set; } = "Student";
    public string? Course { get; set; }
    public string? YearLevel { get; set; }
    public string? Institute { get; set; }
    public string? Position { get; set; }
}

public class NarrativeReportStatsDto
{
    public int Total { get; set; }
    public int Draft { get; set; }
    public int Finalized { get; set; }
    public int Archived { get; set; }
    public int Confidential { get; set; }
    public int Counselors { get; set; }
    public int Subjects { get; set; }
}
