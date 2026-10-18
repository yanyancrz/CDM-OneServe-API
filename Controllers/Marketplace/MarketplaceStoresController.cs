using CDM_OneServe_API.DTOs.Admin.Library.Common;
using CDM_OneServe_API.DTOs.Marketplace;
using CDM_OneServe_API.Services.Marketplace;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CDM_OneServe_API.Controllers.Marketplace;

/// <summary>
/// Storefronts: the seller/shopfront layer over the workspace-scoped
/// fulfilment model. Created by Database/Marketplace/006_storefronts.sql.
///
/// Created by Database/Marketplace/006_storefronts.sql. A store is a LAYER, not
/// a replacement: products, stock and orders stay workspace-scoped, and
/// marketplace_stores.WorkspaceId is the bridge.
///
/// [Authorize] is the plain OneServe JWT and the caller is always read from the
/// token. Who may do what inside a store is decided by
/// MarketplaceStoreAccessService - the owner has every permission, a team member
/// has the named ones - never by a role string.
///
/// NO ONLINE PAYMENTS: cash and campus card only.
/// </summary>
[ApiController]
[Route("api/marketplace/stores")]
[Authorize]
public class MarketplaceStoresController : MarketplaceControllerBase
{
    private readonly MarketplaceStoreService _stores;

    public MarketplaceStoresController(MarketplaceStoreService stores) => _stores = stores;

    // =====================================================
    // BROWSE - every signed-in account
    // =====================================================

