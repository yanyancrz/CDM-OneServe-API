using CDM_OneServe_API.DTOs.Admin.Library.Common;
using CDM_OneServe_API.DTOs.LostFound;
using CDM_OneServe_API.Services.LostFound;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CDM_OneServe_API.Controllers.LostFound;

/// <summary>
/// Possible pairings of a lost report with a found report.
/// Matches are created by someone who owns one of the two
/// reports (or the admin) and are confirmed once verified.
/// </summary>
[ApiController]
[Route("api/lostfound/matches")]
[Authorize]
public class LostFoundMatchesController : LostFoundControllerBase
{
    private readonly LostFoundService _service;

    public LostFoundMatchesController(LostFoundService service) => _service = service;

    [HttpGet("my")]
    public async Task<IActionResult> GetMyMatches()
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();
        return Ok(ApiResponse.Ok(await _service.GetMyMatchesAsync(userId)));
    }

    [HttpGet("my/{reportId:int}")]
    public async Task<IActionResult> GetMyMatchesForReport(int reportId)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();
        return Ok(ApiResponse.Ok(await _service.GetMyMatchesForReportAsync(userId, reportId)));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetMatch(int id)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();
        return ToAction(await _service.GetMatchAsync(userId, id));
    }

    [HttpPost]
    public async Task<IActionResult> CreateMatch(CreateMatchDto dto)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();
        return ToAction(await _service.CreateMatchAsync(userId, dto));
    }

    [HttpPut("{id:int}/confirm")]
    public async Task<IActionResult> ConfirmMatch(int id)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();
        return ToAction(await _service.ConfirmMatchAsync(userId, id));
    }
}
