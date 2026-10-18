// ============================================================
// CAMPUSMARKET - moderation
//
// Created by Database/Marketplace/015_moderation.sql.
//
// THREE THINGS, NOT ONE
//   Blocks are a USER action: "I do not want to see this person or store."
//   Reports are a USER action: "I am flagging this for an administrator."
//   Bans are an ADMIN action: "this account or store is not allowed here."
//   They are deliberately independent: a report never bans anything by itself,
//   and a block never removes anything. Only an administrator's decision does.
//
// MUTUAL HIDING
//   A store is hidden from a buyer when:
//     (a) the buyer blocked that store,
//     (b) the buyer blocked the store's owner, or
//     (c) the owner blocked the buyer.
//   The same rule hides a user's public profile. A block is therefore
//   symmetrical for visibility even though only one side creates it.
//
// BANS AND OPEN ORDERS
//   Banning an account or a store suspends every open order the buyer placed,
//   every open order at that store, and every open order at any store the
//   banned user owns:
//     unpaid  -> released and cancelled;
//     paid    -> return_requested + refund_pending, with an auto-approved
//                refund request (reason: "Order suspended by moderation").
//   Completed purchases stay in history. See 010_order_lifecycle.sql for the
//   stock-settle lock that makes this safe against concurrent cancellation.
// ============================================================

namespace CDM_OneServe_API.Models.Marketplace;

/// <summary>A block: either a user or a store, never both.</summary>
public class MarketplaceBlock
{
    public int BlockId { get; set; }

    public int UserId { get; set; }
    public User? User { get; set; }

    /// <summary>Null when this is a store block.</summary>
    public int? TargetUserId { get; set; }

    public User? TargetUser { get; set; }

    /// <summary>Null when this is a user block.</summary>
    public int? TargetStoreId { get; set; }

    public MarketplaceStore? TargetStore { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>A report: exactly one target, a reason, and a status.</summary>
public class MarketplaceReport
{
    public int ReportId { get; set; }

    public int ReporterId { get; set; }
    public User? Reporter { get; set; }

    public int? TargetUserId { get; set; }

    public User? TargetUser { get; set; }

    public int? TargetStoreId { get; set; }

    public MarketplaceStore? TargetStore { get; set; }

    public string Reason { get; set; } = "";

    /// <summary>open | reviewed | dismissed.</summary>
    public string Status { get; set; } = "open";

    public string? Resolution { get; set; }

    public int? ReviewedBy { get; set; }
    public User? ReviewedByUser { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReviewedAt { get; set; }
}

/// <summary>A support ticket opened by a user, answered by an administrator.</summary>
public class MarketplaceSupportTicket
{
    public int TicketId { get; set; }

    public int UserId { get; set; }
    public User? User { get; set; }

    public string Subject { get; set; } = "";

    public string Body { get; set; } = "";

    /// <summary>open | answered | closed.</summary>
    public string Status { get; set; } = "open";

    public string? Response { get; set; }

    public int? RespondedBy { get; set; }
    public User? RespondedByUser { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? RespondedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
}
