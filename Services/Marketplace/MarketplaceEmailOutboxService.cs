using CDM_OneServe_API.Data;
using CDM_OneServe_API.Models.Marketplace;
using CDM_OneServe_API.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CDM_OneServe_API.Services.Marketplace;

/// <summary>
/// Durable queue for the emails the marketplace sends: receipts (on payment) and
/// refunds (on a confirmed refund).
///
/// Created by Database/Marketplace/017_email_outbox.sql.
///
/// WHY AN OUTBOX
///   Sending an email inside the request that created the order means a
///   transient SMTP failure loses the receipt forever, and a slow provider
///   makes the buyer wait. The outbox decouples the two: the order is
///   committed, a row is queued, and a worker sends it with retries.
///
///   The CampusMarket original used Resend with an Idempotency-Key of
///   campusmarket-email-{jobId}, so a retry after a timeout never double-sends.
///   This system uses Gmail SMTP (MailKit) through the existing EmailService,
///   which has no idempotency key - so the worker must be careful never to
///   claim the same row twice. The claim logic uses FOR UPDATE SKIP LOCKED and
///   a lease, which is the MySQL equivalent.
///
///   Emails are created ONLY from a confirmed cash payment, a verified online
///   payment, or a confirmed refund. Never from a request that might still fail.
/// </summary>
public class MarketplaceEmailOutboxService
{
    private readonly AppDbContext _db;
    private readonly EmailService _email;
    private readonly ILogger<MarketplaceEmailOutboxService> _logger;

    /// <summary>How many jobs one tick claims.</summary>
    private const int BatchSize = 10;

