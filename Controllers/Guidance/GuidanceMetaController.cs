using CDM_OneServe_API.Authorization;
using CDM_OneServe_API.Services.Guidance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CDM_OneServe_API.Controllers.Guidance;

/// <summary>
/// Shared Guidance reference data used by both sides of the module.
///   GET /api/guidance/holidays?year=YYYY
///
/// The mobile app used to call the public holiday API directly; routing it
/// through the server means the student booking screen, the counselor
/// Manage Availability calendar and the server-side booking rule all read the
/// exact same list.
/// </summary>
[ApiController]
[Route("api/guidance/holidays")]
[Authorize(Roles = GuidanceRoles.Any)]
public class GuidanceMetaController : ControllerBase
{
    private readonly GuidanceHolidayService _holidays;

    public GuidanceMetaController(GuidanceHolidayService holidays)
    {
        _holidays = holidays;
    }

    /// <summary>Public holidays for a year as { "yyyy-MM-dd": "Name" }. Defaults to the current school year.</summary>
    [HttpGet]
    public async Task<IActionResult> GetHolidays([FromQuery] int? year, CancellationToken ct)
    {
        var y = year is >= 2000 and <= 2100 ? year.Value : GuidanceClock.Today.Year;
        var map = await _holidays.GetHolidaysAsync(y, ct);
        return Ok(new { year = y, holidays = map });
    }
}
