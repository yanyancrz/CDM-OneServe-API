using CDM_OneServe_API.DTOs;
using CDM_OneServe_API.Models;
using CDM_OneServe_API.Services;
using CDMOneServe.API.DTOs.Library;
using Microsoft.AspNetCore.Mvc;

namespace CDM_OneServe_API.Controllers;

[ApiController]
[Route("api/library/books")]
public class BooksController : ControllerBase
{
    private readonly BookService _bookService;

    public BooksController(BookService bookService)
    {
        _bookService = bookService;
    }

    // ============================
    // GET ALL BOOKS
    // ============================
    [HttpGet]
    public async Task<IActionResult> GetAllBooks()
    {
        var books = await _bookService.GetAllBooksAsync();

        return Ok(new
        {
            success = true,
            data = books
        });
    }

    // ============================
    // GET BOOK BY ID
    // ============================
    [HttpGet("{bookId}")]
    public async Task<IActionResult> GetBookById(int bookId)
    {
        var book = await _bookService.GetBookByIdAsync(bookId);

        if (book == null)
        {
            return NotFound(new
            {
                success = false,
                message = "Book not found."
            });
        }

        return Ok(new
        {
            success = true,
            data = book
        });
    }

    // ============================
    // CREATE BOOK
    // ============================
    [HttpPost]
    public async Task<IActionResult> CreateBook(CreateBookDto dto)
    {
        var result = await _bookService.CreateBookAsync(dto);

        if (!result.Success)
            return BadRequest(result);

        return Ok(result);
    }

    // ============================
    // UPDATE BOOK
    // ============================
    [HttpPut("{bookId}")]
    public async Task<IActionResult> UpdateBook(
        int bookId,
        UpdateBookDto dto)
    {
        var result = await _bookService.UpdateBookAsync(bookId, dto);

        if (!result.Success)
            return BadRequest(result);

        return Ok(result);
    }

    // ============================
    // DELETE BOOK
    // ============================
    [HttpDelete("{bookId}")]
    public async Task<IActionResult> DeleteBook(int bookId)
    {
        var result = await _bookService.DeleteBookAsync(bookId);

        if (!result.Success)
            return BadRequest(result);

        return Ok(result);
    }
}