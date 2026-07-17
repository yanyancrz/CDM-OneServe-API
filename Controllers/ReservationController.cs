using CDM_OneServe_API.Services;
using Microsoft.AspNetCore.Mvc;


[ApiController]
[Route("api/library/reservations")]
public class ReservationController : ControllerBase
{
    private readonly ReservationService _reservationService;

    public ReservationController(ReservationService reservationService)
    {
        _reservationService = reservationService;
    }

    [HttpPost]
    public async Task<IActionResult> ReserveBook(int userId, int bookId)
    {
        var result = await _reservationService.ReserveBookAsync(userId, bookId);

        if (!result.Success)
            return BadRequest(result);

        return Ok(result);
    }

    // ============================
    // GET: api/library/reservations/student/5
    // ============================
    [HttpGet("student/{userId}")]
    public async Task<IActionResult> GetMyReservations(int userId)
    {
        var result = await _reservationService.GetMyReservationsAsync(userId);

        return Ok(result);
    }

    // ============================
    // PUT: api/library/reservations/cancel/5
    // ============================
    [HttpPut("cancel/{reservationId}")]
    public async Task<IActionResult> CancelReservation(int reservationId)
    {
        var result = await _reservationService.CancelReservationAsync(reservationId);

        if (!result.Success)
            return BadRequest(result);

        return Ok(result);
    }

    // ============================
    // PUT: api/library/reservations/approve/5
    // ============================
    [HttpPut("approve/{reservationId}")]
    public async Task<IActionResult> ApproveReservation(int reservationId)
    {
        var result = await _reservationService.ApproveReservationAsync(reservationId);

        if (!result.Success)
            return BadRequest(result);

        return Ok(result);
    }

    // ============================
    // PUT: api/library/reservations/reject/5
    // ============================
    [HttpPut("reject/{reservationId}")]
    public async Task<IActionResult> RejectReservation(int reservationId)
    {
        var result = await _reservationService.RejectReservationAsync(reservationId);

        if (!result.Success)
            return BadRequest(result);

        return Ok(result);
    }

    // ============================
    // PUT: api/library/reservations/expire
    // ============================
    [HttpPut("expire")]
    public async Task<IActionResult> ExpireReservations()
    {
        int count = await _reservationService.ExpireReservationsAsync();

        return Ok(new
        {
            Success = true,
            Message = $"{count} reservation(s) expired.",
            TotalExpired = count
        });
    }

    // ============================
    // POST: api/library/reservations/claim/5
    // ============================
    [HttpPost("claim/{reservationId}")]
    public async Task<IActionResult> ClaimReservedBook(int reservationId)
    {
        var result = await _reservationService.ClaimReservedBookAsync(reservationId);

        if (!result.Success)
            return BadRequest(result);

        return Ok(result);
    }
}