using CDM_OneServe_API.Authorization;
using CDM_OneServe_API.Data;
using CDM_OneServe_API.DTOs.Admin.Library;
using CDM_OneServe_API.Services.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Controllers.Library;

[ApiController]
[Authorize]
[Route("api/library/reservations")]
public class ReservationController : ControllerBase
{
    private readonly ReservationService _reservationService;
    private readonly AppDbContext _context;

    public ReservationController(
        ReservationService reservationService,
        AppDbContext context)
    {
        _reservationService = reservationService;
        _context = context;
    }

    private bool CanAccessUser(int userId) =>
        User.TryGetUserId(out var currentUserId) &&
        (currentUserId == userId || User.IsLibraryStaff());

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

        if (!User.TryGetUserId(out var currentUserId))
        {
            return Unauthorized();
        }

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

        // Students reserve for themselves; library staff may reserve for a
        // patron. A body userId never overrides a student's identity.
        if (!User.IsLibraryStaff())
        {
            if (request.UserId > 0 &&
                request.UserId != currentUserId)
            {
                return Forbid();
            }

            request.UserId = currentUserId;
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
            Console.WriteLine(ex.ToString());
            Console.WriteLine("==========================================");

            return StatusCode(500, new
            {
                success = false,
                // Temporary: keep the detail visible while diagnosing the
                // production 500. Revert to the generic message afterwards.
                message =
                    $"An error occurred while creating the reservation. [{ex.Message}]",
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
        if (!CanAccessUser(userId))
        {
            return Forbid();
        }

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
        if (!User.TryGetUserId(out var currentUserId))
        {
            return Unauthorized();
        }

        if (!User.IsLibraryStaff())
        {
            var reservation = await _context.Reservations
                .AsNoTracking()
                .FirstOrDefaultAsync(r =>
                    r.ReservationId == reservationId);

            if (reservation == null)
            {
                return NotFound(new
                {
                    success = false,
                    message = "Reservation not found."
                });
            }

            if (reservation.UserId != currentUserId)
            {
                return Forbid();
            }
        }

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
    [Authorize(Roles = "LibraryAdmin,LibraryStaff,SuperAdmin")]
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