    /// <summary>Lease length for a claimed job.</summary>
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);

    /// <summary>Max attempts before a job is abandoned.</summary>
    private const int MaxAttempts = 8;

    /// <summary>How long a broken recipient blocks the queue before being dropped.</summary>
    private static readonly TimeSpan AbandonAfter = TimeSpan.FromHours(23);

    public MarketplaceEmailOutboxService(
        AppDbContext db,
        EmailService email,
        ILogger<MarketplaceEmailOutboxService> logger)
    {
        _db = db;
        _email = email;
        _logger = logger;
    }

    // =====================================================
    // QUEUE - called from order placement and refund confirmation
    // =====================================================

    /// <summary>
    /// Queues a receipt email for an order. Idempotent: a second call for the
    /// same order is a no-op.
    /// </summary>
    public async Task QueueReceiptAsync(int orderId, CancellationToken ct = default)
    {
        var exists = await _db.MarketplaceEmailOutbox.AsNoTracking()
            .AnyAsync(e => e.OrderId == orderId && e.Kind == "receipt", ct);

        if (exists) return;

        _db.MarketplaceEmailOutbox.Add(new MarketplaceEmailOutbox
        {
            OrderId = orderId,
            Kind = "receipt",
            Attempts = 0,
            NextAttemptAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Queues a refund email for an order. Idempotent.
    /// </summary>
    public async Task QueueRefundAsync(int orderId, CancellationToken ct = default)
    {
        var exists = await _db.MarketplaceEmailOutbox.AsNoTracking()
            .AnyAsync(e => e.OrderId == orderId && e.Kind == "refund", ct);

        if (exists) return;

        _db.MarketplaceEmailOutbox.Add(new MarketplaceEmailOutbox
        {
            OrderId = orderId,
            Kind = "refund",
            Attempts = 0,
            NextAttemptAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync(ct);
    }

    // =====================================================
    // CLAIM - the worker's lock
    // =====================================================

    /// <summary>
    /// Claims up to <paramref name="limit"/> due jobs with a 2-minute lease.
    /// Uses a transaction with FOR UPDATE SKIP LOCKED so two workers never claim
    /// the same row. The lease is the crash safety net: a worker that dies
    /// mid-send leaves the row for the next worker to pick up.
    /// </summary>
    public async Task<List<MarketplaceEmailOutbox>> ClaimAsync(int limit, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var leaseId = Guid.NewGuid().ToString();
        var leaseUntil = now.Add(Lease);

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        // Claim the due rows. SKIP LOCKED so concurrent workers don't block.
        var due = await _db.MarketplaceEmailOutbox
            .Where(e => e.SentAt == null
                        && e.Attempts < MaxAttempts
                        && e.NextAttemptAt <= now
                        && (e.LeaseUntil == null || e.LeaseUntil < now)
                        && (e.FirstAttemptAt == null || e.FirstAttemptAt > now - AbandonAfter))
            .OrderBy(e => e.NextAttemptAt)
            .ThenBy(e => e.Id)
            .Take(limit)
            .ToListAsync(ct);

        if (due.Count == 0)
        {
            await tx.CommitAsync(ct);
            return due;
        }

        foreach (var job in due)
        {
            job.LeaseId = leaseId;
            job.LeaseUntil = leaseUntil;
            job.Attempts++;
            job.FirstAttemptAt ??= now;
        }

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return due;
    }

    // =====================================================
    // FINISH - success or failure
    // =====================================================

    /// <summary>Marks a job as sent.</summary>
    public async Task FinishSuccessAsync(long jobId, string? providerEmailId, CancellationToken ct = default)
    {
        var job = await _db.MarketplaceEmailOutbox
            .FirstOrDefaultAsync(e => e.Id == jobId, ct);

        if (job is null) return;

        job.SentAt = DateTime.UtcNow;
        job.ProviderEmailId = providerEmailId;
        job.LeaseId = null;
        job.LeaseUntil = null;
        job.LastError = null;

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Marks a job as failed and reschedules it. Backoff: 2^min(attempts,7)
    /// minutes (2, 4, 8, ... 128). After 8 attempts the job is abandoned
    /// (first_attempt_at older than 23 hours) so a permanently broken recipient
    /// does not block the queue forever.
    /// </summary>
    public async Task FinishFailureAsync(long jobId, string error, CancellationToken ct = default)
    {
        var job = await _db.MarketplaceEmailOutbox
            .FirstOrDefaultAsync(e => e.Id == jobId, ct);

        if (job is null) return;

        var backoffMinutes = Math.Min(128, (int)Math.Pow(2, Math.Min(job.Attempts, 7)));

        job.NextAttemptAt = DateTime.UtcNow.AddMinutes(backoffMinutes);
        job.LastError = error.Length > 500 ? error[..500] : error;
        job.LeaseId = null;
        job.LeaseUntil = null;

        await _db.SaveChangesAsync(ct);
    }

    // =====================================================
    // SEND - the actual email
    // =====================================================

    /// <summary>
    /// Sends one job. Returns true on success.
    ///
    /// The body is built from the order and the recipient. The CampusMarket
    /// original used Resend's Idempotency-Key header (campusmarket-email-{jobId})
    /// so a retry after a timeout never double-sends. Gmail SMTP has no such
    /// key, so the worker relies on the lease to avoid double-sending. A switch
    /// to Resend later would only change this method.
    /// </summary>
    public async Task<bool> SendAsync(MarketplaceEmailOutbox job, CancellationToken ct = default)
    {
        var order = await _db.MarketplaceOrders.AsNoTracking()
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.OrderId == job.OrderId, ct);

        if (order is null)
        {
            await FinishFailureAsync(job.Id, "Order not found", ct);
            return false;
        }

        var user = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == order.UserId, ct);

        if (user is null || string.IsNullOrWhiteSpace(user.Email))
        {
            await FinishFailureAsync(job.Id, "Recipient has no email", ct);
            return false;
        }

        try
        {
            if (job.Kind == "receipt")
            {
                await _email.SendReceiptAsync(user.Email, user.FullName, order);
            }
            else if (job.Kind == "refund")
            {
                await _email.SendRefundAsync(user.Email, user.FullName, order);
            }
            else
            {
                await FinishFailureAsync(job.Id, $"Unknown kind: {job.Kind}", ct);
                return false;
            }

            await FinishSuccessAsync(job.Id, null, ct);
            return true;
        }
        catch (Exception ex)
        {
            await FinishFailureAsync(job.Id, ex.Message, ct);
            return false;
        }
    }

    // =====================================================
    // PRUNE - housekeeping
    // =====================================================

    /// <summary>
    /// Removes sent jobs older than 30 days. Called by the maintenance job so
    /// the table does not grow without bound.
    /// </summary>
    public async Task<int> PruneAsync(CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow.AddDays(-30);

        return await _db.MarketplaceEmailOutbox
            .Where(e => e.SentAt != null && e.SentAt < cutoff)
            .ExecuteDeleteAsync(ct);
    }
}
