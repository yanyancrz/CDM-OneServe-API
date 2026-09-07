using CDM_OneServe_API.Services.LostFound;
using Microsoft.AspNetCore.Mvc;

namespace CDM_OneServe_API.Controllers.LostFound;

[ApiController]
[Route("api/lostfound/admin")]
public class LostFoundAdminController : ControllerBase
{
    private readonly LostFoundService _service;

    public LostFoundAdminController(LostFoundService service)
    {
        _service = service;
    }

    // =========================
    // DASHBOARD
    // =========================
    [HttpGet("dashboard")]
    public async Task<IActionResult> GetDashboard()
    {
        var result = await _service.GetAdminDashboardAsync();
        return Ok(result);
    }

    // =========================
    // REPORTS
    // =========================
    [HttpGet("reports")]
    public async Task<IActionResult> GetReports()
    {
        var result = await _service.GetAdminReportsAsync();
        return Ok(result);
    }

    [HttpGet("reports/{id}")]
    public async Task<IActionResult> GetReportById(int id)
    {
        var result = await _service.GetAdminReportByIdAsync(id);

        if (result == null)
            return NotFound(new { message = "Report not found." });

        return Ok(result);
    }

    [HttpPut("reports/{id}/verify")]
    public async Task<IActionResult> VerifyReport(int id)
    {
        var result = await _service.VerifyReportAsync(id);

        if (!result)
            return NotFound(new { message = "Report not found." });

        return Ok(new
        {
            message = "Report verified successfully."
        });
    }

    [HttpPut("reports/{id}/reject")]
    public async Task<IActionResult> RejectReport(int id)
    {
        var result = await _service.RejectReportAsync(id);

        if (!result)
            return NotFound(new { message = "Report not found." });

        return Ok(new
        {
            message = "Report rejected successfully."
        });
    }

    // =========================
    // MATCHES
    // =========================
    [HttpGet("matches")]
    public async Task<IActionResult> GetMatches()
    {
        var result = await _service.GetAdminMatchesAsync();
        return Ok(result);
    }

    [HttpPut("matches/{id}/confirm")]
    public async Task<IActionResult> ConfirmMatch(int id)
    {
        var result = await _service.AdminConfirmMatchAsync(id);

        if (!result)
            return NotFound(new { message = "Match not found." });

        return Ok(new
        {
            message = "Match confirmed successfully."
        });
    }

    [HttpPut("matches/{id}/reject")]
    public async Task<IActionResult> RejectMatch(int id)
    {
        var result = await _service.AdminRejectMatchAsync(id);

        if (!result)
            return NotFound(new { message = "Match not found." });

        return Ok(new
        {
            message = "Match rejected successfully."
        });
    }

    // =========================
    // CLAIMS
    // =========================
    [HttpGet("claims")]
    public async Task<IActionResult> GetClaims()
    {
        var result = await _service.GetAdminClaimsAsync();
        return Ok(result);
    }

    [HttpGet("claims/{id}")]
    public async Task<IActionResult> GetClaimById(int id)
    {
        var result = await _service.GetAdminClaimByIdAsync(id);

        if (result == null)
            return NotFound(new { message = "Claim not found." });

        return Ok(result);
    }

    [HttpPut("claims/{id}/approve")]
    public async Task<IActionResult> ApproveClaim(int id, [FromQuery] int adminUserId)
    {
        var result = await _service.ApproveClaimAsync(id, adminUserId);

        if (!result)
            return NotFound(new { message = "Claim not found." });

        return Ok(new
        {
            message = "Claim approved successfully."
        });
    }

    [HttpPut("claims/{id}/reject")]
    public async Task<IActionResult> RejectClaim(int id, [FromQuery] int adminUserId)
    {
        var result = await _service.RejectClaimAsync(id, adminUserId);

        if (!result)
            return NotFound(new { message = "Claim not found." });

        return Ok(new
        {
            message = "Claim rejected successfully."
        });
    }

    // =========================
    // NOTIFICATIONS
    // =========================
    [HttpGet("notifications/{adminUserId}")]
    public async Task<IActionResult> GetNotifications(int adminUserId)
    {
        var result = await _service.GetAdminNotificationsAsync(adminUserId);
        return Ok(result);
    }

    [HttpPut("notifications/{id}/read")]
    public async Task<IActionResult> MarkNotificationAsRead(int id)
    {
        var result = await _service.MarkAdminNotificationAsReadAsync(id);

        if (!result)
            return NotFound(new { message = "Notification not found." });

        return Ok(new
        {
            message = "Notification marked as read."
        });
    }
}