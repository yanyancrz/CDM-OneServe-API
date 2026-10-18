using CDM_OneServe_API.DTOs.Admin.Library.Common;
using CDM_OneServe_API.DTOs.Marketplace;
using CDM_OneServe_API.Services.Marketplace;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace CDM_OneServe_API.Controllers.Marketplace;

/// <summary>
/// The Student / Faculty campus ID claim, from both sides.
///
///   /id-claims/*          the BUYER: file a claim, see their own history
///   /id-claims/staff/*    STAFF: the review queue and the counter lookup
///
/// Created by Database/Marketplace/003_campus_services.sql.
///
/// Reviewing a claim is a mutation and requires a live operator session
/// (X-Operator-Session), so the person who released an ID is recorded from the
/// server-validated session and not from the request body.
/// </summary>
[ApiController]
[Route("api/marketplace/id-claims")]
[Authorize]
public class MarketplaceIdClaimController : MarketplaceControllerBase
{
    private readonly MarketplaceIdClaimService _claims;

    public MarketplaceIdClaimController(MarketplaceIdClaimService claims) => _claims = claims;

    // =====================================================
    // BUYER
    // =====================================================

    /// <summary>Every claim the signed-in student or faculty member has made.</summary>
    [HttpGet]
    public async Task<IActionResult> MyClaims(CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _claims.GetMyClaimsAsync(userId, ct));
    }

    /// <summary>
    /// Files a new claim. Refuses with a 409 when one is already open, so a
    /// double-tap cannot produce two replacement IDs.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> File(
        [FromBody] RequestIdClaimRequest request,
        CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _claims.RequestAsync(userId, request.ClaimType, request.Reason, ct));
    }

    // =====================================================
    // STAFF
    // =====================================================

    /// <summary>
    /// The review queue: open claims oldest-first, then settled ones. Filter by
    /// status or search by name / ID number / email / claim code.
    /// </summary>
    [HttpGet("staff")]
    public async Task<IActionResult> Queue(
        [FromQuery] string? status,
        [FromQuery] string? search,
        CancellationToken ct)
    {
        if (!TryGetUserId(out var staffId)) return InvalidSession();

        return ToAction(await _claims.ListForStaffAsync(staffId, status, search, ct));
    }

    /// <summary>
    /// Staff decision on a claim. Approving a Pending claim releases it for
    /// collection and issues the claim code; approving a released claim marks it
    /// handed over. Rejecting requires a note.
    /// </summary>
    [HttpPost("staff/{idClaimId:int}/review")]
    public async Task<IActionResult> Review(
        int idClaimId,
        [FromBody] ReviewIdClaimRequest request,
        CancellationToken ct)
    {
        if (!TryGetUserId(out var staffId)) return InvalidSession();

        return ToAction(await _claims.ReviewAsync(
            staffId, idClaimId, request.Approve, request.Notes, OperatorSession(), ct));
    }

    /// <summary>
    /// Counter lookup: the person types the code they were given and gets back
    /// the claim, instead of staff sifting the whole queue.
    /// </summary>
    [HttpGet("staff/verify")]
    public async Task<IActionResult> Verify([FromQuery] string? claimCode, CancellationToken ct)
    {
        if (!TryGetUserId(out var staffId)) return InvalidSession();

        return ToAction(await _claims.VerifyAsync(staffId, claimCode ?? "", ct));
    }
}
