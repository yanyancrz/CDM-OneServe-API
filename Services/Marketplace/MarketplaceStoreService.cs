using CDM_OneServe_API.Data;
using CDM_OneServe_API.DTOs.Marketplace;
using CDM_OneServe_API.Models;
using CDM_OneServe_API.Models.Marketplace;

using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Services.Marketplace;

/// <summary>
/// Storefronts: browsing, the public page, the team, and the categories and
/// photo gallery that go with a storefront.
///
/// Created by Database/Marketplace/006_storefronts.sql.
///
/// A STORE IS A LAYER, NOT A REPLACEMENT
///   Products, stock and orders stay where they already are - workspace-scoped.
///   A store points at one workspace through
///   <see cref="MarketplaceStore.WorkspaceId"/> and is browsed separately. So a
///   store can be paused or banned to stop people ordering, without touching a
///   single inventory or order row.
///
/// THE SELLING RULES, matching the CampusMarket spec
///   * a store must be Status=active for a buyer to order from it;
///   * a banned store's open orders are suspended by the moderation phase, not
///     here;
///   * the owner may pause and reopen their own store; an administrator bans.
///     Unbanning lands on paused, so reopening is always the owner's decision.
///
/// NO ONLINE PAYMENTS. Cash and campus card only.
/// </summary>
public class MarketplaceStoreService
{
    private readonly AppDbContext _db;
    private readonly MarketplaceStoreAccessService _access;

    /// <summary>How many storefronts one browse call returns.</summary>
    private const int BrowsePageSize = 100;

    /// <summary>How many applications the team screen pulls at once.</summary>
    private const int ApplicationPageSize = 200;

    /// <summary>The gallery limit per product, from the CampusMarket spec.</summary>
    public const int MaxProductPhotos = 8;

    public MarketplaceStoreService(AppDbContext db, MarketplaceStoreAccessService access)
    {
        _db = db;
        _access = access;
    }

    // =====================================================
    // BUYER - browse and the public page
    // =====================================================

