using System.Security.Claims;

using CDM_OneServe_API.Authorization;
using CDM_OneServe_API.DTOs.Guidance;
using CDM_OneServe_API.Services.Guidance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CDM_OneServe_API.Controllers.Guidance;

/// <summary>
/// Student-side Guidance API. Student / Faculty tokens only; a Counselor token gets 403.
/// The student is always the JWT user: there is no studentId in any request.
///   GET    /api/guidance/student/counselors
///   GET    /api/guidance/student/booking-rules
///   GET    /api/guidance/student/availability?counselorId=&amp;date=yyyy-MM-dd
///   POST   /api/guidance/student/appointments
///   GET    /api/guidance/student/appointments
///   DELETE /api/guidance/student/appointments/{id}
/// </summary>
[ApiController]
[Route("api/guidance/student")]
[Authorize(Roles = GuidanceRoles.StudentSide)]
public class GuidanceStudentController : ControllerBase
{
    private readonly GuidanceStudentService _service;
    private readonly GuidanceChatService _chat;

    public GuidanceStudentController(GuidanceStudentService service, GuidanceChatService chat)
    {
        _service = service;
        _chat = chat;
    }

    private bool TryGetUserId(out int userId) =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

    private IActionResult Map<T>(GuidanceResult<T> r) =>
        r.Success
            ? StatusCode(r.Status, r.Data is null ? new { message = r.Message } : (object)new { message = r.Message, data = r.Data })
            : StatusCode(r.Status, new { message = r.Message });

    [HttpGet("counselors")]
    public async Task<IActionResult> Counselors(CancellationToken ct) =>
        Ok(new { counselors = await _service.GetCounselorsAsync(ct) });

    /// <summary>
    /// Rules the Book screen needs before it draws anything: whether today is
    /// bookable, and the date range the API will accept.
    /// </summary>
    [HttpGet("booking-rules")]
    public async Task<IActionResult> BookingRules(CancellationToken ct) =>
        Ok(await _service.GetBookingRulesAsync(ct));

    [HttpGet("availability")]
    public async Task<IActionResult> Availability(
        [FromQuery] int counselorId, [FromQuery] string date, CancellationToken ct)
    {
        // Slot availability changes minute to minute - never let a browser reuse it.
        Response.Headers.CacheControl = "no-store";

        if (counselorId <= 0 || string.IsNullOrWhiteSpace(date))
            return BadRequest(new { message = "counselorId and date are required." });

        var r = await _service.GetAvailabilityAsync(counselorId, date, ct);
        return r.Success ? Ok(r.Data) : StatusCode(r.Status, new { message = r.Message });
    }

    [HttpPost("appointments")]
    public async Task<IActionResult> Book([FromBody] BookAppointmentRequest request, CancellationToken ct)
    {
        if (!TryGetUserId(out var studentId))
            return Unauthorized(new { message = "Invalid session. Please log in again." });

        return Map(await _service.BookAsync(studentId, request, ct));
    }

    [HttpGet("appointments")]
    public async Task<IActionResult> MyAppointments(CancellationToken ct)
    {
        if (!TryGetUserId(out var studentId))
            return Unauthorized(new { message = "Invalid session. Please log in again." });

        return Ok(new { appointments = await _service.GetMyAppointmentsAsync(studentId, ct) });
    }

    [HttpGet("conversations")]
    public async Task<IActionResult> Conversations(CancellationToken ct)
    {
        if (!TryGetUserId(out var studentId))
            return Unauthorized(new { message = "Invalid session. Please log in again." });

        return Ok(new { conversations = await _chat.GetConversationsAsync(studentId, isCounselor: false, ct) });
    }

    [HttpPost("conversations")]
    public async Task<IActionResult> StartConversation([FromBody] StartConversationRequest request, CancellationToken ct)
    {
        if (!TryGetUserId(out var studentId))
            return Unauthorized(new { message = "Invalid session. Please log in again." });

        return Map(await _chat.StartConversationAsync(studentId, request.CounselorId, ct));
    }

    [HttpGet("conversations/{conversationId:int}/messages")]
    public async Task<IActionResult> Messages(int conversationId, [FromQuery] int? take, CancellationToken ct)
    {
        if (!TryGetUserId(out var studentId))
            return Unauthorized(new { message = "Invalid session. Please log in again." });

        var messages = await _chat.GetMessagesAsync(studentId, isCounselor: false, conversationId, take, ct);
        return messages == null ? NotFound(new { message = "Conversation not found." }) : Ok(new { messages });
    }

    [HttpPost("conversations/{conversationId:int}/messages")]
    public async Task<IActionResult> Send(int conversationId, [FromBody] SendMessageRequest request, CancellationToken ct)
    {
        if (!TryGetUserId(out var studentId))
            return Unauthorized(new { message = "Invalid session. Please log in again." });

        return Map(await _chat.SendMessageAsync(studentId, isCounselor: false, conversationId, request, ct));
    }

    [HttpPost("conversations/{conversationId:int}/read")]
    public async Task<IActionResult> MarkRead(int conversationId, CancellationToken ct)
    {
        if (!TryGetUserId(out var studentId))
            return Unauthorized(new { message = "Invalid session. Please log in again." });

        return await _chat.MarkReadAsync(studentId, isCounselor: false, conversationId, ct)
            ? Ok(new { message = "Marked as read." })
            : NotFound(new { message = "Conversation not found." });
    }

    [HttpGet("follow-ups")]
    public async Task<IActionResult> MyFollowUps(CancellationToken ct)
    {
        if (!TryGetUserId(out var studentId))
            return Unauthorized(new { message = "Invalid session. Please log in again." });

        return Ok(new { followUps = await _service.GetMyFollowUpsAsync(studentId, ct) });
    }

    [HttpPatch("follow-ups/{id:int}")]
    public async Task<IActionResult> RespondFollowUp(
        int id, [FromBody] RespondFollowUpRequest request, CancellationToken ct)
    {
        if (!TryGetUserId(out var studentId))
            return Unauthorized(new { message = "Invalid session. Please log in again." });

        return Map(await _service.RespondFollowUpAsync(studentId, id, request.Status, ct));
    }

    [HttpDelete("appointments/{id:int}")]
    public async Task<IActionResult> Cancel(int id, CancellationToken ct)
    {
        if (!TryGetUserId(out var studentId))
            return Unauthorized(new { message = "Invalid session. Please log in again." });

        return Map(await _service.CancelAsync(studentId, id, ct));
    }
}