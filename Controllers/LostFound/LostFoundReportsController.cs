using CDM_OneServe_API.DTOs.Admin.Library.Common;
using CDM_OneServe_API.DTOs.LostFound;
using CDM_OneServe_API.Services.LostFound;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CDM_OneServe_API.Controllers.LostFound;

/// <summary>
/// Lost &amp; Found reports - the public board plus the
/// signed-in user's own reports. Photos upload to
/// wwwroot/uploads/lostfound and are stored as root-relative
/// paths, resolved against the API origin.
/// </summary>
[ApiController]
[Route("api/lostfound/reports")]
[Authorize]
public class LostFoundReportsController : LostFoundControllerBase
{
    private readonly LostFoundService _service;

    public LostFoundReportsController(LostFoundService service) => _service = service;

    /// <summary>The caller's own reports, newest first.</summary>
    [HttpGet("my")]
    public async Task<IActionResult> GetMyReports()
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();
        return Ok(ApiResponse.Ok(await _service.GetMyReportsAsync(userId)));
    }

    /// <summary>
    /// The campus board: published reports only. A lost report
    /// whose item was handed over drops off on its own once a
    /// claim is marked Returned.
    ///
    /// `search` matches the item name, description, category and
    /// location - and related words, so "cellphone" also finds
    /// "smartphone". Results are ranked: exact title match first,
    /// broader matches below. `type`, `status` and `category` narrow
    /// the board and combine with the search instead of replacing it.
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GetPublic(
        [FromQuery] string? type,
        [FromQuery] string? status,
        [FromQuery] string? category,
        [FromQuery] string? search)
    {
        return Ok(ApiResponse.Ok(await _service.GetPublicAsync(type, status, category, search)));
    }

    /// <summary>Six most recent published found items, for the home screen.</summary>
    [HttpGet("recent-found")]
    [AllowAnonymous]
    public async Task<IActionResult> GetRecentFound()
    {
        return Ok(ApiResponse.Ok(await _service.GetRecentFoundAsync()));
    }

    /// <summary>Six most recent published lost items, for the home screen.</summary>
    [HttpGet("recent-lost")]
    [AllowAnonymous]
    public async Task<IActionResult> GetRecentLost()
    {
        return Ok(ApiResponse.Ok(await _service.GetRecentLostAsync()));
    }

    /// <summary>One report, if it is on the board.</summary>
    [HttpGet("{id:int}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetReport(int id)
    {
        var report = await _service.GetReportAsync(id);
        return report is null
            ? NotFound(ApiResponse.Fail("Report not found."))
            : Ok(ApiResponse.Ok(report));
    }

    [HttpPost]
    public async Task<IActionResult> CreateReport(CreateReportDto dto)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();
        return ToAction(await _service.CreateReportAsync(userId, dto));
    }

    /// <summary>Edit own report while it is still pending review.</summary>
    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateReport(int id, UpdateReportDto dto)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();
        return ToAction(await _service.UpdateReportAsync(userId, id, dto));
    }

    /// <summary>Delete own report while it is still pending review.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteReport(int id)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();
        return ToAction(await _service.DeleteReportAsync(userId, id));
    }

    /// <summary>
    /// Upload a photo for a report. The file is streamed to
    /// wwwroot/uploads/lostfound; only the relative path is stored.
    /// </summary>
    [HttpPost("upload")]
    public async Task<IActionResult> UploadImage(IFormFile file)
    {
        return ToAction(await _service.UploadImageAsync(file));
    }

    /// <summary>Admin review: publish or reject a pending report.</summary>
    [HttpPut("{id:int}/status")]
    [Authorize(Roles = "LostFoundAdmin,Admin,SuperAdmin")]
    public async Task<IActionResult> UpdateReportStatus(int id, [FromBody] UpdateStatusDto dto)
    {
        return ToAction(await _service.UpdateReportStatusAsync(id, dto.Status));
    }
}
