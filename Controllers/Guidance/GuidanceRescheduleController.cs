using System.Security.Claims;
using CDM_OneServe_API.Authorization;
using CDM_OneServe_API.DTOs.Guidance;
using CDM_OneServe_API.Services.Guidance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CDM_OneServe_API.Controllers.Guidance;

[ApiController, Route("api/guidance/reschedules"), Authorize(Roles = GuidanceRoles.Any)]
public class GuidanceRescheduleController(GuidanceRescheduleService service) : ControllerBase
{
    private int Actor => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;
    private bool Counselor => User.IsInRole(GuidanceRoles.Counselor);
    private IActionResult Map<T>(GuidanceResult<T> r) => StatusCode(r.Status, new { r.Message, data = r.Data });
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(await service.ListAsync(Actor, Counselor, ct));
    [HttpGet("rules")]
    public async Task<IActionResult> Rules(CancellationToken ct) => Ok(await service.RulesAsync(ct));
    [HttpGet("{appointmentId:int}/availability")]
    public async Task<IActionResult> Availability(int appointmentId, [FromQuery] string date, CancellationToken ct) =>
        Map(await service.AvailabilityAsync(Actor, Counselor, appointmentId, date, ct));
    [HttpPost("{appointmentId:int}/proposals")]
    public async Task<IActionResult> Propose(int appointmentId, ProposeRescheduleRequest request, CancellationToken ct) =>
        Map(await service.ProposeAsync(Actor, Counselor, appointmentId, request, ct));
    [HttpPost("{appointmentId:int}/accept")]
    public async Task<IActionResult> Accept(int appointmentId, AcceptRescheduleRequest request, CancellationToken ct) =>
        Map(await service.AcceptAsync(Actor, Counselor, appointmentId, request.ProposalId, ct));
}
