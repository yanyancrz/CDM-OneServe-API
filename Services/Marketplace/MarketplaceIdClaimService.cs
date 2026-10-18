using CDM_OneServe_API.Data;
using CDM_OneServe_API.DTOs.Marketplace;
using CDM_OneServe_API.Models;
using CDM_OneServe_API.Models.Marketplace;
using CDM_OneServe_API.Services.Notifications;

using Microsoft.EntityFrameworkCore;
namespace CDM_OneServe_API.Services.Marketplace;

/// <summary>
/// The Student / Faculty campus ID claim.
///
/// Created by Database/Marketplace/003_campus_services.sql.
///
/// FLOW
///   Pending -> ReadyForClaim -> Claimed      (approved, collected)
///   Pending -> Rejected                      (turned down, with a reason)
///
///   Claimed and Rejected are final: a rejected claim must be answered with a
///   fresh request, never by quietly reopening the old one, or the history
///   stops explaining itself.
///
///   Exactly one claim per user may be open at a time. That is not enforced
///   here with a read-then-write check - the SQL has a UNIQUE index on a
///   generated OpenUserId column, so a double-tapped request fails at the
///   database with an error this service turns into a friendly 409.
/// </summary>
public class MarketplaceIdClaimService
{
    private readonly AppDbContext _db;
    private readonly MarketplaceAccessService _access;
    private readonly MarketplaceOperatorSessionService _sessions;
    private readonly IPushDispatcher _push;

    /// <summary>How many claims the staff queue returns in one page.</summary>
    private const int StaffPageSize = 200;

    public MarketplaceIdClaimService(
        AppDbContext db,
        MarketplaceAccessService access,
        MarketplaceOperatorSessionService sessions,
        IPushDispatcher push)
    {
        _db = db;
        _access = access;
        _sessions = sessions;
        _push = push;
    }

    // =====================================================
    // BUYER
    // =====================================================

    /// <summary>Every claim the signed-in student or faculty member has made.</summary>
    public async Task<MarketplaceResult<List<IdClaimDto>>> GetMyClaimsAsync(
        int userId, CancellationToken ct = default)
    {
        var buyer = await _access.RequireBuyerAsync(userId, ct);
        if (buyer.Actor is null)
            return MarketplaceAccessService.BuyerRefusal<List<IdClaimDto>>(buyer);

        var claims = await _db.MarketplaceIdClaims.AsNoTracking()
            .Include(c => c.User)
            .Include(c => c.ReviewedByUser)
            .Where(c => c.UserId == userId)
            .OrderByDescending(c => c.IdClaimId)
            .ToListAsync(ct);

        return MarketplaceResult<List<IdClaimDto>>.Ok(claims.Select(c => ToDto(c, c.User, c.ReviewedByUser)).ToList());
    }

    /// <summary>
    /// Files a new claim for a campus ID.
    ///
    /// Fails with a 409 when the person already has one open - that is the
    /// double-tap guard, and the message tells them to look at the claim they
    /// already have rather than silently replacing it.
    /// </summary>
    public async Task<MarketplaceResult<IdClaimDto>> RequestAsync(
        int userId, string claimType, string? reason, CancellationToken ct = default)
    {
        var buyer = await _access.RequireBuyerAsync(userId, ct);
        if (buyer.Actor is null)
            return MarketplaceAccessService.BuyerRefusal<IdClaimDto>(buyer);

        if (!IdClaimType.IsValid(claimType))
            return MarketplaceResult<IdClaimDto>.BadRequest("Choose what the ID claim is for.");

        var type = IdClaimType.Normalize(claimType);
        var text = (reason ?? "").Trim();

        if (IdClaimType.RequiresReason(type) && text.Length == 0)
            return MarketplaceResult<IdClaimDto>.BadRequest("Please say what happened to your ID.");

        // Two literal comparisons, not IdClaimStatus.IsOpen() - EF cannot
        // translate a C# method call inside a LINQ-to-SQL predicate.
        // This check is only a friendly early 409; the real single-open-claim
        // guard is the UNIQUE OpenUserId index in 003_campus_services.sql.
        var alreadyOpen = await _db.MarketplaceIdClaims.AsNoTracking()
            .AnyAsync(c =>
                c.UserId == userId
                && (c.Status == IdClaimStatus.Pending || c.Status == IdClaimStatus.ReadyForClaim), ct);

        if (alreadyOpen)
            return MarketplaceResult<IdClaimDto>.Conflict(
                "You already have an open ID claim. Wait for it to be settled before filing another.");

        var user = buyer.Actor.User;
        var claim = new MarketplaceIdClaim
        {
            UserId = userId,
            ClaimType = type,
            Reason = text.Length > 500 ? text[..500] : text,
            Status = IdClaimStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        _db.MarketplaceIdClaims.Add(claim);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // The UNIQUE OpenUserId index fired: a concurrent double-tap won the
            // race between our check and our insert.
            _db.Entry(claim).State = EntityState.Detached;
            return MarketplaceResult<IdClaimDto>.Conflict(
                "You already have an open ID claim. Wait for it to be settled before filing another.");
        }

        return MarketplaceResult<IdClaimDto>.Created(
            ToDto(claim, user, null), "Your ID claim was filed and is waiting for review.");
    }

