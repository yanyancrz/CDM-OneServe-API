using CDM_OneServe_API.Authorization;
using CDM_OneServe_API.DTOs.Admin.Library.Common;
using CDM_OneServe_API.DTOs.LostFound;
using CDM_OneServe_API.Services.LostFound;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CDM_OneServe_API.Controllers.LostFound;

/// <summary>
/// Claims on found items. A student claims an item they lost;
/// the finder (or the Lost &amp; Found admin) reviews the evidence,
/// then marks it awaiting pickup or rejects it. Claiming opens a
/// pickup schedule automatically.
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

    /// <summary>
    /// Stages a proof-of-ownership file. Only the generated reference is
    /// returned; the bytes are never served from a public URL and the
    /// real content type is verified from the file itself.
    /// </summary>
    [HttpPost("proof-upload")]
    [RequestSizeLimit(5 * 1024 * 1024)]
    public async Task<IActionResult> UploadProof(IFormFile file, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();
        return ToAction(await _service.UploadClaimProofAsync(userId, file, ct));
    }

    /// <summary>
    /// Streams one claim's proof to an authorized reader only: the
    /// claimant, the item's finder, or the Lost &amp; Found admin.
    /// </summary>
    [HttpGet("{id:int}/proof")]
    public async Task<IActionResult> GetProof(int id, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        var isAdmin = User.IsInRole(LostFoundRoles.LegacyAdmin) ||
                      User.IsInRole(LostFoundRoles.SuperAdmin) ||
                      User.IsInRole(LostFoundRoles.SubAdmin) ||
                      User.IsInRole("Admin") ||
                      User.IsInRole("SuperAdmin");

        var result = await _service.GetClaimProofAsync(userId, id, isAdmin, ct);
        if (!result.Success || result.Data == null) return ToAction(result);

        Response.Headers.CacheControl = "no-store";
        return File(result.Data.Content, result.Data.ContentType);
    }

    /// <summary>Claim a found item you lost.</summary>
    [HttpPost]
    public async Task<IActionResult> SubmitClaim([FromBody] CreateClaimDto dto)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();
        return ToAction(await _service.SubmitClaimAsync(userId, dto));
    }

    /// <summary>
    /// The finder (or admin) decides a claim: awaiting pickup, rejected
    /// with a reason, or sent back for more information with a message.
    /// </summary>
    [HttpPut("{id:int}/status")]
    public async Task<IActionResult> UpdateClaimStatus(int id, [FromBody] UpdateStatusDto dto)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();
        return ToAction(await _service.UpdateClaimStatusAsync(userId, id, dto.Status, dto.Reason));
    }

    /// <summary>
    /// The claimant answers a request for more information on their own
    /// claim, while it is still under review.
    /// </summary>
    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateClaim(int id, [FromBody] CreateClaimDto dto, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();
        return ToAction(await _service.UpdateMyClaimAsync(userId, id, dto, ct));
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
