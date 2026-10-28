using CDM_OneServe_API.Authorization;
using CDM_OneServe_API.DTOs.Admin.Library.Common;
using CDM_OneServe_API.DTOs.LostFound;
using CDM_OneServe_API.Services.LostFound;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CDM_OneServe_API.Controllers.LostFound;

/// <summary>
/// Pickup availability and pickup schedules.
///
/// The Lost &amp; Found admin publishes the windows when the
/// Student Affairs Office accepts handovers; claimants walk in during
/// those windows (nobody books a slot); the admin records the
/// handover itself, which completes the claim.
/// </summary>
[ApiController]
[Route("api/lostfound/schedules")]
[Authorize]
public class LostFoundSchedulesController : LostFoundControllerBase
{
    private readonly LostFoundService _service;

    public LostFoundSchedulesController(LostFoundService service) => _service = service;

    /// <summary>Published, still-open pickup windows.</summary>
    [HttpGet("availability")]
    [AllowAnonymous]
    public async Task<IActionResult> GetAvailability()
    {
        return Ok(ApiResponse.Ok(await _service.GetAvailabilityAsync()));
    }

    /// <summary>Publish one pickup window.</summary>
    [HttpPost("availability")]
    [Authorize(Roles = LostFoundRoles.Access)]
    public async Task<IActionResult> CreateAvailability(CreatePickupAvailabilityDto dto)
    {
        return ToAction(await _service.CreateAvailabilityAsync(dto));
    }

    /// <summary>Publish several non-overlapping pickup windows at once.</summary>
    [HttpPost("availability/bulk")]
    [Authorize(Roles = LostFoundRoles.Access)]
    public async Task<IActionResult> CreateAvailabilityBatch(CreatePickupAvailabilityBatchDto dto)
    {
        return ToAction(await _service.CreateAvailabilityBatchAsync(dto));
    }

    /// <summary>
    /// Retire a window. Refused while a schedule still sits inside
    /// it, so a booked pickup can never lose its window.
    /// </summary>
    [HttpDelete("availability/{id:int}")]
    [Authorize(Roles = LostFoundRoles.Access)]
    public async Task<IActionResult> DeleteAvailability(int id)
    {
        return ToAction(await _service.DeleteAvailabilityAsync(id));
    }

    /// <summary>The schedule for one claim - claimant, finder or admin.</summary>
    [HttpGet("claim/{claimId:int}")]
    public async Task<IActionResult> GetScheduleForClaim(int claimId)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();
        return ToAction(await _service.GetScheduleForClaimAsync(userId, claimId));
    }

    /// <summary>Every schedule, ordered Awaiting Pickup first.</summary>
    [HttpGet]
    [Authorize(Roles = LostFoundRoles.Access)]
    public async Task<IActionResult> GetAllSchedules()
    {
        return Ok(ApiResponse.Ok(await _service.GetAllSchedulesAsync()));
    }

    /// <summary>
    /// Record the handover: who received the item and their student
    /// ID. Completes the schedule and the claim, marks the report
    /// Claimed, and closes every other active claim on the item.
    /// </summary>
    [HttpPut("{scheduleId:int}/complete")]
    [Authorize(Roles = LostFoundRoles.Access)]
    public async Task<IActionResult> CompleteSchedule(
        int scheduleId,
        [FromBody] CompleteClaimScheduleDto dto)
    {
        if (!TryGetUserId(out var adminId)) return InvalidSession();
        return ToAction(await _service.CompleteScheduleAsync(adminId, scheduleId, dto));
    }
}
