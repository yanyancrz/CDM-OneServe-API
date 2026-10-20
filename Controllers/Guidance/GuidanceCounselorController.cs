using System.Security.Claims;

using CDM_OneServe_API.Authorization;
using CDM_OneServe_API.DTOs.Guidance;
using CDM_OneServe_API.Services.Guidance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CDM_OneServe_API.Controllers.Guidance;

/// <summary>
/// Counselor-side Guidance API. Counselor tokens only - a Student/Faculty token gets 403.
/// The counselor is always the JWT user; every query is scoped to them.
///   GET    appointments?status=&amp;date=         GET   stats
///   PATCH  appointments/{id}/status          PATCH appointments/{id}/reschedule
///   GET    reschedule-availability?appointmentId=&amp;date=
///   GET/POST availability                    PATCH/DELETE availability/{id}
///   GET/POST day-offs                         DELETE day-offs/{id}
/// </summary>
[ApiController]
[Route("api/guidance/counselor")]
[Authorize(Roles = GuidanceRoles.Counselor)]
public class GuidanceCounselorController : ControllerBase
{
    private readonly GuidanceCounselorService _service;
    private readonly GuidanceChatService _chat;

    public GuidanceCounselorController(GuidanceCounselorService service, GuidanceChatService chat)
    {
        _service = service;
        _chat = chat;
    }