    /// <summary>
    /// Storefronts a buyer may see, most popular first.
    ///
    /// Paused stores stay listed but are flagged not orderable, so a buyer who
    /// followed a stall link is told it is paused rather than hitting a 404.
    /// Banned and deleted stores never appear.
    /// </summary>
    public async Task<MarketplaceResult<List<MarketplaceStoreDto>>> BrowseAsync(
        string? kind, string? search, CancellationToken ct = default)
    {
        var query = _db.MarketplaceStores.AsNoTracking()
            .Include(s => s.OwnerUser)
            .Where(s => s.IsActive
                        && s.Status != MarketplaceStoreStatus.Deleted
                        && s.Status != MarketplaceStoreStatus.Banned);

        if (!string.IsNullOrWhiteSpace(kind) && MarketplaceStoreKind.IsValid(kind))
        {
            var wanted = MarketplaceStoreKind.Normalize(kind);
            query = query.Where(s => s.Kind == wanted);
        }

        var stores = await query
            .OrderBy(s => s.SortOrder)
            .ThenByDescending(s => s.CreatedAt)
            .Take(BrowsePageSize)
            .ToListAsync(ct);

        // Sales figures come from COMPLETED, PAID orders only - never from
        // cancelled ones, and never from unpaid ones. Same rule the CampusMarket
        // "trends by completed sales" uses.
        var sales = await SalesByStoreAsync(stores.Select(s => s.StoreId).ToList(), ct);
        var products = await ProductCountsByStoreAsync(stores.Select(s => s.StoreId).ToList(), ct);

        // No caller in this method, so nobody is an owner or a member here.
        var rows = stores.Select(s => ToDto(
            s, isOwner: false, permissions: Array.Empty<string>(),
            sales.GetValueOrDefault(s.StoreId),
            products.GetValueOrDefault(s.StoreId))).ToList();

        // Client-side search, like the rest of the module: this is a bounded
        // page, and the server is not a search engine over the whole catalogue.
        var needle = (search ?? "").Trim();
        if (needle.Length > 0)
        {
            rows = rows.Where(r =>
                r.Name.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || r.Description.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || r.Location.Contains(needle, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        rows = rows
            .OrderByDescending(r => r.SalesCentavos)
            .ThenBy(r => r.Name)
            .ToList();

        return MarketplaceResult<List<MarketplaceStoreDto>>.Ok(rows);
    }

    /// <summary>
    /// One storefront with its caller's own permissions attached, so the same
    /// payload serves the buyer page and the management screen.
    /// </summary>
    public async Task<MarketplaceResult<MarketplaceStoreDto>> GetAsync(
        int userId, int storeId, CancellationToken ct = default)
    {
        var actor = await _access.GetActorAsync(userId, storeId, ct);
        if (actor is null) return MarketplaceResult<MarketplaceStoreDto>.NotFound("That store does not exist.");

        var store = await _db.MarketplaceStores.AsNoTracking()
            .Include(s => s.OwnerUser)
            .FirstAsync(s => s.StoreId == storeId, ct);

        // A buyer must not be able to read a banned or deleted storefront.
        var isStaff = actor.IsStaff;
        if (!isStaff &&
            store.Status is MarketplaceStoreStatus.Banned or MarketplaceStoreStatus.Deleted)
            return MarketplaceResult<MarketplaceStoreDto>.NotFound("That store does not exist.");

        var sales = await SalesByStoreAsync(new List<int> { storeId }, ct);
        var products = await ProductCountsByStoreAsync(new List<int> { storeId }, ct);

        return MarketplaceResult<MarketplaceStoreDto>.Ok(ToDto(
            store, actor.IsOwner, actor.Permissions,
            sales.GetValueOrDefault(storeId), products.GetValueOrDefault(storeId)));
    }

    // =====================================================
    // HEAD - create and edit storefronts
    // =====================================================

    /// <summary>
    /// EVERY storefront, including banned and deleted ones.
    ///
    /// <see cref="BrowseAsync"/> deliberately hides banned and deleted
    /// storefronts from buyers. The Head needs the opposite: an administrator
    /// who cannot see a banned store can never unban it, because the row that
    /// carries the status is the only place the status lives.
    /// </summary>
    public async Task<MarketplaceResult<List<MarketplaceStoreDto>>> ListAllForHeadAsync(
        int headId, CancellationToken ct = default)
    {
        if (!await IsHeadAsync(headId, ct))
            return MarketplaceResult<List<MarketplaceStoreDto>>.Forbidden(HeadOnly);

        var stores = await _db.MarketplaceStores.AsNoTracking()
            .Include(s => s.OwnerUser)
            .Include(s => s.Workspace)
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.Name)
            .ToListAsync(ct);

        var storeIds = stores.Select(s => s.StoreId).ToList();
        var sales = await SalesByStoreAsync(storeIds, ct);
        var products = await ProductCountsByStoreAsync(storeIds, ct);

        return MarketplaceResult<List<MarketplaceStoreDto>>.Ok(stores.Select(s => new MarketplaceStoreDto
        {
            StoreId = s.StoreId,
            Name = s.Name,
            Description = s.Description,
            Kind = s.Kind,
            KindLabel = MarketplaceStoreKind.Label(s.Kind),
            Location = s.Location,
            PhotoUrl = s.PhotoUrl,
            BannerUrl = s.BannerUrl,
            Status = s.Status,
            IsOrderable = s.IsActive && MarketplaceStoreStatus.IsOrderable(s.Status),
            DeliveryEnabled = s.DeliveryEnabled,
            DeliveryFeeCentavos = s.DeliveryFeeCentavos,
            WorkspaceId = s.WorkspaceId,
            WorkspaceName = s.Workspace?.Name ?? "",
            OwnerUserId = s.OwnerUserId,
            OwnerName = s.OwnerUser?.FullName ?? "",
            OwnerEmail = s.OwnerUser?.Email ?? "",
            IsOwner = s.OwnerUserId == headId,
            // The Head administers every storefront, so the screen must show the
            // management affordances even where they own nothing.
            CanManage = true,
            MyPermissions = MarketplaceStorePermissions.All.ToList(),
            ProductCount = products.GetValueOrDefault(s.StoreId),
            SoldUnits = sales.GetValueOrDefault(s.StoreId).Units,
            SalesCentavos = sales.GetValueOrDefault(s.StoreId).Centavos,
            CreatedAt = s.CreatedAt
        }).ToList());
    }



    /// <summary>
    /// Creates or edits a storefront. Marketplace Head only - a store owner may
    /// pause their own store but cannot rename it, re-own it or re-point it at a
    /// different workspace.
    /// </summary>
    public async Task<MarketplaceResult<MarketplaceStoreDto>> SaveAsync(
        int headId, SaveMarketplaceStoreRequest request, CancellationToken ct = default)
    {
        if (!await IsHeadAsync(headId, ct))
            return MarketplaceResult<MarketplaceStoreDto>.Forbidden(HeadOnly);

        var name = (request.Name ?? "").Trim();
        if (name.Length is < 2 or > 100)
            return MarketplaceResult<MarketplaceStoreDto>.BadRequest("Enter a store name (2-100 characters).");

        var kind = MarketplaceStoreKind.IsValid(request.Kind)
            ? MarketplaceStoreKind.Normalize(request.Kind)
            : MarketplaceStoreKind.Food;

        // Only active/paused are settable. banned is an administrative action and
        // deleted is terminal; neither may be reached through a save form.
        var status = MarketplaceStoreStatus.CanOwnerSet(request.Status)
            ? MarketplaceStoreStatus.Normalize(request.Status)
            : MarketplaceStoreStatus.Active;

        if (request.DeliveryFeeCentavos < 0)
            return MarketplaceResult<MarketplaceStoreDto>.BadRequest("The delivery fee cannot be negative.");

        if (request.OwnerUserId.HasValue)
        {
            var owner = await _db.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == request.OwnerUserId.Value, ct);

            if (owner is null)
                return MarketplaceResult<MarketplaceStoreDto>.BadRequest("That owner account does not exist.");

            if (owner.AccountStatus != "Active")
                return MarketplaceResult<MarketplaceStoreDto>.BadRequest("That owner account is not active.");
        }

        if (request.WorkspaceId.HasValue)
        {
            var workspace = await _db.MarketplaceWorkspaces.AsNoTracking()
                .FirstOrDefaultAsync(w => w.WorkspaceId == request.WorkspaceId.Value, ct);

            if (workspace is null)
                return MarketplaceResult<MarketplaceStoreDto>.BadRequest("That workspace does not exist.");
        }

        var taken = await _db.MarketplaceStores.AsNoTracking()
            .AnyAsync(s => s.Name == name
                           && (request.StoreId == null || s.StoreId != request.StoreId.Value), ct);

        if (taken)
            return MarketplaceResult<MarketplaceStoreDto>.Conflict("A store with that name already exists.");

        MarketplaceStore store;

        if (request.StoreId is int storeId && storeId > 0)
        {
            store = await _db.MarketplaceStores.FirstOrDefaultAsync(s => s.StoreId == storeId, ct)
                ?? throw new InvalidOperationException("Store vanished mid-save.");

            // A banned store must be unbanned deliberately, so a plain save must
            // not silently bring it back. Keep the stricter state.
            if (store.Status == MarketplaceStoreStatus.Banned)
                status = MarketplaceStoreStatus.Banned;
        }
        else
        {
            store = new MarketplaceStore { CreatedAt = DateTime.UtcNow };
            _db.MarketplaceStores.Add(store);
        }

        store.Name = name;
        store.Description = (request.Description ?? "").Trim();
        store.Kind = kind;
        store.Location = (request.Location ?? "").Trim();
        store.PhotoUrl = Blank(request.PhotoUrl);
        store.BannerUrl = Blank(request.BannerUrl);
        store.OwnerUserId = request.OwnerUserId;
        store.WorkspaceId = request.WorkspaceId;
        store.DeliveryEnabled = request.DeliveryEnabled;
        store.DeliveryFeeCentavos = request.DeliveryFeeCentavos;
        store.Status = status;
        store.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        var saved = await _db.MarketplaceStores.AsNoTracking()
            .Include(s => s.OwnerUser)
            .FirstAsync(s => s.StoreId == store.StoreId, ct);

        return MarketplaceResult<MarketplaceStoreDto>.Ok(
            ToDto(saved, isOwner: false, MarketplaceStorePermissions.All, null, null),
            request.StoreId is null ? "Storefront created." : "Storefront updated.");
    }

    // =====================================================
    // TEAM
    // =====================================================

    /// <summary>
    /// The team, with the owner as a synthetic first row. Owner-only: who works
    /// where is the owner's business, and members cannot see each other.
    /// </summary>
    public async Task<MarketplaceResult<List<MarketplaceStoreMemberDto>>> ListTeamAsync(
        int userId, int storeId, CancellationToken ct = default)
    {
        var (actor, error) = await _access.RequireOwnerAsync(userId, storeId, ct);
        if (actor is null) return MarketplaceResult<List<MarketplaceStoreMemberDto>>.Forbidden(error);

        var members = await _db.MarketplaceStoreMembers.AsNoTracking()
            .Include(m => m.User)
            .Include(m => m.Permissions)
            .Where(m => m.StoreId == storeId)
            .ToListAsync(ct);

        var rows = new List<MarketplaceStoreMemberDto>();

        if (actor.Store.OwnerUserId is int ownerId)
        {
            var owner = await _db.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == ownerId, ct);

            rows.Add(new MarketplaceStoreMemberDto
            {
                StoreId = storeId,
                UserId = ownerId,
                FullName = owner?.FullName ?? "",
                IdNumber = owner?.IdNumber ?? "",
                Email = owner?.Email ?? "",
                Position = "Owner",
                IsOwner = true,
                IsActive = true,
                Permissions = MarketplaceStorePermissions.All.ToList(),
                AssignedAt = actor.Store.CreatedAt
            });
        }

        rows.AddRange(members.Select(m => new MarketplaceStoreMemberDto
        {
            StoreId = storeId,
            UserId = m.UserId,
            FullName = m.User?.FullName ?? "",
            IdNumber = m.User?.IdNumber ?? "",
            Email = m.User?.Email ?? "",
            Position = m.Position ?? "",
            IsOwner = false,
            IsActive = m.IsActive,
            Permissions = m.Permissions.Select(p => p.Permission).ToList(),
            AssignedAt = m.AssignedAt
        }));

        return MarketplaceResult<List<MarketplaceStoreMemberDto>>.Ok(rows);
    }