    /// <summary>
    /// Storefronts a buyer may see, most sales first. Paused stores stay listed
    /// but are flagged not orderable, so a stall link a buyer followed explains
    /// itself instead of 404-ing.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Browse(
        [FromQuery] string? kind,
        [FromQuery] string? search,
        CancellationToken ct) =>
        ToAction(await _stores.BrowseAsync(kind, search, ct));

    /// <summary>
    /// One storefront, with the caller's own permissions attached - the same
    /// payload serves the buyer page and the management screen.
    /// </summary>
    [HttpGet("{storeId:int}")]
    public async Task<IActionResult> Get(int storeId, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _stores.GetAsync(userId, storeId, ct));
    }

    // =====================================================
    // HEAD - create and edit storefronts
    // =====================================================

    /// <summary>
    /// EVERY storefront, including banned and deleted.
    ///
    /// The browse endpoint hides those from buyers, but the Head needs the
    /// opposite: an administrator who cannot see a banned storefront can never
    /// unban it.
    ///
    /// Declared before <c>{storeId:int}</c> in intent, though the constraint is
    /// what actually keeps them apart - "manage" is not an int, so it can only
    /// ever reach this action.
    /// </summary>
    [HttpGet("manage")]
    public async Task<IActionResult> Manage(CancellationToken ct)
    {
        if (!TryGetUserId(out var headId)) return InvalidSession();

        return ToAction(await _stores.ListAllForHeadAsync(headId, ct));
    }

    /// <summary>
    /// Creates or edits a storefront. Marketplace Head only; a store owner may
    /// pause their own store but cannot rename it, re-own it or re-point it at a
    /// different workspace.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Save(
        [FromBody] SaveMarketplaceStoreRequest request,
        CancellationToken ct)
    {
        if (!TryGetUserId(out var headId)) return InvalidSession();

        return ToAction(await _stores.SaveAsync(headId, request, ct));
    }

    // =====================================================
    // TEAM - owner only
    // =====================================================

    /// <summary>
    /// The team, with the owner as a synthetic first row so the list never has a
    /// confusing gap where they should be.
    /// </summary>
    [HttpGet("{storeId:int}/team")]
    public async Task<IActionResult> Team(int storeId, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _stores.ListTeamAsync(userId, storeId, ct));
    }

    /// <summary>
    /// Adds or updates a team member. Owner only. The permission set is replaced
    /// wholesale, so a bit the owner removed really stops applying.
    /// </summary>
    [HttpPost("{storeId:int}/team")]
    public async Task<IActionResult> SaveMember(
        int storeId,
        [FromBody] SaveStoreMemberRequest request,
        CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _stores.SaveMemberAsync(userId, storeId, request, ct));
    }

    // =====================================================
    // APPLICATIONS
    // =====================================================

    /// <summary>
    /// Applies to join a store's team. Any Student or Faculty account; one
    /// pending application per store, enforced by a UNIQUE index so a
    /// double-tap cannot send two.
    /// </summary>
    [HttpPost("apply")]
    public async Task<IActionResult> Apply(
        [FromBody] ApplyToStoreRequest request,
        CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _stores.ApplyAsync(userId, request, ct));
    }

    /// <summary>Applications at a store. Owner only.</summary>
    [HttpGet("{storeId:int}/applications")]
    public async Task<IActionResult> Applications(
        int storeId,
        [FromQuery] string? status,
        CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _stores.ListApplicationsAsync(userId, storeId, status, ct));
    }

    /// <summary>
    /// Owner's decision. Approving creates the membership in the same step, with
    /// the permissions the owner chose - so nobody is ever "approved" without a
    /// way to act.
    /// </summary>
    [HttpPost("applications/{applicationId:int}/review")]
    public async Task<IActionResult> ReviewApplication(
        int applicationId,
        [FromBody] ReviewStoreApplicationRequest request,
        CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _stores.ReviewApplicationAsync(userId, applicationId, request, ct));
    }

    // =====================================================
    // CATEGORIES
    // =====================================================

    /// <summary>
    /// Categories available to a store: its own plus every system category. A
    /// store can never use another store's private category.
    /// </summary>
    [HttpGet("{storeId:int}/categories")]
    public async Task<IActionResult> Categories(int storeId, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _stores.ListCategoriesAsync(userId, storeId, ct));
    }

    /// <summary>
    /// Creates or renames one of the store's own categories. Needs the
    /// <c>catalog</c> permission. System categories cannot be renamed from here -
    /// that would silently change every other store's catalogue.
    /// </summary>
    [HttpPost("{storeId:int}/categories")]
    public async Task<IActionResult> SaveCategory(
        int storeId,
        [FromBody] SaveStoreCategoryRequest request,
        CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _stores.SaveCategoryAsync(userId, storeId, request, ct));
    }

    // =====================================================
    // ANALYTICS
    // =====================================================

    /// <summary>
    /// Sales figures for one storefront: a 28-day daily series and a seven-day
    /// average forecast. Needs the <c>analytics</c> permission; the owner has
    /// every permission, so this is theirs by default.
    ///
    /// Read-only. Figures come from the marketplace_sales_daily view
    /// (Database/Marketplace/018_store_analytics.sql).
    /// </summary>
    [HttpGet("{storeId:int}/analytics")]
    public async Task<IActionResult> Analytics(int storeId, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _stores.AnalyticsAsync(userId, storeId, ct));
    }

    // =====================================================
    // PRODUCT PHOTOS
    // =====================================================

    /// <summary>
    /// A product's gallery. The existing products.ImageUrl stays the primary
    /// image and is not repeated here.
    /// </summary>
    [HttpGet("products/{productId:int}/photos")]
    public async Task<IActionResult> Photos(int productId, CancellationToken ct) =>
        ToAction(await _stores.ListPhotosAsync(productId, ct));

    /// <summary>
    /// Adds one gallery image. HTTPS only, at most eight per product - the same
    /// limit and URL rules the CampusMarket spec uses.
    /// </summary>
    [HttpPost("products/{productId:int}/photos")]
    public async Task<IActionResult> AddPhoto(
        int productId,
        [FromBody] SaveProductPhotoRequest request,
        CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _stores.AddPhotoAsync(userId, productId, request, ct));
    }

    /// <summary>Removes one gallery image.</summary>
    [HttpDelete("photos/{photoId:int}")]
    public async Task<IActionResult> RemovePhoto(int photoId, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _stores.RemovePhotoAsync(userId, photoId, ct));
    }
}
