namespace CDM_OneServe_API.Models.LostFound;

public class Notification
{
    // =========================================================
    // NOTIFICATION ID
    // =========================================================

    public int NotificationId { get; set; }


    // =========================================================
    // USER
    // =========================================================

    public int UserId { get; set; }


    // =========================================================
    // RELATED REPORT
    // =========================================================

    public int? ReportId { get; set; }


    // =========================================================
    // TITLE
    // =========================================================

    public string Title { get; set; } = string.Empty;


    // =========================================================
    // MESSAGE
    // =========================================================

    public string Message { get; set; } = string.Empty;


    // =========================================================
    // NOTIFICATION TYPE
    // =========================================================
    //
    // Examples:
    //
    // General
    // ClaimSubmitted
    // ClaimApproved
    // ClaimRejected
    // MatchFound
    // RecoveryReady
    // RecoveryCompleted
    // ReportUpdate
    //
    // String is intentional.
    // This lets us add notification types later without
    // changing an enum.
    // =========================================================

    public string NotificationType { get; set; } = "General";


    // =========================================================
    // TARGET URL
    // =========================================================
    //
    // Page opened when the user taps the notification.
    //
    // Examples:
    //
    // /Claims/MyClaims
    // /Notifications
    // /Reports/MyReports
    //
    // Nullable because some general notifications may not
    // have a destination.
    // =========================================================

    public string? TargetUrl { get; set; }


    // =========================================================
    // READ STATUS
    // =========================================================

    public bool IsRead { get; set; }


    // =========================================================
    // CREATED DATE
    // =========================================================

    public DateTime CreatedAt { get; set; }
        = DateTime.Now;
}