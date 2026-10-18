using CDM_OneServe_API.Data;
using CDM_OneServe_API.Models.Marketplace;

using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Services.Marketplace;

/// <summary>
/// Fixed-window rate limiting, backed by marketplace_rate_limits (016).
///
/// WHY THIS IS NOT A STORED PROCEDURE
///   A previous version of 016_rate_limiting.sql contained a MySQL stored
///   procedure (`consume_rate_limit`). Its execution crashed the MySQL server and
///   destroyed the cdm_oneserve database, taking every table and every row of
///   every module with it. There is no stored procedure here, and there will not
///   be one.
///
///   The logic is small enough to live in C# and is easier to test, to log, and
///   to reason about. It also means the limit can be changed without a
///   migration.
///
/// HOW IT WORKS
///   One row per key. The window resets when it is older than `seconds`; the
///   count is incremented otherwise. ConsumeAsync returns true when the call is
///   allowed, false when it is over the limit.
///
///   The key is hashed (SHA-256) before it is stored, so a database dump cannot
///   be used to enumerate which IP addresses or student numbers tried to log in.
///   The raw value never touches the database.
///
/// WHAT IS LIMITED (matching the CampusMarket spec)
///   * student-number login, per IP and per student number - 15 per 5 min / 8 per 5 min
///   * media uploads, per user - 20 per 5 min
///
///   The plain email/password login is NOT limited by this app: it relies on
///   ASP.NET's own rate limiting or the reverse proxy. place_order, report,
///   support, review_order and the rest are gated by authentication and by the
///   existing AccountStatus check, so they are not limited here.
/// </summary>
public class MarketplaceRateLimitService
{
    private readonly AppDbContext _db;

    /// <summary>How long a dead window is kept before being pruned.</summary>
    private static readonly TimeSpan DeadWindowRetention = TimeSpan.FromDays(1);

    public MarketplaceRateLimitService(AppDbContext db) => _db = db;

    /// <summary>
    /// Returns true when the call is allowed, false when it is over the limit.
    ///
    /// The key is hashed before storage. The caller passes the raw identifier
    /// (an IP address, a student number, a user id) and this method hashes it.
    /// </summary>
    public async Task<bool> ConsumeAsync(string key, int max, int windowSeconds, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(key)) return true;
        if (max <= 0) return false;

        var hashed = Hash(key);
        var now = DateTime.UtcNow;

        // Read the existing row, if any. This is a single-row lookup on the PK.
        var row = await _db.MarketplaceRateLimits
            .FirstOrDefaultAsync(r => r.Key == hashed, ct);

        if (row is null)
        {
            _db.MarketplaceRateLimits.Add(new MarketplaceRateLimit
            {
                Key = hashed,
                WindowStart = now,
                Count = 1
            });

            await _db.SaveChangesAsync(ct);
            return true;
        }

        // The window has expired: reset it.
        if (now - row.WindowStart > TimeSpan.FromSeconds(windowSeconds))
        {
            row.WindowStart = now;
            row.Count = 1;
            await _db.SaveChangesAsync(ct);
            return true;
        }

        // Still inside the window: increment and check.
        row.Count++;

        if (row.Count > max)
        {
            await _db.SaveChangesAsync(ct);
            return false;
        }

        await _db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Prunes rows older than a day. Called by the maintenance job so the table
    /// does not grow without bound.
    /// </summary>
    public async Task<int> PruneAsync(CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow - DeadWindowRetention;

        return await _db.MarketplaceRateLimits
            .Where(r => r.WindowStart < cutoff)
            .ExecuteDeleteAsync(ct);
    }

    private static string Hash(string raw)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(raw));

        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
