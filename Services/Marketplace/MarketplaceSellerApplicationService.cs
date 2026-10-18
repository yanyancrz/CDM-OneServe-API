using CDM_OneServe_API.Data;
using CDM_OneServe_API.DTOs.Marketplace;
using CDM_OneServe_API.Models;
using CDM_OneServe_API.Models.Marketplace;

using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Services.Marketplace;

/// <summary>
/// Seller onboarding: a verified Student or Faculty account expresses the
/// intent to sell, and the Marketplace Head reviews it.
///
/// Created by Database/Marketplace/012_seller_applications.sql.
///
/// WHY THIS IS NOT THE ZIP'S FLOW
///   CampusMarket grants a `seller` role on approval and the seller then creates
///   their own store. This system has ONE Role per user, so a seller cannot be
///   a Student at the same time. Store creation is also a Head act here, not a
///   seller act, because who sells where is a governance decision.
///
///   So the mapping is:
///     apply   -> "I would like to sell"
///     approve -> "you may, and I will give you a storefront"
///     reject  -> "not now, with a reason"
///   The Head then creates the storefront and assigns the applicant as owner via
///   the existing /stores endpoint. The application is the intent signal; the
///   storefront is the act.
///
/// IDENTITY GATE
///   An applicant must be a verified Student or Faculty account
///   (AccountStatus = 'Active', which is only set on physical ID approval by
///   /api/admin/registration-verification). The service refuses anything else,
///   so an unverified or non-buyer account cannot apply to sell.
/// </summary>
public class MarketplaceSellerApplicationService
{
    private readonly AppDbContext _db;
    private readonly MarketplaceStoreAccessService _access;

    private const int PageSize = 200;

    public MarketplaceSellerApplicationService(AppDbContext db, MarketplaceStoreAccessService access)
    {
        _db = db;
        _access = access;
    }

    // =====================================================
    // BUYER - apply
    // =====================================================

    /// <summary>
    /// Files an application to become a seller. Refuses with a 409 when one is
    /// already pending, so a double-tap cannot produce two applications.
    /// </summary>
    public async Task<MarketplaceResult<MarketplaceSellerApplicationDto>> ApplyAsync(
        int userId, string businessName, string? description, string? documentPath, CancellationToken ct = default)
    {
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);

        if (user is null)
            return MarketplaceResult<MarketplaceSellerApplicationDto>.Unauthorized("Please sign in to continue.");

        if (user.Role is not ("Student" or "Faculty"))
            return MarketplaceResult<MarketplaceSellerApplicationDto>.Forbidden(
                "Only Student and Faculty accounts can apply to become a seller.");

        if (user.AccountStatus != "Active")
            return MarketplaceResult<MarketplaceSellerApplicationDto>.Forbidden(
                "Your account must be verified before you can apply to sell.");

        var name = (businessName ?? "").Trim();
        if (name.Length is < 2 or > 100)
            return MarketplaceResult<MarketplaceSellerApplicationDto>.BadRequest(
                "Enter a business name (2-100 characters).");

        var pending = await _db.MarketplaceSellerApplications.AsNoTracking()
            .AnyAsync(a => a.UserId == userId && a.Status == MarketplaceSellerApplicationStatus.Pending, ct);

        if (pending)
            return MarketplaceResult<MarketplaceSellerApplicationDto>.Conflict(
                "You already have a seller application waiting for review.");

        var application = new MarketplaceSellerApplication
        {
            UserId = userId,
            BusinessName = name,
            Description = (description ?? "").Trim(),
            DocumentPath = string.IsNullOrWhiteSpace(documentPath) ? null : documentPath.Trim(),
            Status = MarketplaceSellerApplicationStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        _db.MarketplaceSellerApplications.Add(application);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // The UNIQUE PendingUserId index fired: a concurrent double-tap won.
            _db.Entry(application).State = EntityState.Detached;
            return MarketplaceResult<MarketplaceSellerApplicationDto>.Conflict(
                "You already have a seller application waiting for review.");
        }

