using CDM_OneServe_API.DTOs.Admin.Library.Common;
using CDM_OneServe_API.DTOs.LostFound;
using CDM_OneServe_API.Services.LostFound;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CDM_OneServe_API.Controllers.LostFound;

/// <summary>
/// Recovery reports: "I found your lost item." The finder hands the item to
/// the Student Affairs Office, which turns the lost report into a found one
/// and auto-creates the owner's claim so the pickup flow runs. Keepers
/// outside the office no longer exist.
/// </summary>
[ApiController]
[Route("api/lostfound/recovery")]
[Authorize]
public class LostFoundRecoveryController : LostFoundControllerBase
{
    private readonly LostFoundService _service;

    public LostFoundRecoveryController(LostFoundService service) => _service = service;

    /// <summary>The recovery trail for a lost report - owner, finder or admin.</summary>
    [HttpGet("report/{reportId:int}")]
    public async Task<IActionResult> GetRecoveryForReport(int reportId)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();
        return ToAction(await _service.GetRecoveryForReportAsync(userId, reportId));
    }

    /// <summary>The caller's own recovery reports.</summary>
    [HttpGet("my")]
    public async Task<IActionResult> GetMyRecovery()
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();
        return Ok(ApiResponse.Ok(await _service.GetMyRecoveryAsync(userId)));
    }

    /// <summary>Report that you recovered someone's lost item.</summary>
    [HttpPost]
    public async Task<IActionResult> CreateRecovery(CreateRecoveryDto dto)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();
        return ToAction(await _service.CreateRecoveryAsync(userId, dto));
    }
}
