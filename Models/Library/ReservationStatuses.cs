namespace CDM_OneServe_API.Models.Library;

/// <summary>
/// Single source of truth for Reservation.Status values.
/// Stored as plain strings in reservations.Status, so no migration is needed.
/// </summary>
public static class ReservationStatuses
{
    /// <summary>Created by the mobile app. Auto-accepted, no approval needed.</summary>
    public const string Reserved = "Reserved";

    /// <summary>Older rows from the approval flow. Must be approved before pickup.</summary>
    public const string Pending = "Pending";

    /// <summary>Approved by the Library Head, but no copy set aside yet.</summary>
    public const string Approved = "Approved";

    /// <summary>The Library Head set one copy aside. AvailableCopies already went down by 1.</summary>
    public const string ReadyForPickup = "ReadyForPickup";

    /// <summary>
    /// Converted into a BorrowTransaction. The spec calls this "Completed";
    /// the stored value stays "Claimed" because existing rows and the mobile app use it.
    /// </summary>
    public const string Claimed = "Claimed";

    public const string Rejected = "Rejected";
    public const string Cancelled = "Cancelled";
    public const string Expired = "Expired";

    /// <summary>Still open: blocks duplicates, can expire, can be cancelled.</summary>
    public static readonly string[] Active = { Reserved, Pending, Approved, ReadyForPickup };

    /// <summary>Waiting for the library to act (approve or set a copy aside).</summary>
    public static readonly string[] AwaitingAction = { Reserved, Pending, Approved };

    /// <summary>Can be turned into a loan. Pending must be approved first.</summary>
    public static readonly string[] Claimable = { Reserved, Approved, ReadyForPickup };

    public static readonly string[] All =
        { Reserved, Pending, Approved, ReadyForPickup, Claimed, Rejected, Cancelled, Expired };

    public static bool IsActive(string? status) => status != null && Active.Contains(status);

    public static bool IsClaimable(string? status) => status != null && Claimable.Contains(status);

    /// <summary>Only ReadyForPickup holds a copy off the shelf.</summary>
    public static bool HoldsCopy(string? status) => status == ReadyForPickup;

    public static string Label(string? status) => status switch
    {
        ReadyForPickup => "Ready for Pickup",
        Claimed => "Completed",
        { Length: > 0 } value => value,
        _ => "Unknown"
    };
}
