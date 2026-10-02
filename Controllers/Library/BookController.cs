using CDM_OneServe_API.DTOs.Library;
using CDM_OneServe_API.Services.Library;
using Microsoft.AspNetCore.Mvc;

namespace CDM_OneServe_API.Controllers.Library;

[ApiController]
[Route("api/library/books")]
public class BooksController : ControllerBase
{
    private readonly BookService _bookService;

    public BooksController(BookService bookService)
    {
        _bookService = bookService;
    }

    // =========================================================
    // GET ALL BOOKS
    // GET: api/library/books
    // =========================================================
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


    // =========================================================
    // GET BOOK BY ID
    // GET: api/library/books/{bookId}
    // =========================================================
    [HttpGet("{bookId:int}")]
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


    // =========================================================
    // CREATE BOOK
    // POST: api/library/books
    // =========================================================
    [HttpPost]
    public async Task<IActionResult> CreateBook(
        [FromBody] CreateBookDto dto)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(new
            {
                success = false,
                message = "Invalid book data.",
                errors = ModelState
            });
        }

        var result = await _bookService.CreateBookAsync(dto);

        if (!result.Success)
        {
            return BadRequest(result);
        }

        return Ok(result);
    }


    // =========================================================
    // UPDATE BOOK
    // PUT: api/library/books/{bookId}
    // =========================================================
    [HttpPut("{bookId:int}")]
    public async Task<IActionResult> UpdateBook(
        int bookId,
        [FromBody] UpdateBookDto dto)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(new
            {
                success = false,
                message = "Invalid book data.",
                errors = ModelState
            });
        }

        var result = await _bookService.UpdateBookAsync(
            bookId,
            dto
        );

        if (!result.Success)
        {
            return BadRequest(result);
        }

        return Ok(result);
    }


    // =========================================================
    // DELETE BOOK
    // DELETE: api/library/books/{bookId}
    // =========================================================
    [HttpDelete("{bookId:int}")]
    public async Task<IActionResult> DeleteBook(int bookId)
    {
        var result = await _bookService.DeleteBookAsync(bookId);

        if (!result.Success)
        {
            return BadRequest(result);
        }

        return Ok(result);
    }
}