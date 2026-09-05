using CDM_OneServe_API.DTOs.LostFound;
using CDM_OneServe_API.Services.LostFound;
using Microsoft.AspNetCore.Mvc;

namespace CDM_OneServe_API.Controllers.LostFound;

[ApiController]
[Route("api/lostfound")]
public class LostFoundController : ControllerBase
{
    private readonly LostFoundService _service;

    public LostFoundController(LostFoundService service)
    {
        _service = service;
    }

    // ==========================================
    // CREATE LOST / FOUND REPORT
    // POST: /api/lostfound
    // ==========================================

    [HttpPost]
    public async Task<IActionResult> CreateReport(
        [FromBody] CreateLostFoundItemDto request)
    {
        try
        {
            var result = await _service.CreateReportAsync(request);

            return Ok(result);
        }
        catch (Exception ex)
        {
            return BadRequest(new
            {
                message = ex.Message
            });
        }
    }


    // ==========================================
    // GET ALL REPORTS
    // GET: /api/lostfound
    // ==========================================

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var reports = await _service.GetAllAsync();

        return Ok(reports);
    }


    // ==========================================
    // GET REPORT BY ID
    // GET: /api/lostfound/{id}
    // ==========================================

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var report = await _service.GetItemByIdAsync(id);

        if (report == null)
        {
            return NotFound(new
            {
                message = "Lost & Found report not found."
            });
        }

        return Ok(report);
    }


    // ==========================================
    // GET USER'S REPORTS
    // GET: /api/lostfound/user/{userId}
    // ==========================================

    [HttpGet("user/{userId}")]
    public async Task<IActionResult> GetUserReports(int userId)
    {
        var reports = await _service.GetUserReportsAsync(userId);

        return Ok(reports);
    }


    // ==========================================
    // SEARCH / FILTER
    // GET: /api/lostfound/search
    // ==========================================

    [HttpGet("search")]
    public async Task<IActionResult> Search(
        [FromQuery] string? keyword,
        [FromQuery] string? category,
        [FromQuery] string? reportType,
        [FromQuery] string? status)
    {
        var results = await _service.SearchAsync(
            keyword,
            category,
            reportType,
            status
        );

        return Ok(results);
    }


    // ==========================================
    // FIND POTENTIAL MATCHES
    // GET: /api/lostfound/{lostItemId}/matches
    // ==========================================

    [HttpGet("{lostItemId}/matches")]
    public async Task<IActionResult> FindPotentialMatches(
        int lostItemId)
    {
        try
        {
            var matches =
                await _service.FindPotentialMatchesAsync(lostItemId);

            return Ok(matches);
        }
        catch (Exception ex)
        {
            return BadRequest(new
            {
                message = ex.Message
            });
        }
    }


    // ==========================================
    // CONFIRM MATCH
    // POST: /api/lostfound/matches/confirm
    // ==========================================

    [HttpPost("matches/confirm")]
    public async Task<IActionResult> ConfirmMatch(
        [FromBody] ConfirmLostFoundMatchDto request)
    {
        try
        {
            var match =
                await _service.ConfirmMatchAsync(request);

            return Ok(match);
        }
        catch (Exception ex)
        {
            return BadRequest(new
            {
                message = ex.Message
            });
        }
    }


    // ==========================================
    // CREATE CLAIM
    // POST: /api/lostfound/claims
    // ==========================================

    [HttpPost("claims")]
    public async Task<IActionResult> CreateClaim(
        [FromBody] CreateLostFoundClaimDto request)
    {
        try
        {
            var claim = await _service.CreateClaimAsync(request);

            return Ok(claim);
        }
        catch (Exception ex)
        {
            return BadRequest(new
            {
                message = ex.Message
            });
        }
    }

    // ==========================================
    // GET USER'S CLAIMS
    // GET: /api/lostfound/claims/user/{userId}
    // ==========================================

    [HttpGet("claims/user/{userId}")]
    public async Task<IActionResult> GetUserClaims(int userId)
    {
        var claims = await _service.GetUserClaimsAsync(userId);

        return Ok(claims);
    }
}