    private bool TryGetUserId(out int id) =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out id);

    private IActionResult Map<T>(GuidanceResult<T> r) =>
        r.Success
            ? StatusCode(r.Status, new { message = r.Message, data = r.Data })
            : StatusCode(r.Status, new { message = r.Message });

    private IActionResult Invalid() => Unauthorized(new { message = "Invalid session. Please log in again." });

    [HttpGet("appointments")]
    public async Task<IActionResult> Appointments([FromQuery] string? status, [FromQuery] string? date, CancellationToken ct)
    {
        if (!TryGetUserId(out var id)) return Invalid();
        var r = await _service.GetAppointmentsAsync(id, status, date, ct);
        return r.Success ? Ok(new { appointments = r.Data }) : StatusCode(r.Status, new { message = r.Message });
    }

    [HttpPut("profile")]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateCounselorProfileRequest request, CancellationToken ct)
    {
        if (!TryGetUserId(out var id)) return Invalid();
        return Map(await _service.UpdateProfileAsync(id, request, ct));
    }

    [HttpPost("photo")]
    public async Task<IActionResult> UploadPhoto(IFormFile file, CancellationToken ct)
    {
        if (!TryGetUserId(out var id)) return Invalid();
        return Map(await _service.UploadPhotoAsync(id, file, ct));
    }

    [HttpGet("reports")]
    public async Task<IActionResult> Reports(CancellationToken ct)
    {
        if (!TryGetUserId(out var id)) return Invalid();
        return Ok(new { report = await _service.GetReportAsync(id, ct) });
    }

    [HttpGet("stats")]
    public async Task<IActionResult> Stats(CancellationToken ct)
    {
        if (!TryGetUserId(out var id)) return Invalid();
        return Ok(new { stats = await _service.GetStatsAsync(id, ct) });
    }

    [HttpPatch("appointments/{appointmentId:int}/status")]
    public async Task<IActionResult> UpdateStatus(
        int appointmentId, [FromBody] UpdateAppointmentStatusRequest request, CancellationToken ct)
    {
        if (!TryGetUserId(out var id)) return Invalid();
        return Map(await _service.UpdateStatusAsync(id, appointmentId, request.Status, ct));
    }

    [HttpGet("reschedule-availability")]
    public async Task<IActionResult> RescheduleAvailability(
        [FromQuery] int appointmentId, [FromQuery] string date, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        if (!TryGetUserId(out var id)) return Invalid();
        if (appointmentId <= 0 || string.IsNullOrWhiteSpace(date))
            return BadRequest(new { message = "appointmentId and date are required." });

        var r = await _service.GetRescheduleAvailabilityAsync(id, appointmentId, date, ct);
        return r.Success ? Ok(r.Data) : StatusCode(r.Status, new { message = r.Message });
    }

    [HttpPatch("appointments/{appointmentId:int}/reschedule")]
    public async Task<IActionResult> Reschedule(
        int appointmentId, [FromBody] RescheduleAppointmentRequest request, CancellationToken ct)
    {
        if (!TryGetUserId(out var id)) return Invalid();
        return Map(await _service.RescheduleAsync(id, appointmentId, request, ct));
    }

    [HttpGet("records")]
    public async Task<IActionResult> Records([FromQuery] int? studentId, CancellationToken ct)
    {
        if (!TryGetUserId(out var id)) return Invalid();
        return Ok(new { records = await _service.GetRecordsAsync(id, studentId, ct) });
    }

    [HttpGet("appointments/{appointmentId:int}/record")]
    public async Task<IActionResult> GetRecord(int appointmentId, CancellationToken ct)
    {
        if (!TryGetUserId(out var id)) return Invalid();
        return Ok(new { record = await _service.GetRecordForAppointmentAsync(id, appointmentId, ct) });
    }

    [HttpPut("appointments/{appointmentId:int}/record")]
    public async Task<IActionResult> UpsertRecord(
        int appointmentId, [FromBody] UpsertSessionRecordRequest request, CancellationToken ct)
    {
        if (!TryGetUserId(out var id)) return Invalid();
        return Map(await _service.UpsertRecordAsync(id, appointmentId, request, ct));
    }

    [HttpGet("follow-ups")]
    public async Task<IActionResult> FollowUps([FromQuery] string? status, CancellationToken ct)
    {
        if (!TryGetUserId(out var id)) return Invalid();
        return Ok(new { followUps = await _service.GetFollowUpsAsync(id, status, ct) });
    }

    [HttpPost("follow-ups")]
    public async Task<IActionResult> CreateFollowUp([FromBody] CreateFollowUpRequest request, CancellationToken ct)
    {
        if (!TryGetUserId(out var id)) return Invalid();
        return Map(await _service.CreateFollowUpAsync(id, request, ct));
    }

    [HttpPost("follow-ups/{followUpId:int}/cancel")]
    public async Task<IActionResult> CancelFollowUp(int followUpId, CancellationToken ct)
    {
        if (!TryGetUserId(out var id)) return Invalid();
        return Map(await _service.CancelFollowUpAsync(id, followUpId, ct));
    }

    [HttpGet("students")]
    public async Task<IActionResult> Students(CancellationToken ct)
    {
        if (!TryGetUserId(out var id)) return Invalid();
        return Ok(new { students = await _service.GetStudentCasesAsync(id, ct) });
    }

    [HttpGet("students/{studentId:int}")]
    public async Task<IActionResult> StudentCase(int studentId, CancellationToken ct)
    {
        if (!TryGetUserId(out var id)) return Invalid();
        var r = await _service.GetStudentCaseAsync(id, studentId, ct);
        return r.Success ? Ok(r.Data) : StatusCode(r.Status, new { message = r.Message });
    }

    [HttpGet("conversations")]
    public async Task<IActionResult> Conversations(CancellationToken ct)
    {
        if (!TryGetUserId(out var id)) return Invalid();
        return Ok(new { conversations = await _chat.GetConversationsAsync(id, isCounselor: true, ct) });
    }

    [HttpGet("conversations/{conversationId:int}/messages")]
    public async Task<IActionResult> Messages(int conversationId, [FromQuery] int? take, CancellationToken ct)
    {
        if (!TryGetUserId(out var id)) return Invalid();
        var messages = await _chat.GetMessagesAsync(id, isCounselor: true, conversationId, take, ct);
        return messages == null ? NotFound(new { message = "Conversation not found." }) : Ok(new { messages });
    }

    [HttpPost("conversations/{conversationId:int}/messages")]
    public async Task<IActionResult> Send(int conversationId, [FromBody] SendMessageRequest request, CancellationToken ct)
    {
        if (!TryGetUserId(out var id)) return Invalid();
        return Map(await _chat.SendMessageAsync(id, isCounselor: true, conversationId, request, ct));
    }

    [HttpPost("conversations/{conversationId:int}/read")]
    public async Task<IActionResult> MarkRead(int conversationId, CancellationToken ct)
    {
        if (!TryGetUserId(out var id)) return Invalid();
        return await _chat.MarkReadAsync(id, isCounselor: true, conversationId, ct)
            ? Ok(new { message = "Marked as read." })
            : NotFound(new { message = "Conversation not found." });
    }

    [HttpGet("availability")]
    public async Task<IActionResult> Availability(CancellationToken ct)
    {
        if (!TryGetUserId(out var id)) return Invalid();
        return Ok(new { availability = await _service.GetAvailabilityAsync(id, ct) });
    }

    [HttpPost("availability")]
    public async Task<IActionResult> AddAvailability([FromBody] AddAvailabilityRequest request, CancellationToken ct)
    {
        if (!TryGetUserId(out var id)) return Invalid();
        return Map(await _service.AddAvailabilityAsync(id, request, ct));
    }

    [HttpPatch("availability/{blockId:int}")]
    public async Task<IActionResult> UpdateAvailability(
        int blockId, [FromBody] UpdateAvailabilityRequest request, CancellationToken ct)
    {
        if (!TryGetUserId(out var id)) return Invalid();
        return Map(await _service.UpdateAvailabilityAsync(id, blockId, request, ct));
    }

    [HttpDelete("availability/{blockId:int}")]
    public async Task<IActionResult> DeleteAvailability(int blockId, CancellationToken ct)
    {
        if (!TryGetUserId(out var id)) return Invalid();
        return Map(await _service.DeleteAvailabilityAsync(id, blockId, ct));
    }

    [HttpPost("availability/reset-default")]
    public async Task<IActionResult> ResetAvailabilityToDefault(CancellationToken ct)
    {
        if (!TryGetUserId(out var id)) return Invalid();
        return Map(await _service.ResetToDefaultAsync(id, ct));
    }

    [HttpGet("day-offs")]
    public async Task<IActionResult> DayOffs(CancellationToken ct)
    {
        if (!TryGetUserId(out var id)) return Invalid();
        return Ok(new { dayOffs = await _service.GetDayOffsAsync(id, ct) });
    }

    [HttpPost("day-offs")]
    public async Task<IActionResult> AddDayOff([FromBody] AddDayOffRequest request, CancellationToken ct)
    {
        if (!TryGetUserId(out var id)) return Invalid();
        return Map(await _service.AddDayOffAsync(id, request, ct));
    }

    [HttpDelete("day-offs/{dayOffId:int}")]
    public async Task<IActionResult> DeleteDayOff(int dayOffId, CancellationToken ct)
    {
        if (!TryGetUserId(out var id)) return Invalid();
        return Map(await _service.DeleteDayOffAsync(id, dayOffId, ct));
    }
}