    // =====================================================
    // STAFF (counter)
    // =====================================================

    /// <summary>
    /// The staff queue. Open claims first (oldest waiting longest at the top),
    /// then settled ones so the screen doubles as a history.
    /// </summary>
    public async Task<MarketplaceResult<List<IdClaimDto>>> ListForStaffAsync(
        int staffId, string? status, string? search, CancellationToken ct = default)
    {
        var actor = await _access.GetOperatorAsync(staffId, ct);
        if (actor is null) return MarketplaceResult<List<IdClaimDto>>.Forbidden(StaffOnly);

        var query = _db.MarketplaceIdClaims.AsNoTracking()
            .Include(c => c.User)
            .Include(c => c.ReviewedByUser)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
        {
            var wanted = status.Trim();
            query = query.Where(c => c.Status == wanted);
        }

        // Open claims first, then settled ones; oldest waiting longest at the top.
        //
        // The open/settled split is spelled out as two literal status
        // comparisons rather than IdClaimStatus.IsOpen(). EF translates the
        // query to SQL and cannot call a C# method that is not mapped to a
        // database function, so IsOpen() here would fail at runtime with
        // "The LINQ expression ... could not be translated".
        var claims = await query
            .OrderBy(c => c.Status == IdClaimStatus.Pending || c.Status == IdClaimStatus.ReadyForClaim ? 0 : 1)
            .ThenBy(c => c.CreatedAt)
            .Take(StaffPageSize)
            .ToListAsync(ct);

        var rows = claims.Select(c => ToDto(c, c.User, c.ReviewedByUser)).ToList();

        // Filtering by name/ID in memory, like the rest of the module: it is a
        // bounded page, and the server must not be used as a search engine over
        // the whole users table.
        var needle = (search ?? "").Trim();
        if (needle.Length > 0)
        {
            rows = rows.Where(r =>
                r.FullName.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || r.IdNumber.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || r.Email.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || (r.ClaimCode ?? "").Contains(needle, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        return MarketplaceResult<List<IdClaimDto>>.Ok(rows);
    }

    /// <summary>
    /// Staff decision on a claim.
    ///
    /// <paramref name="approve"/> from Pending  -> ReadyForClaim (a claim code is
    ///                                  issued so the person can collect)
    /// <paramref name="approve"/> from ReadyForClaim -> Claimed (they collected it)
    /// <paramref name="approve"/> false           -> Rejected, notes required
    ///
    /// The caller may only name the state the server already considers next, so
    /// a claim cannot be skipped straight from Pending to Claimed and an ID
    /// cannot be handed over without being released first.
    /// </summary>
    public async Task<MarketplaceResult<IdClaimDto>> ReviewAsync(
        int staffId, int idClaimId, bool approve, string? notes,
        Guid? sessionId, CancellationToken ct = default)
    {
        var actor = await _access.GetOperatorAsync(staffId, ct);
        if (actor is null) return MarketplaceResult<IdClaimDto>.Forbidden(StaffOnly);

        if (!actor.CanOperate) return MarketplaceResult<IdClaimDto>.Forbidden(StaffOnly);

        // Releasing or refusing an ID is a mutation, so it obeys the same rule
        // as every other staff write: a live operator session is required, and
        // the operator name is what lands on the audit trail.
        var (attribution, scopeError) = await _sessions.RequireMutationScopeAsync(
            staffId, sessionId, targetWorkspaceId: null, ct);

        if (attribution is null)
            return MarketplaceResult<IdClaimDto>.Forbidden(scopeError ?? StaffOnly);

        var claim = await _db.MarketplaceIdClaims
            .Include(c => c.User)
            .Include(c => c.ReviewedByUser)
            .FirstOrDefaultAsync(c => c.IdClaimId == idClaimId, ct);

        // A claim that is not there is a 404 even to staff - it does not need to
        // leak that the id existed and was deleted.
        if (claim is null) return MarketplaceResult<IdClaimDto>.NotFound("That ID claim does not exist.");

        var next = IdClaimStatus.Next(claim.Status, approve);
        if (next is null)
            return MarketplaceResult<IdClaimDto>.BadRequest(
                $"This claim is already {claim.Status} and cannot be changed.");

        var text = (notes ?? "").Trim();
        if (next == IdClaimStatus.Rejected && text.Length == 0)
            return MarketplaceResult<IdClaimDto>.BadRequest("Please say why the claim was rejected.");

        claim.Status = next;
        claim.ReviewedByUserId = staffId;
        claim.ReviewedAt = DateTime.UtcNow;
        claim.ReviewNotes = text.Length > 0 ? (text.Length > 500 ? text[..500] : text) : null;
        claim.UpdatedAt = DateTime.UtcNow;

        if (next == IdClaimStatus.ReadyForClaim)
            claim.ClaimCode = await NextClaimCodeAsync(ct);

        if (next == IdClaimStatus.Claimed) claim.ClaimedAt = DateTime.UtcNow;

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            return MarketplaceResult<IdClaimDto>.Conflict(
                "That claim could not be updated. Reload the queue and try again.");
        }

        // The claimant is told the moment the outcome changes - especially on
        // release, because that is when a claim code appears for them to bring.
        await NotifyAsync(claim, next, ct);

        var message = next switch
        {
            IdClaimStatus.ReadyForClaim => $"ID claim released. Claim code {claim.ClaimCode}.",
            IdClaimStatus.Claimed => "ID handed over.",
            IdClaimStatus.Rejected => "ID claim rejected.",
            _ => "ID claim updated."
        };

        return MarketplaceResult<IdClaimDto>.Ok(ToDto(claim, claim.User, claim.ReviewedByUser), message);
    }

    /// <summary>
    /// Counter lookup: the person types the code they were given and gets back
    /// the claim to release. Never returns the whole queue to sift through.
    /// </summary>
    public async Task<MarketplaceResult<IdClaimDto>> VerifyAsync(
        int staffId, string claimCode, CancellationToken ct = default)
    {
        var actor = await _access.GetOperatorAsync(staffId, ct);
        if (actor is null) return MarketplaceResult<IdClaimDto>.Forbidden(StaffOnly);

        var code = (claimCode ?? "").Trim().ToUpperInvariant();
        if (code.Length == 0) return MarketplaceResult<IdClaimDto>.BadRequest("Enter the claim code.");

        var claim = await _db.MarketplaceIdClaims.AsNoTracking()
            .Include(c => c.User)
            .Include(c => c.ReviewedByUser)
            .FirstOrDefaultAsync(c => c.ClaimCode == code, ct);

        if (claim is null) return MarketplaceResult<IdClaimDto>.NotFound("That claim code is not valid.");

        return MarketplaceResult<IdClaimDto>.Ok(ToDto(claim, claim.User, claim.ReviewedByUser));
    }

    // =====================================================
    // INTERNAL
    // =====================================================

    /// <summary>
    /// Tells the claimant the outcome.
    ///
    /// Wrapped so a push failure can NEVER fail a claim decision that has
    /// already been saved - the state change is the important part, the
    /// notification is a courtesy.
    /// </summary>
    private async Task NotifyAsync(
        MarketplaceIdClaim claim, string nextStatus, CancellationToken ct)
    {
        var (type, title, body) = nextStatus switch
        {
            IdClaimStatus.ReadyForClaim => (
                "id-claim-released",
                "Your campus ID is ready",
                $"Collect it with claim code {claim.ClaimCode}."),

            IdClaimStatus.Claimed => (
                "id-claim-claimed",
                "Campus ID collected",
                "Your campus ID has been handed over. Thanks."),

            IdClaimStatus.Rejected => (
                "id-claim-rejected",
                "ID claim rejected",
                string.IsNullOrWhiteSpace(claim.ReviewNotes)
                    ? "Open your ID claim page to see why."
                    : $"Reason: {claim.ReviewNotes}"),

            // Nothing to announce for any other transition.
            _ => (null, null, null)
        };

        if (type is null || title is null || body is null) return;

        try
        {
            await _push.EnqueueAsync(new PushNotification(
                UserId: claim.UserId,
                Module: "Marketplace",
                Type: type,
                Title: title,
                Body: body,
                TargetUrl: PushRoutes.Resolve("Marketplace", type),
                DedupeKey: $"idclaim:{claim.IdClaimId}:{nextStatus}"), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A push failure must never fail an ID claim decision.
        }
    }

    /// <summary>
    /// Mints an unused 8-character code.
    ///
    /// Only the ambiguous glyphs are dropped (0/O, 1/I/L) so the code can be
    /// read aloud across a counter without being misread. The UNIQUE index on
    /// ClaimCode is the real guard; this just makes a collision vanishingly
    /// unlikely before the insert.
    /// </summary>
    private async Task<string> NextClaimCodeAsync(CancellationToken ct)
    {
        const string Alphabet = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";

        for (var attempt = 0; attempt < 8; attempt++)
        {
            var chars = new char[8];
            for (var i = 0; i < chars.Length; i++)
                chars[i] = Alphabet[Random.Shared.Next(Alphabet.Length)];

            var code = new string(chars);

            var taken = await _db.MarketplaceIdClaims.AsNoTracking()
                .AnyAsync(c => c.ClaimCode == code, ct);

            if (!taken) return code;
        }

        // Eight collisions on a 32^8 space means something is badly wrong, not
        // merely unlucky - fail loudly rather than issue a duplicate.
        throw new InvalidOperationException("Could not allocate a unique ID claim code.");
    }

    /// <summary>
    /// Maps a claim to its DTO, computing the next legal status so the staff UI
    /// only ever offers a move the server will accept.
    /// </summary>
    private static IdClaimDto ToDto(
        MarketplaceIdClaim c, User? user, User? reviewer) => new()
    {
        IdClaimId = c.IdClaimId,
        UserId = c.UserId,
        FullName = user?.FullName ?? "",
        IdNumber = user?.IdNumber ?? "",
        AccountType = user?.Role ?? "",
        Email = user?.Email ?? "",
        ClaimType = c.ClaimType,
        ClaimTypeLabel = ClaimTypeLabel(c.ClaimType),
        Reason = c.Reason,
        Status = c.Status,
        ClaimCode = c.ClaimCode,
        ReviewNotes = c.ReviewNotes,
        ReviewedByName = reviewer?.FullName ?? "",
        ReviewedAt = c.ReviewedAt,
        ClaimedAt = c.ClaimedAt,
        CreatedAt = c.CreatedAt,
        NextStatus = IdClaimStatus.Next(c.Status, approve: true),
        CanReject = IdClaimStatus.IsAllowedNext(c.Status, approve: false),
        IsAwaitingRelease = c.Status == IdClaimStatus.Pending
    };

    private static string ClaimTypeLabel(string? type) => type switch
    {
        IdClaimType.FirstIssue => "First ID",
        IdClaimType.Lost => "Lost ID",
        IdClaimType.Damaged => "Damaged ID",
        _ => "Replacement ID"
    };

    private const string StaffOnly = "Marketplace Staff access required.";
}
