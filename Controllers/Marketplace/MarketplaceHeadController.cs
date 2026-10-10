using CDM_OneServe_API.DTOs.Admin.Library.Common;
using CDM_OneServe_API.DTOs.Marketplace;
using CDM_OneServe_API.Services.Marketplace;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CDM_OneServe_API.Controllers.Marketplace;

/// <summary>
/// MARKETPLACE HEAD - staff account management.
///
///   GET    /api/marketplace/head/staff
///   POST   /api/marketplace/head/staff
///   POST   /api/marketplace/head/staff/{staffUserId}/deactivate
///   POST   /api/marketplace/head/staff/{staffUserId}/reactivate
///
/// The Marketplace Head account is created in the EXISTING Admin > Users "Add
/// Head" screen, exactly like the Library Head and Guidance Head - there is no
/// marketplace signup. From there the Head manages the marketplace's operator
/// accounts, which is what these endpoints are for.
///
/// WHO IS REFUSED
///   * the CDM OneServe Admin - they monitor, read-only. Spec 39 says the OneServe
///     Admin must not be able to create, edit or delete Student/Faculty accounts
///     through the marketplace, so every method here returns 403 for them;
///   * operators (staff) - only the Head manages staff;
///   * buyers - they have no way in.
///
/// Note what deactivating does NOT do: it does not touch, suspend or delete the
/// OneServe user. It withdraws marketplace operating access and nothing more.
/// </summary>
[ApiController]
[Route("api/marketplace/head")]
[Authorize]
public class MarketplaceHeadController : MarketplaceControllerBase
{
    private readonly MarketplaceStaffManagementService _staff;

    public MarketplaceHeadController(MarketplaceStaffManagementService staff)
    {
        _staff = staff;
    }

    /// <summary>
    /// Confirms the caller is the Marketplace Head. Used by the staff portal to
    /// decide whether to show the Staff Accounts screen.
    /// </summary>
    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        var head = await _staff.GetHeadAsync(userId, ct);
        if (head is null)
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse.Fail("This account is not the Marketplace Head."));

        return Ok(ApiResponse.Ok(new
        {
            userId = head.UserId,
            fullName = head.User.FullName,
            email = head.User.Email,
            isHead = true,
            // A Head can operate the marketplace and manage its staff accounts.
            canOperate = true,
            canManageStaff = true
        }));
    }

    /// <summary>
    /// Every operator account, active and retired. Retired rows stay visible so
    /// the Head can see who handled past orders.
    /// </summary>
    [HttpGet("staff")]
    public async Task<IActionResult> ListStaff(CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _staff.ListAsync(userId, ct));
    }

    /// <summary>
    /// Creates an operator account: a normal OneServe user plus the marketplace
    /// operator role. The person signs in with the regular OneServe login.
    /// </summary>
    [HttpPost("staff")]
    public async Task<IActionResult> CreateStaff(
        [FromBody] CreateMarketplaceStaffRequest request,
        CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _staff.CreateAsync(userId, request, ct));
    }

    /// <summary>
    /// Withdraws marketplace operating access. The OneServe account is left
    /// exactly as it was.
    /// </summary>
    [HttpPost("staff/{staffUserId:int}/deactivate")]
    public async Task<IActionResult> Deactivate(int staffUserId, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _staff.DeactivateAsync(userId, staffUserId, ct));
    }

    /// <summary>Restores operating access to a retired operator account.</summary>
    [HttpPost("staff/{staffUserId:int}/reactivate")]
    public async Task<IActionResult> Reactivate(int staffUserId, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _staff.ReactivateAsync(userId, staffUserId, ct));
    }
}