    /// <summary>Adds or updates a team member. Owner only.</summary>
    public async Task<MarketplaceResult<List<MarketplaceStoreMemberDto>>> SaveMemberAsync(
        int userId, int storeId, SaveStoreMemberRequest request, CancellationToken ct = default)
    {
        var (actor, error) = await _access.RequireOwnerAsync(userId, storeId, ct);
        if (actor is null) return MarketplaceResult<List<MarketplaceStoreMemberDto>>.Forbidden(error);

        if (request.UserId <= 0)
            return MarketplaceResult<List<MarketplaceStoreMemberDto>>.BadRequest("Choose an account to add.");

        if (actor.Store.OwnerUserId == request.UserId)
            return MarketplaceResult<List<MarketplaceStoreMemberDto>>.BadRequest(
                "That account owns this store and already has every permission.");

        var person = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == request.UserId, ct);

        if (person is null)
            return MarketplaceResult<List<MarketplaceStoreMemberDto>>.NotFound("That account does not exist.");

        if (person.AccountStatus != "Active")
            return MarketplaceResult<List<MarketplaceStoreMemberDto>>.BadRequest("That account is not active.");

        var existing = await _db.MarketplaceStoreMembers
            .Include(m => m.Permissions)
            .FirstOrDefaultAsync(
                m => m.StoreId == storeId && m.UserId == request.UserId, ct);

        if (existing is null)
        {
            existing = new MarketplaceStoreMember
            {
                StoreId = storeId,
                UserId = request.UserId,
                AssignedBy = userId,
                AssignedAt = DateTime.UtcNow
            };
            _db.MarketplaceStoreMembers.Add(existing);
        }

        existing.Position = Blank(request.Position);
        existing.IsActive = request.IsActive;

        // Replace the permission set wholesale rather than diffing it: the owner
        // just told us what this person may do, and a stale bit is worse than a
        // missing one.
        if (existing.Permissions.Count > 0)
            _db.MarketplaceStorePermissions.RemoveRange(existing.Permissions);
        existing.Permissions.Clear();

