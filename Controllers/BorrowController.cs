using CDM_OneServe_API.DTOs;
using CDM_OneServe_API.Models;
using CDM_OneServe_API.Services;
using Microsoft.AspNetCore.Mvc;

namespace CDM_OneServe_API.Controllers;

[ApiController]
[Route("api/library/borrow")]
public class BorrowController : ControllerBase
{
    private readonly BorrowService _borrowService;

    public BorrowController(BorrowService borrowService)
    {
        _borrowService = borrowService;
    }

    // ============================
    // GET: api/library/borrow
    // ============================
    [HttpGet]
    public async Task<IActionResult> GetAllBorrowTransactions()
    {
        var result = await _borrowService.GetAllBorrowTransactionsAsync();
        return Ok(result);
    }

    // ============================
    // GET: api/library/borrow/5
    // ============================
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
        var result = await _borrowService.GetBorrowHistoryAsync(userId);

        return Ok(result);
    }

    // ============================
    // POST: api/library/borrow
    // ============================
    [HttpPost]
    public async Task<IActionResult> BorrowBook([FromBody] BorrowTransaction transaction)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var result = await _borrowService.BorrowBookAsync(transaction);

        if (!result.Success)
            return BadRequest(result);

        return Ok(result);
    }

    // ============================
    // PUT: api/library/borrow/return/5
    // ============================
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
        var result = await _borrowService.GetCurrentBorrowedBooksAsync(userId);

        return Ok(result);
    }

    // ============================
    // GET: api/library/borrow/overdue
    // ============================
    [HttpGet("overdue")]
    public async Task<IActionResult> GetOverdueBooks()
    {
        var result = await _borrowService.GetOverdueBooksAsync();

        return Ok(result);
    }
}