        return MarketplaceResult<MarketplaceSellerApplicationDto>.Created(
            ToDto(application, user), "Seller application sent. The Marketplace Head will review it.");
    }

    /// <summary>Every application the signed-in account has filed.</summary>
    public async Task<MarketplaceResult<List<MarketplaceSellerApplicationDto>>> GetMineAsync(
        int userId, CancellationToken ct = default)
    {
        var rows = await _db.MarketplaceSellerApplications.AsNoTracking()
            .Include(a => a.User)
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(ct);

        return MarketplaceResult<List<MarketplaceSellerApplicationDto>>.Ok(
            rows.Select(a => ToDto(a, a.User)).ToList());
    }

    // =====================================================
    // HEAD - the review queue
    // =====================================================

    /// <summary>
    /// The Head's review queue. Pending first, oldest first, so the person who
    /// has waited longest is the one who gets answered first.
    /// </summary>
    public async Task<MarketplaceResult<List<MarketplaceSellerApplicationDto>>> ListAsync(
        int headId, string? status, CancellationToken ct = default)
    {
        if (!await IsHeadAsync(headId, ct))
            return MarketplaceResult<List<MarketplaceSellerApplicationDto>>.Forbidden(HeadOnly);

        var query = _db.MarketplaceSellerApplications.AsNoTracking()
            .Include(a => a.User)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
        {
            var wanted = MarketplaceSellerApplicationStatus.Normalize(status);
            query = query.Where(a => a.Status == wanted);
        }

        var rows = await query
            .OrderBy(a => a.Status == MarketplaceSellerApplicationStatus.Pending ? 0 : 1)
            .ThenBy(a => a.CreatedAt)
            .Take(PageSize)
            .ToListAsync(ct);

        return MarketplaceResult<List<MarketplaceSellerApplicationDto>>.Ok(
            rows.Select(a => ToDto(a, a.User)).ToList());
    }

    /// <summary>
    /// The Head's decision. Approving does not itself create a store - it records
    /// that the applicant may sell, and the Head then creates the storefront and
    /// assigns them as owner through the existing /stores endpoint. That keeps
    /// governance in one place: who sells where is the Head's call, not the
    /// applicant's.
    /// </summary>
    public async Task<MarketplaceResult<MarketplaceSellerApplicationDto>> ReviewAsync(
        int headId, int applicationId, bool approve, string? note, CancellationToken ct = default)
    {
        if (!await IsHeadAsync(headId, ct))
            return MarketplaceResult<MarketplaceSellerApplicationDto>.Forbidden(HeadOnly);

        var application = await _db.MarketplaceSellerApplications
            .Include(a => a.User)
            .FirstOrDefaultAsync(a => a.ApplicationId == applicationId, ct);

        if (application is null)
            return MarketplaceResult<MarketplaceSellerApplicationDto>.NotFound("That application does not exist.");

        if (application.Status != MarketplaceSellerApplicationStatus.Pending)
            return MarketplaceResult<MarketplaceSellerApplicationDto>.BadRequest(
                $"That application is already {application.Status}.");

        application.Status = approve
            ? MarketplaceSellerApplicationStatus.Approved
            : MarketplaceSellerApplicationStatus.Rejected;

        application.ReviewedBy = headId;
        application.ReviewedAt = DateTime.UtcNow;
        application.ReviewNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();

        await _db.SaveChangesAsync(ct);

        var message = approve
            ? $"Approved. Create a storefront and assign {application.User?.FullName ?? "them"} as owner."
            : "Seller application rejected.";

        return MarketplaceResult<MarketplaceSellerApplicationDto>.Ok(
            ToDto(application, application.User), message);
    }

    // =====================================================
    // INTERNAL
    // =====================================================

    private static MarketplaceSellerApplicationDto ToDto(
        MarketplaceSellerApplication a, User? user) => new()
    {
        ApplicationId = a.ApplicationId,
        UserId = a.UserId,
        FullName = user?.FullName ?? "",
        Email = user?.Email ?? "",
        BusinessName = a.BusinessName,
        Description = a.Description,
        DocumentPath = a.DocumentPath,
        Status = a.Status,
        ReviewNote = a.ReviewNote,
        CreatedAt = a.CreatedAt,
        ReviewedAt = a.ReviewedAt
    };

    private async Task<bool> IsHeadAsync(int userId, CancellationToken ct) =>
        await _db.Users.AsNoTracking().AnyAsync(
            u => u.Id == userId
                 && u.AccountStatus == "Active"
                 && u.Role == "MarketplaceAdmin"
                 && u.AdminModule == "Marketplace", ct);

    private const string HeadOnly = "Marketplace Head access required.";
}