        if (request.IsActive)
        {
            foreach (var permission in MarketplaceStorePermissions.Sanitize(request.Permissions))
                existing.Permissions.Add(new MarketplaceStorePermission { Permission = permission });
        }

        await _db.SaveChangesAsync(ct);
        return await ListTeamAsync(userId, storeId, ct);
    }

    // =====================================================
    // APPLICATIONS
    // =====================================================

    /// <summary>
    /// Applies to join a store's team. Any Student or Faculty account may apply;
    /// staff cannot, because they are not buyers and a store team is a buyer
    /// role in this model.
    /// </summary>
    public async Task<MarketplaceResult<MarketplaceStoreApplicationDto>> ApplyAsync(
        int userId, ApplyToStoreRequest request, CancellationToken ct = default)
    {
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);

        if (user is null) return MarketplaceResult<MarketplaceStoreApplicationDto>.Unauthorized("Please sign in to continue.");

        if (user.Role is not ("Student" or "Faculty"))
            return MarketplaceResult<MarketplaceStoreApplicationDto>.Forbidden(
                "Only Student and Faculty accounts can apply to work at a store.");

        var position = (request.Position ?? "").Trim();
        if (position.Length is < 2 or > 120)
            return MarketplaceResult<MarketplaceStoreApplicationDto>.BadRequest(
                "Say which position you are applying for (2-120 characters).");

        var store = await _db.MarketplaceStores.AsNoTracking()
            .FirstOrDefaultAsync(s => s.StoreId == request.StoreId, ct);

        if (store is null)
            return MarketplaceResult<MarketplaceStoreApplicationDto>.NotFound("That store does not exist.");

        if (!store.IsActive || store.Status is not (MarketplaceStoreStatus.Active or MarketplaceStoreStatus.Paused))
            return MarketplaceResult<MarketplaceStoreApplicationDto>.BadRequest("That store is not taking applications.");

        if (store.OwnerUserId == userId)
            return MarketplaceResult<MarketplaceStoreApplicationDto>.BadRequest("You already own this store.");

        var pending = await _db.MarketplaceStoreApplications.AsNoTracking()
            .AnyAsync(a => a.StoreId == request.StoreId
                           && a.UserId == userId
                           && a.Status == MarketplaceStoreApplicationStatus.Pending, ct);

        if (pending)
            return MarketplaceResult<MarketplaceStoreApplicationDto>.Conflict(
                "You already have an application waiting at this store.");

        var application = new MarketplaceStoreApplication
        {
            StoreId = request.StoreId,
            UserId = userId,
            Position = position,
            Message = (request.Message ?? "").Trim(),
            Status = MarketplaceStoreApplicationStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        _db.MarketplaceStoreApplications.Add(application);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // The UNIQUE (StoreId, PendingUserId) index fired: a concurrent
            // double-tap won the race.
            _db.Entry(application).State = EntityState.Detached;
            return MarketplaceResult<MarketplaceStoreApplicationDto>.Conflict(
                "You already have an application waiting at this store.");
        }

        return MarketplaceResult<MarketplaceStoreApplicationDto>.Created(
            ToApplicationDto(application, store.Name, user),
            "Application sent. The store owner will review it.");
    }

    /// <summary>Applications at a store. Owner only.</summary>
    public async Task<MarketplaceResult<List<MarketplaceStoreApplicationDto>>> ListApplicationsAsync(
        int userId, int storeId, string? status, CancellationToken ct = default)
    {
        var (actor, error) = await _access.RequireOwnerAsync(userId, storeId, ct);
        if (actor is null) return MarketplaceResult<List<MarketplaceStoreApplicationDto>>.Forbidden(error);

        var query = _db.MarketplaceStoreApplications.AsNoTracking()
            .Include(a => a.User)
            .Where(a => a.StoreId == storeId);

        if (!string.IsNullOrWhiteSpace(status))
        {
            var wanted = MarketplaceStoreApplicationStatus.Normalize(status);
            query = query.Where(a => a.Status == wanted);
        }

        var rows = await query
            .OrderBy(a => a.Status == MarketplaceStoreApplicationStatus.Pending ? 0 : 1)
            .ThenBy(a => a.CreatedAt)
            .Take(ApplicationPageSize)
            .ToListAsync(ct);

        return MarketplaceResult<List<MarketplaceStoreApplicationDto>>.Ok(
            rows.Select(a => ToApplicationDto(a, actor.Store.Name, a.User)).ToList());
    }

    /// <summary>
    /// Owner's decision on an application. Approving creates the membership with
    /// the permissions the owner chose - approval and team membership are one
    /// step, so a person can never be "approved" without a way to act.
    /// </summary>
    public async Task<MarketplaceResult<MarketplaceStoreApplicationDto>> ReviewApplicationAsync(
        int userId, int applicationId, ReviewStoreApplicationRequest request, CancellationToken ct = default)
    {
        var application = await _db.MarketplaceStoreApplications
            .Include(a => a.User)
            .Include(a => a.Store)
            .FirstOrDefaultAsync(a => a.ApplicationId == applicationId, ct);

        if (application is null)
            return MarketplaceResult<MarketplaceStoreApplicationDto>.NotFound("That application does not exist.");

        // The store id comes from the row, never from the route, so the owner
        // check cannot be aimed at somebody else's store.
        var (actor, error) = await _access.RequireOwnerAsync(userId, application.StoreId, ct);
        if (actor is null) return MarketplaceResult<MarketplaceStoreApplicationDto>.Forbidden(error);

        if (application.Status != MarketplaceStoreApplicationStatus.Pending)
            return MarketplaceResult<MarketplaceStoreApplicationDto>.BadRequest(
                $"That application is already {application.Status}.");

        application.ReviewedBy = userId;
        application.ReviewedAt = DateTime.UtcNow;
        application.ReviewNote = Blank(request.Notes);

        if (!request.Approve)
        {
            application.Status = MarketplaceStoreApplicationStatus.Rejected;

            await _db.SaveChangesAsync(ct);
            return MarketplaceResult<MarketplaceStoreApplicationDto>.Ok(
                ToApplicationDto(application, application.Store.Name, application.User),
                "Application rejected.");
        }

        var grantedPosition = (request.Position ?? "").Trim();
        if (grantedPosition.Length is < 2 or > 120)
            return MarketplaceResult<MarketplaceStoreApplicationDto>.BadRequest(
                "Enter the position you are granting (2-120 characters).");

        application.Status = MarketplaceStoreApplicationStatus.Approved;

        var member = await _db.MarketplaceStoreMembers
            .Include(m => m.Permissions)
            .FirstOrDefaultAsync(
                m => m.StoreId == application.StoreId && m.UserId == application.UserId, ct);

        if (member is null)
        {
            member = new MarketplaceStoreMember
            {
                StoreId = application.StoreId,
                UserId = application.UserId,
                AssignedBy = userId,
                AssignedAt = DateTime.UtcNow
            };
            _db.MarketplaceStoreMembers.Add(member);
        }

        member.Position = grantedPosition;
        member.IsActive = true;

        if (member.Permissions.Count > 0)
            _db.MarketplaceStorePermissions.RemoveRange(member.Permissions);
        member.Permissions.Clear();

        foreach (var permission in MarketplaceStorePermissions.Sanitize(request.Permissions))
            member.Permissions.Add(new MarketplaceStorePermission { Permission = permission });

        await _db.SaveChangesAsync(ct);

        return MarketplaceResult<MarketplaceStoreApplicationDto>.Ok(
            ToApplicationDto(application, application.Store.Name, application.User),
            $"{application.User?.FullName ?? "They"} can now work at this store.");
    }

    // =====================================================
    // CATEGORIES
    // =====================================================

    /// <summary>
    /// Categories available to a store: its own, plus every system category.
    /// A store can never use another store's private category.
    /// </summary>
    public async Task<MarketplaceResult<List<MarketplaceCategoryDto>>> ListCategoriesAsync(
        int userId, int storeId, CancellationToken ct = default)
    {
        var actor = await _access.GetActorAsync(userId, storeId, ct);
        if (actor is null) return MarketplaceResult<List<MarketplaceCategoryDto>>.NotFound("That store does not exist.");

        var categories = await _db.MarketplaceCategories.AsNoTracking()
            .Where(c => c.IsActive && (c.StoreId == null || c.StoreId == storeId))
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Name)
            .ToListAsync(ct);

        var counts = await CategoryProductCountsAsync(categories.Select(c => c.CategoryId).ToList(), ct);

        return MarketplaceResult<List<MarketplaceCategoryDto>>.Ok(categories.Select(c => new MarketplaceCategoryDto
        {
            CategoryId = c.CategoryId,
            StoreId = c.StoreId,
            Name = c.Name,
            SortOrder = c.SortOrder,
            IsActive = c.IsActive,
            ProductCount = counts.GetValueOrDefault(c.CategoryId)
        }).ToList());
    }

    /// <summary>Creates or renames one of the store's own categories.</summary>
    public async Task<MarketplaceResult<List<MarketplaceCategoryDto>>> SaveCategoryAsync(
        int userId, int storeId, SaveStoreCategoryRequest request, CancellationToken ct = default)
    {
        var (actor, error) = await _access.RequirePermissionAsync(
            userId, storeId, MarketplaceStorePermissions.Catalog, ct);

        if (actor is null) return MarketplaceResult<List<MarketplaceCategoryDto>>.Forbidden(error);

        var name = (request.Name ?? "").Trim();
        if (name.Length is < 1 or > 80)
            return MarketplaceResult<List<MarketplaceCategoryDto>>.BadRequest(
                "Enter a category name (1-80 characters).");

        var duplicate = await _db.MarketplaceCategories.AsNoTracking()
            .AnyAsync(c => c.StoreId == storeId
                           && c.Name == name
                           && (request.CategoryId == null || c.CategoryId != request.CategoryId.Value), ct);

        if (duplicate)
            return MarketplaceResult<List<MarketplaceCategoryDto>>.Conflict(
                "That store already has a category with that name.");

        if (request.CategoryId is int categoryId && categoryId > 0)
        {
            var existing = await _db.MarketplaceCategories
                .FirstOrDefaultAsync(c => c.CategoryId == categoryId, ct);

            if (existing is null)
                return MarketplaceResult<List<MarketplaceCategoryDto>>.NotFound("That category does not exist.");

            // A system category belongs to nobody and cannot be renamed from a
            // store screen - that would change every other store's catalogue.
            if (existing.StoreId is null)
                return MarketplaceResult<List<MarketplaceCategoryDto>>.BadRequest(
                    "System categories cannot be renamed here.");

            if (existing.StoreId != storeId)
                return MarketplaceResult<List<MarketplaceCategoryDto>>.Forbidden(
                    "That category belongs to another store.");

            existing.Name = name;
            existing.SortOrder = request.SortOrder;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            _db.MarketplaceCategories.Add(new MarketplaceCategory
            {
                StoreId = storeId,
                Name = name,
                SortOrder = request.SortOrder,
                CreatedAt = DateTime.UtcNow
            });
        }

        await _db.SaveChangesAsync(ct);
        return await ListCategoriesAsync(userId, storeId, ct);
    }

    // =====================================================
    // ANALYTICS - owner, or a member with the "analytics" permission
    // =====================================================

    /// <summary>
    /// Sales figures for one storefront: a 28-day daily series and a plain
    /// seven-day-average forecast.
    ///
    /// Read-only. The window and the forecast rule mirror the CampusMarket
    /// original exactly (Database/Marketplace/018_store_analytics.sql):
    ///   * the daily series comes from the <c>marketplace_sales_daily</c> view,
    ///     which counts COMPLETED, PAID orders only - never cancelled, never
    ///     unpaid, never refunded;
    ///   * the forecast is the average of the previous SEVEN COMPLETE Manila
    ///     days, counting quiet days as zero. It is labelled an estimate, not a
    ///     model - the original said the same.
    ///
    /// The view cannot invent a calendar, so a day with no sale simply is not a
    /// row. The 28-day series is filled here, one entry per day, so the buyer of
    /// this screen (the owner) sees a continuous line rather than scattered
    /// points.
    /// </summary>
    public async Task<MarketplaceResult<MarketplaceStoreAnalyticsDto>> AnalyticsAsync(
        int userId, int storeId, CancellationToken ct = default)
    {
        var (actor, error) = await _access.RequirePermissionAsync(
            userId, storeId, MarketplaceStorePermissions.Analytics, ct);

        if (actor is null) return MarketplaceResult<MarketplaceStoreAnalyticsDto>.Forbidden(error);

        var rows = await _db.Database
            .SqlQueryRaw<SalesDailyRow>(
                "SELECT StoreId, Day, SalesCentavos, Units, Orders "
                + "FROM marketplace_sales_daily WHERE StoreId = {0}",
                storeId)
            .ToListAsync(ct);

        // Sales are stored UTC; a campus "day" is Manila (+08:00), which is what
        // the view already grouped on. Match it here so a late-evening sale does
        // not land on the wrong day.
        var manilaToday = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8));

        var byDay = rows
            .Where(r => r.Day.HasValue)
            .GroupBy(r => DateOnly.FromDateTime(r.Day!.Value))
            .ToDictionary(
                g => g.Key,
                g => (Centavos: g.Sum(x => x.SalesCentavos), Orders: (int)g.Sum(x => x.Orders)));

        var series = new List<MarketplaceSalesBucketDto>(28);

        for (var offset = 27; offset >= 0; offset--)
        {
            var day = manilaToday.AddDays(-offset);
            byDay.TryGetValue(day, out var totals);

            series.Add(new MarketplaceSalesBucketDto
            {
                Day = day.ToString("yyyy-MM-dd"),
                AmountCentavos = totals.Centavos,
                Orders = totals.Orders
            });
        }

        // The previous seven COMPLETE days: yesterday back to seven days ago. The
        // day in progress is deliberately excluded, so a busy morning cannot
        // flatter the estimate.
        long sevenDayTotal = 0;
        var daysWithSales = 0;

        for (var offset = 7; offset >= 1; offset--)
        {
            if (byDay.TryGetValue(manilaToday.AddDays(-offset), out var totals))
            {
                sevenDayTotal += totals.Centavos;
                if (totals.Centavos > 0) daysWithSales++;
            }
        }

        return MarketplaceResult<MarketplaceStoreAnalyticsDto>.Ok(new MarketplaceStoreAnalyticsDto
        {
            StoreId = storeId,
            Sales28dCentavos = series.Sum(b => b.AmountCentavos),
            Orders28d = series.Sum(b => b.Orders),
            Units28d = (int)rows.Where(r => r.Day.HasValue).Sum(r => r.Units),
            DailyAverageCentavos = sevenDayTotal / 7,
            Forecast7dCentavos = sevenDayTotal,
            DaysWithSales = daysWithSales,
            SalesDaily = series
        });
    }

    /// <summary>
    /// One row of the <c>marketplace_sales_daily</c> view. An ad-hoc type for
    /// SqlQueryRaw, not a mapped entity - the view is created by SQL, not by EF.
    /// Public so EF's materializer can construct it.
    /// </summary>
    public sealed class SalesDailyRow
    {
        public int StoreId { get; set; }
        public DateTime? Day { get; set; }
        public long SalesCentavos { get; set; }
        public long Units { get; set; }
        public long Orders { get; set; }
    }

    // =====================================================
    // PRODUCT PHOTOS
    // =====================================================

    /// <summary>
    /// A product's gallery, ordered. The existing products.ImageUrl is the
    /// primary image and is not repeated here.
    /// </summary>
    public async Task<MarketplaceResult<List<ProductPhotoDto>>> ListPhotosAsync(
        int productId, CancellationToken ct = default)
    {
        var exists = await _db.MarketplaceProducts.AsNoTracking()
            .AnyAsync(p => p.ProductId == productId, ct);

        if (!exists) return MarketplaceResult<List<ProductPhotoDto>>.NotFound("That product does not exist.");

        var photos = await _db.MarketplaceProductPhotos.AsNoTracking()
            .Where(p => p.ProductId == productId)
            .OrderBy(p => p.SortOrder)
            .ThenBy(p => p.PhotoId)
            .ToListAsync(ct);

        return MarketplaceResult<List<ProductPhotoDto>>.Ok(photos.Select(p => new ProductPhotoDto
        {
            PhotoId = p.PhotoId,
            ProductId = p.ProductId,
            Url = p.Url,
            SortOrder = p.SortOrder
        }).ToList());
    }

    /// <summary>
    /// Adds one gallery image. HTTPS only and at most eight per product - the
    /// CampusMarket limit, enforced here rather than by a CHECK constraint
    /// because a constraint cannot count rows.
    /// </summary>
    public async Task<MarketplaceResult<List<ProductPhotoDto>>> AddPhotoAsync(
        int userId, int productId, SaveProductPhotoRequest request, CancellationToken ct = default)
    {
        var url = (request.Url ?? "").Trim();

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return MarketplaceResult<List<ProductPhotoDto>>.BadRequest("Photo links must be full https:// URLs.");

        if (url.Length > 2048)
            return MarketplaceResult<List<ProductPhotoDto>>.BadRequest("That photo link is too long.");

        var product = await _db.MarketplaceProducts.AsNoTracking()
            .FirstOrDefaultAsync(p => p.ProductId == productId, ct);

        if (product is null)
            return MarketplaceResult<List<ProductPhotoDto>>.NotFound("That product does not exist.");

        // Permission is checked against the store that OWNS the product's
        // workspace, resolved through the product - never from the request.
        if (!product.WorkspaceId.HasValue)
            return MarketplaceResult<List<ProductPhotoDto>>.Forbidden(
                "That product is not assigned to a storefront yet.");

        var store = await _db.MarketplaceStores.AsNoTracking()
            .FirstOrDefaultAsync(s => s.WorkspaceId == product.WorkspaceId.Value, ct);

        if (store is null)
            return MarketplaceResult<List<ProductPhotoDto>>.Forbidden(
                "That product's workspace has no storefront.");

        var (actor, error) = await _access.RequirePermissionAsync(
            userId, store.StoreId, MarketplaceStorePermissions.Catalog, ct);

        if (actor is null) return MarketplaceResult<List<ProductPhotoDto>>.Forbidden(error);

        var count = await _db.MarketplaceProductPhotos.AsNoTracking()
            .CountAsync(p => p.ProductId == productId, ct);

        if (count >= MaxProductPhotos)
            return MarketplaceResult<List<ProductPhotoDto>>.BadRequest(
                $"A product can have at most {MaxProductPhotos} photos.");

        var duplicate = await _db.MarketplaceProductPhotos.AsNoTracking()
            .AnyAsync(p => p.ProductId == productId && p.Url == url, ct);

        if (duplicate)
            return MarketplaceResult<List<ProductPhotoDto>>.Conflict("That photo is already in the gallery.");

        var maxSort = await _db.MarketplaceProductPhotos.AsNoTracking()
            .Where(p => p.ProductId == productId)
            .Select(p => (int?)p.SortOrder)
            .MaxAsync(ct) ?? -1;

        _db.MarketplaceProductPhotos.Add(new MarketplaceProductPhoto
        {
            ProductId = productId,
            Url = url,
            SortOrder = request.SortOrder > 0 ? request.SortOrder : maxSort + 1,
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync(ct);
        return await ListPhotosAsync(productId, ct);
    }

    /// <summary>Removes one gallery image.</summary>
    public async Task<MarketplaceResult<List<ProductPhotoDto>>> RemovePhotoAsync(
        int userId, int photoId, CancellationToken ct = default)
    {
        var photo = await _db.MarketplaceProductPhotos.AsNoTracking()
            .FirstOrDefaultAsync(p => p.PhotoId == photoId, ct);

        if (photo is null) return MarketplaceResult<List<ProductPhotoDto>>.NotFound("That photo does not exist.");

        var product = await _db.MarketplaceProducts.AsNoTracking()
            .FirstOrDefaultAsync(p => p.ProductId == photo.ProductId, ct);

        if (product?.WorkspaceId is not int workspaceId)
            return MarketplaceResult<List<ProductPhotoDto>>.Forbidden(
                "That product is not assigned to a storefront yet.");

        var store = await _db.MarketplaceStores.AsNoTracking()
            .FirstOrDefaultAsync(s => s.WorkspaceId == workspaceId, ct);

        if (store is null)
            return MarketplaceResult<List<ProductPhotoDto>>.Forbidden(
                "That product's workspace has no storefront.");

        var (actor, error) = await _access.RequirePermissionAsync(
            userId, store.StoreId, MarketplaceStorePermissions.Catalog, ct);

        if (actor is null) return MarketplaceResult<List<ProductPhotoDto>>.Forbidden(error);

        _db.MarketplaceProductPhotos.Remove(photo);
        await _db.SaveChangesAsync(ct);

        return await ListPhotosAsync(photo.ProductId, ct);
    }

    // =====================================================
    // INTERNAL
    // =====================================================

    /// <summary>
    /// Sold units and sales in centavos per store, from COMPLETED orders whose
    /// payment is settled. Cancelled and unpaid orders contribute nothing, so a
    /// "popular" storefront can never be one that merely processed a lot of
    /// abandoned carts.
    /// </summary>
    private async Task<Dictionary<int, (int Units, int Centavos)>> SalesByStoreAsync(
        List<int> storeIds, CancellationToken ct = default)
    {
        if (storeIds.Count == 0) return new();

        return await _db.MarketplaceOrderItems.AsNoTracking()
            .Where(i => i.Order!.Status == MarketplaceOrderStatus.Completed
                        && i.Order!.PaymentStatus == "paid"
                        && i.Order!.WorkspaceId != null
                        && storeIds.Contains(i.Order!.WorkspaceId.Value))
            .GroupBy(i => i.Order!.WorkspaceId!.Value)
            .Select(g => new
            {
                WorkspaceId = g.Key,
                Units = g.Sum(i => i.Quantity),
                Centavos = g.Sum(i => i.SubtotalCentavos)
            })
            .ToDictionaryAsync(
                x => x.WorkspaceId, x => (x.Units, x.Centavos), ct);
    }

    /// <summary>Active product count per store, keyed by workspace.</summary>
    private async Task<Dictionary<int, int>> ProductCountsByStoreAsync(
        List<int> storeIds, CancellationToken ct = default)
    {
        if (storeIds.Count == 0) return new();

        var stores = await _db.MarketplaceStores.AsNoTracking()
            .Where(s => storeIds.Contains(s.StoreId))
            .Select(s => new { s.StoreId, s.WorkspaceId })
            .ToListAsync(ct);

        var workspaceIds = stores.Where(s => s.WorkspaceId.HasValue)
            .Select(s => s.WorkspaceId!.Value).ToList();

        if (workspaceIds.Count == 0) return new();

        var counts = await _db.MarketplaceProducts.AsNoTracking()
            .Where(p => p.IsActive && p.WorkspaceId != null && workspaceIds.Contains(p.WorkspaceId.Value))
            .GroupBy(p => p.WorkspaceId!.Value)
            .Select(g => new { WorkspaceId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.WorkspaceId, x => x.Count, ct);

        var result = new Dictionary<int, int>();
        foreach (var store in stores)
        {
            if (store.WorkspaceId is int workspaceId && counts.TryGetValue(workspaceId, out var count))
                result[store.StoreId] = count;
            else
                result[store.StoreId] = 0;
        }

        return result;
    }

    private async Task<Dictionary<int, int>> CategoryProductCountsAsync(
        List<int> categoryIds, CancellationToken ct = default)
    {
        if (categoryIds.Count == 0) return new();

        return await _db.MarketplaceProducts.AsNoTracking()
            .Where(p => p.CategoryId != null && categoryIds.Contains(p.CategoryId.Value))
            .GroupBy(p => p.CategoryId!.Value)
            .Select(g => new { CategoryId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CategoryId, x => x.Count, ct);
    }

    private static MarketplaceStoreDto ToDto(
        MarketplaceStore store,
        bool isOwner,
        string[] permissions,
        (int Units, int Centavos)? sales,
        int? productCount) => new()
    {
        StoreId = store.StoreId,
        Name = store.Name,
        Description = store.Description,
        Kind = store.Kind,
        KindLabel = MarketplaceStoreKind.Label(store.Kind),
        Location = store.Location,
        PhotoUrl = store.PhotoUrl,
        BannerUrl = store.BannerUrl,
        Status = store.Status,
        IsOrderable = store.IsActive && MarketplaceStoreStatus.IsOrderable(store.Status),
        DeliveryEnabled = store.DeliveryEnabled,
        DeliveryFeeCentavos = store.DeliveryFeeCentavos,
        WorkspaceId = store.WorkspaceId,
        WorkspaceName = store.Workspace?.Name ?? "",
        OwnerUserId = store.OwnerUserId,
        OwnerName = store.OwnerUser?.FullName ?? "",
        OwnerEmail = store.OwnerUser?.Email ?? "",
        IsOwner = isOwner,
        // "Can manage" means the catalogue, which is the permission that implies
        // running the storefront day to day.
        CanManage = isOwner || permissions.Contains(MarketplaceStorePermissions.Catalog),
        MyPermissions = isOwner
            ? MarketplaceStorePermissions.All.ToList()
            : permissions.ToList(),
        ProductCount = productCount ?? 0,
        SoldUnits = sales?.Units ?? 0,
        SalesCentavos = sales?.Centavos ?? 0,
        CreatedAt = store.CreatedAt
    };

    private static MarketplaceStoreApplicationDto ToApplicationDto(
        MarketplaceStoreApplication a, string storeName, User? user) => new()
    {
        ApplicationId = a.ApplicationId,
        StoreId = a.StoreId,
        StoreName = storeName,
        UserId = a.UserId,
        FullName = user?.FullName ?? "",
        IdNumber = user?.IdNumber ?? "",
        Email = user?.Email ?? "",
        Position = a.Position,
        Message = a.Message,
        Status = a.Status,
        ReviewNote = a.ReviewNote,
        CreatedAt = a.CreatedAt,
        ReviewedAt = a.ReviewedAt
    };

    /// <summary>Trimmed, or null when blank - so "" and NULL mean the same thing.</summary>
    private static string? Blank(string? value)
    {
        var text = (value ?? "").Trim();
        return text.Length == 0 ? null : text;
    }

    /// <summary>
    /// Head check.
    ///
    /// Duplicated from MarketplaceAccessService.IsHeadAsync on purpose: that
    /// service answers "may this operator act on this workspace", and the
    /// storefront layer is deliberately independent of workspace scoping. Only
    /// two questions in this file are about the Head at all, so sharing the whole
    /// service to answer them would couple the layers for nothing.
    /// </summary>
    private async Task<bool> IsHeadAsync(int userId, CancellationToken ct) =>
        await _db.Users.AsNoTracking().AnyAsync(
            u => u.Id == userId
                 && u.AccountStatus == "Active"
                 && u.Role == "MarketplaceAdmin"
                 && u.AdminModule == "Marketplace", ct);

    private const string HeadOnly = "Marketplace Head access required.";
}
