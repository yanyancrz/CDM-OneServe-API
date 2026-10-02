using CDM_OneServe_API.DTOs.Library;
using CDM_OneServe_API.Services.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CDM_OneServe_API.Controllers.Library;

[ApiController]
[Route("api/library/reservations")]
public class ReservationController : ControllerBase
{
    private readonly ReservationService _reservationService;

    public ReservationController(ReservationService reservationService)
    {
        _reservationService = reservationService;
    }

    // =========================================================
    // CREATE RESERVATION
    // POST: api/library/reservations
    //
    // Body: { "userId": 17, "bookId": 23 }
    //
    // Reservation is AUTOMATICALLY created.
    // NO ADMIN APPROVAL REQUIRED.
    // =========================================================
    [HttpPost]
    public async Task<IActionResult> ReserveBook(
        [FromBody] ReserveBookRequest request)
    {
        Console.WriteLine("==========================================");
        Console.WriteLine("[RESERVATION CONTROLLER] CREATE");

        if (!ModelState.IsValid)
        {
            Console.WriteLine("[RESERVATION CONTROLLER] Invalid request.");

            return BadRequest(new
            {
                success = false,
                message = "Invalid reservation data.",
                errors = ModelState
            });
        }

        Console.WriteLine($"[RESERVATION CONTROLLER] UserId = {request.UserId}");
        Console.WriteLine($"[RESERVATION CONTROLLER] BookId = {request.BookId}");

        try
        {
            var result =
                await _reservationService.ReserveBookAsync(
                    request.UserId,
                    request.BookId
                );

            if (!result.Success)
            {
                Console.WriteLine(
                    $"[RESERVATION CONTROLLER] FAILED: {result.Message}"
                );

                return BadRequest(result);
            }

            Console.WriteLine(
                "[RESERVATION CONTROLLER] Reservation created successfully."
            );
            Console.WriteLine("==========================================");

            return Ok(result);
        }
        catch (Exception ex)
        {
            Console.WriteLine("==========================================");
            Console.WriteLine("[RESERVATION CONTROLLER] ERROR");
            Console.WriteLine(ex.Message);
            Console.WriteLine(ex.InnerException?.Message);
            Console.WriteLine("==========================================");

            return StatusCode(500, new
            {
                success = false,
                message = "An error occurred while creating the reservation.",
                error = ex.Message
            });
        }
    }


    // =========================================================
    // GET MY RESERVATIONS
    // GET: api/library/reservations/student/{userId}
    // =========================================================
    [HttpGet("student/{userId:int}")]
    public async Task<IActionResult> GetMyReservations(int userId)
    {
        Console.WriteLine(
            $"[RESERVATION CONTROLLER] GET reservations for UserId={userId}"
        );

        try
        {
            var result =
                await _reservationService.GetMyReservationsAsync(userId);

            Console.WriteLine(
                $"[RESERVATION CONTROLLER] Found {result.Count} reservations."
            );

            return Ok(result);
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[RESERVATION CONTROLLER] GET ERROR: {ex.Message}"
            );

            return StatusCode(500, new
            {
                success = false,
                message = "Failed to load reservations.",
                error = ex.Message
            });
        }
    }


    // =========================================================
    // CANCEL RESERVATION
    // PUT: api/library/reservations/cancel/{reservationId}
    // =========================================================
    [HttpPut("cancel/{reservationId:int}")]
    public async Task<IActionResult> CancelReservation(int reservationId)
    {
        Console.WriteLine(
            $"[RESERVATION CONTROLLER] CANCEL ID={reservationId}"
        );

        try
        {
            var result =
                await _reservationService.CancelReservationAsync(reservationId);

            if (!result.Success)
            {
                return BadRequest(result);
            }

            return Ok(result);
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[RESERVATION CONTROLLER] CANCEL ERROR: {ex.Message}"
            );

            return StatusCode(500, new
            {
                success = false,
                message = "Failed to cancel reservation.",
                error = ex.Message
            });
        }
    }


    // =========================================================
    // EXPIRE RESERVATIONS
    // PUT: api/library/reservations/expire
    //
    // Automatically marks expired reservations.
    // =========================================================
    [HttpPut("expire")]
    public async Task<IActionResult> ExpireReservations()
    {
        Console.WriteLine(
            "[RESERVATION CONTROLLER] CHECKING EXPIRED RESERVATIONS"
        );

        try
        {
            var count = await _reservationService.ExpireReservationsAsync();

            return Ok(new
            {
                success = true,
                message = $"{count} reservation(s) expired.",
                totalExpired = count
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[RESERVATION CONTROLLER] EXPIRE ERROR: {ex.Message}"
            );

            return StatusCode(500, new
            {
                success = false,
                message = "Failed to expire reservations.",
                error = ex.Message
            });
        }
    }


    // =========================================================
    // CLAIM RESERVED BOOK
    // POST: api/library/reservations/claim/{reservationId}
    //
    // STAFF ONLY (LibraryAdmin / LibraryStaff / SuperAdmin)
    //
    // Flow:
    // Reserved -> Staff scans Access Pass -> Staff clicks Claimed
    // -> Reservation = Claimed -> BorrowTransaction = Borrowed
    // =========================================================
    [Authorize(Roles = "LibraryAdmin,LibraryStaff,SuperAdmin")]
    [HttpPost("claim/{reservationId:int}")]
    public async Task<IActionResult> ClaimReservedBook(int reservationId)
    {
        Console.WriteLine(
            $"[RESERVATION CONTROLLER] CLAIM ID={reservationId}"
        );

        try
        {
            var result =
                await _reservationService.ClaimReservedBookAsync(reservationId);

            if (!result.Success)
            {
                return BadRequest(result);
            }

            return Ok(result);
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[RESERVATION CONTROLLER] CLAIM ERROR: {ex.Message}"
            );

            return StatusCode(500, new
            {
                success = false,
                message = "Failed to claim reservation.",
                error = ex.Message
            });
        }
    }
}