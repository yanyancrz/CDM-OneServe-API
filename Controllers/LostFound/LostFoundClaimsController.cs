using CDM_OneServe_API.DTOs.Admin.Library.Common;
using CDM_OneServe_API.DTOs.LostFound;
using CDM_OneServe_API.Services.LostFound;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CDM_OneServe_API.Controllers.LostFound;

/// <summary>
/// Claims on found items. A student claims an item they lost;
/// the finder (or the Lost &amp; Found admin) marks it claimed or rejects
/// it. Claiming opens a pickup schedule automatically.
/// </summary>
[ApiController]
[Route("api/lostfound/claims")]
[Authorize]
public class LostFoundClaimsController : LostFoundControllerBase
{
    private readonly LostFoundService _service;

    public LostFoundClaimsController(LostFoundService service) => _service = service;

    /// <summary>The caller's own claims.</summary>
    [HttpGet("my")]
    public async Task<IActionResult> GetMyClaims()
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();
        return Ok(ApiResponse.Ok(await _service.GetMyClaimsAsync(userId)));
    }

    /// <summary>The caller's claim for one report, if any.</summary>
    [HttpGet("my/{reportId:int}")]
    public async Task<IActionResult> GetMyClaimForReport(int reportId)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();
        return ToAction(await _service.GetClaimForReportAsync(userId, reportId));
    }

    /// <summary>
    /// Claims on one report - for its finder and the admin, so
    /// they can review who wants the item.
    /// </summary>
    [HttpGet("report/{reportId:int}")]
    public async Task<IActionResult> GetClaimsForReport(int reportId)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();
        return ToAction(await _service.GetClaimsForReportAsync(userId, reportId));
    }

    /// <summary>Claim a found item you lost.</summary>
    [HttpPost]
    public async Task<IActionResult> SubmitClaim(CreateClaimDto dto)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();
        return ToAction(await _service.SubmitClaimAsync(userId, dto));
    }

    /// <summary>The finder (or admin) marks a claim as claimed or rejects it.</summary>
    [HttpPut("{id:int}/status")]
    public async Task<IActionResult> UpdateClaimStatus(int id, [FromBody] UpdateStatusDto dto)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();
        return ToAction(await _service.UpdateClaimStatusAsync(userId, id, dto.Status));
    }

    /// <summary>A claimant withdraws their own claim.</summary>
    [HttpPut("{id:int}/cancel")]
    public async Task<IActionResult> CancelClaim(int id)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();
        return ToAction(await _service.CancelClaimAsync(userId, id));
    }

    /// <summary>Remove a cancelled claim of the caller's own.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteClaim(int id)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();
        return ToAction(await _service.DeleteClaimAsync(userId, id));
    }
}
