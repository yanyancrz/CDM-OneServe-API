using CDM_OneServe_API.Data;
using CDM_OneServe_API.DTOs.Marketplace;
using CDM_OneServe_API.Models;
using CDM_OneServe_API.Models.Marketplace;

using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Services.Marketplace;

/// <summary>
/// Who may do what inside a storefront, and which workspace they may touch.
///
/// Created by Database/Marketplace/006_storefronts.sql.
///
/// THE RULE, in one sentence
///   You may manage a store if you own it, or if you are an active team member
///   holding the specific permission being asked about.
///
/// WHY THIS IS NOT THE EXISTING WORKSPACE SCOPING
///   `MarketplaceOperatorSessionService` answers "which stall terminal is this
///   account pinned to". This answers "what is this person allowed to do in
///   this shop". They are different questions and both still exist:
///   * a dedicated stall login (marketplace_stall_accounts) is a SHARED TERMINAL,
///     pinned to one workspace;
///   * a store member is a PERSON with named permissions, who may work across
///     several stores.
///
/// WHAT AN OWNER GETS
///   Everything, without a permission row. The owner is deliberately absent from
///   marketplace_store_members so they can never be edited out of their own
///   store, never lose a bit by accident, and never appear in the team list
///   twice. `CanManageAsync` therefore short-circuits on ownership and returns
///   all five permissions.
///
/// This mirrors the CampusMarket `can_manage_store` rule, reimplemented in C#
/// because this project does authorization in services rather than in database
/// RLS.
/// </summary>
public class MarketplaceStoreAccessService
{
    private readonly AppDbContext _db;

    public MarketplaceStoreAccessService(AppDbContext db) => _db = db;

    /// <summary>Who the caller is in a store's context.</summary>
    public sealed record StoreActor(
        MarketplaceStore Store,
        int UserId,
        bool IsOwner,
        bool IsMember,
        string[] Permissions)
    {
        /// <summary>True when the caller may see this store at all.</summary>
        public bool IsStaff => IsOwner || IsMember;

        public bool Has(string permission) =>
            IsOwner
            || Permissions.Contains(permission, StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The workspace this actor may operate for the store, or null when they
        /// have no business touching the fulfilment side - a buyer browsing a
        /// storefront, or a member whose store is not wired to a workspace yet.
        /// </summary>
        public int? WorkspaceId => Has(MarketplaceStorePermissions.Orders)
            || Has(MarketplaceStorePermissions.Catalog)
            || Has(MarketplaceStorePermissions.Cash)
            || Has(MarketplaceStorePermissions.Delivery)
                ? Store.WorkspaceId
                : null;
    }

    /// <summary>
    /// Resolves the caller against a store. Returns null when the store does not
    /// exist; a buyer simply gets <c>IsStaff = false</c> with no permissions,
    /// which is what makes the browse endpoints safe for anyone signed in.
    /// </summary>
    public async Task<StoreActor?> GetActorAsync(
        int userId, int storeId, CancellationToken ct = default)
    {
        var store = await _db.MarketplaceStores.AsNoTracking()
            .FirstOrDefaultAsync(s => s.StoreId == storeId, ct);

        if (store is null) return null;

        var member = await _db.MarketplaceStoreMembers.AsNoTracking()
            .Include(m => m.Permissions)
            .FirstOrDefaultAsync(
                m => m.StoreId == storeId && m.UserId == userId && m.IsActive, ct);

        var isOwner = store.OwnerUserId.HasValue && store.OwnerUserId.Value == userId;

        // Owner implies everything, so no permission row is needed.
        if (isOwner)
            return new StoreActor(store, userId, IsOwner: true, IsMember: member is not null,
                MarketplaceStorePermissions.All);

        if (member is null)
            return new StoreActor(store, userId, IsOwner: false, IsMember: false, Array.Empty<string>());

        return new StoreActor(
            store, userId, IsOwner: false, IsMember: true,
            member.Permissions.Select(p => p.Permission).ToArray());
    }

    /// <summary>
    /// Same as <see cref="GetActorAsync"/> but refuses anyone without the named
    /// permission, and says which permission was missing so the caller can tell
    /// the user something useful instead of a flat 403.
    /// </summary>
    public async Task<(StoreActor? Actor, string? Error)> RequirePermissionAsync(
        int userId, int storeId, string permission, CancellationToken ct = default)
    {
        if (!MarketplaceStorePermissions.IsValid(permission))
            return (null, $"\"{permission}\" is not a store permission.");

        var actor = await GetActorAsync(userId, storeId, ct);

        if (actor is null) return (null, "That store does not exist.");

        if (!actor.IsStaff)
            return (null, "You are not part of this store.");

        if (!actor.Has(permission))
            return (null, $"You need the \"{MarketplaceStorePermissions.Label(permission)}\" permission for this store.");

        // A banned or deleted store still has a team, but nobody may act on it.
        if (actor.IsOwner &&
            actor.Store.Status is MarketplaceStoreStatus.Banned or MarketplaceStoreStatus.Deleted)
            return (null, $"This store is {actor.Store.Status} and cannot be managed right now.");

        return (actor, null);
    }

    /// <summary>Owner-only gate: the team and the store's own settings.</summary>
    public async Task<(StoreActor? Actor, string? Error)> RequireOwnerAsync(
        int userId, int storeId, CancellationToken ct = default)
    {
        var actor = await GetActorAsync(userId, storeId, ct);

        if (actor is null) return (null, "That store does not exist.");
        if (!actor.IsOwner) return (null, "Only the store owner can do that.");

        return (actor, null);
    }

    /// <summary>
    /// Every store the caller owns or is an active member of, for the "My stores"
    /// list. Owners are included whether or not they also hold a member row.
    /// </summary>
    public async Task<List<(MarketplaceStore Store, bool IsOwner, string[] Permissions)>> ListMineAsync(
        int userId, CancellationToken ct = default)
    {
        var owned = await _db.MarketplaceStores.AsNoTracking()
            .Where(s => s.OwnerUserId == userId && s.Status != MarketplaceStoreStatus.Deleted)
            .Select(s => new { Store = s, IsOwner = true })
            .ToListAsync(ct);

        var joined = await _db.MarketplaceStoreMembers.AsNoTracking()
            .Include(m => m.Permissions)
            .Where(m => m.UserId == userId && m.IsActive && m.Store.Status != MarketplaceStoreStatus.Deleted)
            .Select(m => new { Store = m.Store, Perms = m.Permissions.Select(p => p.Permission).ToArray() })
            .ToListAsync(ct);

        var result = new List<(MarketplaceStore, bool, string[])>();

        foreach (var row in owned)
            result.Add((row.Store, true, MarketplaceStorePermissions.All));

        // A store the caller both owns and is a member of must appear once.
        foreach (var row in joined)
        {
            if (owned.Any(o => o.Store.StoreId == row.Store.StoreId)) continue;
            result.Add((row.Store, false, row.Perms));
        }

        return result;
    }

    private const string NotPartOfStore = "You are not part of this store.";
}
