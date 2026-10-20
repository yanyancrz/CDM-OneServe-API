using CDM_OneServe_API.Authorization;
using CDM_OneServe_API.Data;
using CDM_OneServe_API.DTOs;
using CDM_OneServe_API.Models.Library;
using CDM_OneServe_API.Services.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Controllers.Library;

[ApiController]
[Authorize]
[Route("api/library/borrow")]
public class BorrowController : ControllerBase
{
    private readonly BorrowService _borrowService;
    private readonly AppDbContext _context;

    public BorrowController(
        BorrowService borrowService,
        AppDbContext context)
    {
        _borrowService = borrowService;
        _context = context;
    }

    private bool CanAccessUser(int userId) =>
        User.TryGetUserId(out var currentUserId) &&
        (currentUserId == userId || User.IsLibraryStaff());

    // ============================
    // GET: api/library/borrow
    // ============================
    [Authorize(Roles = "LibraryAdmin,LibraryStaff,SuperAdmin")]
    [HttpGet]
    public async Task<IActionResult> GetAllBorrowTransactions()
    {
        var result = await _borrowService.GetAllBorrowTransactionsAsync();
        return Ok(result);
    }

    // ============================
    // GET: api/library/borrow/5
    // ============================
    [Authorize(Roles = "LibraryAdmin,LibraryStaff,SuperAdmin")]
    [HttpGet("{id}")]
    public async Task<IActionResult> GetBorrowById(int id)
    {
        var result = await _borrowService.GetBorrowByIdAsync(id);

        if (result == null)
            return NotFound(new
            {
                success = false,
                message = "Borrow transaction not found."
            });

        return Ok(result);
    }

    // ============================
    // GET: api/library/borrow/student/12
    // ============================
    [HttpGet("student/{userId}")]
    public async Task<IActionResult> GetBorrowHistory(int userId)
    {
        if (!CanAccessUser(userId))
        {
            return Forbid();
        }

        var result = await _borrowService.GetBorrowHistoryAsync(userId);

        return Ok(result);
    }

    // ============================
    // GET: api/library/borrow/count/5
    // Successful borrowed books ONLY
    // ============================
    [HttpGet("count/{userId}")]
    public async Task<IActionResult> GetSuccessfulBorrowedCount(int userId)
    {
        if (!CanAccessUser(userId))
        {
            return Forbid();
        }

        var result =
            await _borrowService.GetSuccessfulBorrowedCountAsync(userId);

        return Ok(new
        {
            success = true,
            count = result
        });
    }

    // ============================
    // POST: api/library/borrow
    // ============================
    [HttpPost]
    public async Task<IActionResult> BorrowBook([FromBody] BorrowTransaction transaction)
    {
        if (!User.TryGetUserId(out var currentUserId))
        {
            return Unauthorized();
        }

        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        // Students may only submit a claim for themselves. Library staff
        // may create one for a patron. The primary key is never client-set.
        if (!User.IsLibraryStaff())
        {
            if (transaction.UserId > 0 &&
                transaction.UserId != currentUserId)
            {
                return Forbid();
            }

            transaction.UserId = currentUserId;
        }

        transaction.BorrowId = 0;

        var result = await _borrowService.BorrowBookAsync(transaction);

        if (!result.Success)
            return BadRequest(result);

        return Ok(result);
    }

    // ============================
    // PUT: api/library/borrow/return/5
    // ============================
    [Authorize(Roles = "LibraryAdmin,LibraryStaff,SuperAdmin")]
    [HttpPut("return/{borrowId}")]
    public async Task<IActionResult> ReturnBook(int borrowId)
    {
        var result = await _borrowService.ReturnBookAsync(borrowId);

        if (!result.Success)
            return BadRequest(result);

        return Ok(result);
    }

    // ============================
    // PUT: api/library/borrow/renew/5
    // ============================
    [HttpPut("renew/{borrowId}")]
    public async Task<IActionResult> RenewBook(int borrowId)
    {
        if (!User.TryGetUserId(out var currentUserId))
        {
            return Unauthorized();
        }

        if (!User.IsLibraryStaff())
        {
            var borrow = await _context.BorrowTransactions
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.BorrowId == borrowId);

            if (borrow == null)
            {
                return NotFound(new
                {
                    success = false,
                    message = "Borrow transaction not found."
                });
            }

            if (borrow.UserId != currentUserId)
            {
                return Forbid();
            }
        }

        var result = await _borrowService.RenewBookAsync(borrowId);

        if (!result.Success)
            return BadRequest(result);

        return Ok(result);
    }

    // ============================
    // GET: api/library/borrow/current/5
    // ============================
    [HttpGet("current/{userId}")]
    public async Task<IActionResult> GetCurrentBorrowedBooks(int userId)
    {
        if (!CanAccessUser(userId))
        {
            return Forbid();
        }

        var result = await _borrowService.GetCurrentBorrowedBooksAsync(userId);

        return Ok(result);
    }

    // ============================
    // GET: api/library/borrow/overdue
    // ============================
    [Authorize(Roles = "LibraryAdmin,LibraryStaff,SuperAdmin")]
    [HttpGet("overdue")]
    public async Task<IActionResult> GetOverdueBooks()
    {
        var result = await _borrowService.GetOverdueBooksAsync();

        return Ok(result);
    }
}