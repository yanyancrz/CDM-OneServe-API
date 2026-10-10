using System.Security.Claims;

using CDM_OneServe_API.Authorization;
using CDM_OneServe_API.DTOs.Admin.Library.Common;
using CDM_OneServe_API.DTOs.Admin.Library.Reservations;
using CDM_OneServe_API.Interfaces.Admin.Library;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace CDM_OneServe_API.Controllers.Admin;

[ApiController]
[Route("api/admin/library/reservations")]
[Authorize(Roles = LibraryRoles.Access)]
public class AdminLibraryReservationsController : ControllerBase
{
    private readonly IAdminReservationService _reservations;

    public AdminLibraryReservationsController(IAdminReservationService reservations)
    {
        _reservations = reservations;
    }

    // GET /api/admin/library/reservations?search=&status=&patronType=&sort=&page=&pageSize=
    [HttpGet]
    public async Task<IActionResult> GetReservations([FromQuery] ReservationFilterDto filter, CancellationToken cancellationToken)
        => Ok(ApiResponse.Ok(await _reservations.GetReservationsAsync(filter, cancellationToken)));

    // GET /api/admin/library/reservations/summary
    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary(CancellationToken cancellationToken)
        => Ok(ApiResponse.Ok(await _reservations.GetSummaryAsync(cancellationToken)));

    // GET /api/admin/library/reservations/{id}
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetReservation(int id, CancellationToken cancellationToken)
        => ToAction(await _reservations.GetReservationAsync(id, cancellationToken));

    // POST /api/admin/library/reservations/{id}/approve      (Head only)
    [HttpPost("{id:int}/approve")]
    [Authorize(Roles = LibraryRoles.HeadOnly)]
    public async Task<IActionResult> Approve(int id, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var staffId)) return InvalidSession();

        return ToAction(await _reservations.ApproveAsync(id, staffId, cancellationToken));
    }

    // POST /api/admin/library/reservations/{id}/ready        (Head only)
    [HttpPost("{id:int}/ready")]
    [Authorize(Roles = LibraryRoles.HeadOnly)]
    public async Task<IActionResult> MarkReady(int id, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var staffId)) return InvalidSession();

        return ToAction(await _reservations.MarkReadyAsync(id, staffId, cancellationToken));
    }

    // POST /api/admin/library/reservations/{id}/reject       (Head only)
    // Body: { "reason": "..." }  (required)
    [HttpPost("{id:int}/reject")]
    [Authorize(Roles = LibraryRoles.HeadOnly)]
    public async Task<IActionResult> Reject(
        int id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ReservationReasonDto? dto,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var staffId)) return InvalidSession();

        return ToAction(await _reservations.RejectAsync(id, dto?.Reason, staffId, cancellationToken));
    }

    // POST /api/admin/library/reservations/{id}/cancel       (Head only)
    // Body: { "reason": "..." }  (optional)
    [HttpPost("{id:int}/cancel")]
    [Authorize(Roles = LibraryRoles.HeadOnly)]
    public async Task<IActionResult> Cancel(
        int id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ReservationReasonDto? dto,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var staffId)) return InvalidSession();

        return ToAction(await _reservations.CancelAsync(id, dto?.Reason, staffId, cancellationToken));
    }

    // POST /api/admin/library/reservations/{id}/claim        (Head + Staff, used by the scanner)
    // Convert-to-borrow. Body: { "loanDays": 7, "acceptedTerms": true }  (loanDays optional)
    [HttpPost("{id:int}/claim")]
    public async Task<IActionResult> Claim(
        int id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ClaimReservationDto? dto,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var staffId)) return InvalidSession();

        return ToAction(await _reservations.ClaimAsync(id, dto ?? new ClaimReservationDto(), staffId, cancellationToken));
    }

    private bool TryGetUserId(out int userId) =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

    private IActionResult InvalidSession() =>
        Unauthorized(ApiResponse.Fail("Invalid session. Please log in again."));

    private IActionResult ToAction<T>(LibraryResult<T> result) =>
        result.Error == null
            ? StatusCode(result.StatusCode, ApiResponse.Ok(result.Data))
            : StatusCode(result.StatusCode, ApiResponse.Fail(result.Error));
}
