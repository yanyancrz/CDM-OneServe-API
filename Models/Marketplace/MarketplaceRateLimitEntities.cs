// ============================================================
// CAMPUSMARKET - rate limiting
//
// Created by Database/Marketplace/016_rate_limiting.sql.
//
// WHY THIS IS NOT A STORED PROCEDURE
//   A previous version of 016_rate_limiting.sql contained a MySQL stored
//   procedure (`consume_rate_limit`). Its execution crashed the MySQL server and
//   destroyed the cdm_oneserve database, taking every table and every row of
//   every module with it. There is no stored procedure here, and there will not
//   be one. The enforcement lives in MarketplaceRateLimitService (C#).
// ============================================================

namespace CDM_OneServe_API.Models.Marketplace;

/// <summary>
/// A fixed-window counter for one identifier. The key is already hashed
/// (SHA-256) before it is stored, so a database dump cannot be used to enumerate
/// which IP addresses or student numbers tried to log in.
/// </summary>
public class MarketplaceRateLimit
{
    /// <summary>The hashed identifier.</summary>
    public string Key { get; set; } = "";

    /// <summary>When the current window began.</summary>
    public DateTime WindowStart { get; set; }

    /// <summary>How many calls have been made in this window.</summary>
    public int Count { get; set; }
}
