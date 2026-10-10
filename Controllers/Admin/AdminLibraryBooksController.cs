using System.Security.Claims;

using CDM_OneServe_API.Authorization;
using CDM_OneServe_API.DTOs.Admin.Library.Books;
using CDM_OneServe_API.DTOs.Admin.Library.Common;
using CDM_OneServe_API.Interfaces.Admin.Library;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CDM_OneServe_API.Controllers.Admin;

[ApiController]
[Route("api/admin/library/books")]
[Authorize(Roles = LibraryRoles.Access)]
public class AdminLibraryBooksController : ControllerBase
{
    private const long MaxUploadBytes = 55L * 1024 * 1024;

    private readonly IAdminBookService _books;

    public AdminLibraryBooksController(IAdminBookService books)
    {
        _books = books;
    }

    // GET /api/admin/library/books
    [HttpGet]
    public async Task<IActionResult> GetBooks([FromQuery] BookFilterDto filter, CancellationToken cancellationToken)
    {
        var result = await _books.GetBooksAsync(filter, cancellationToken);
        return Ok(ApiResponse.Ok(result));
    }

    // GET /api/admin/library/books/{id}
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetBook(int id, CancellationToken cancellationToken)
        => ToAction(await _books.GetBookAsync(id, cancellationToken));

    // POST /api/admin/library/books
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateBookDto dto, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();
        return ToAction(await _books.CreateAsync(dto, userId, cancellationToken));
    }

    // PUT /api/admin/library/books/{id}
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateBookDto dto, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();
        return ToAction(await _books.UpdateAsync(id, dto, userId, cancellationToken));
    }

    // DELETE /api/admin/library/books/{id}   (Library Head only)
    [HttpDelete("{id:int}")]
    [Authorize(Roles = "LibraryAdmin")] // use the Head role constant from LibraryRoles if you have one
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();
        return ToAction(await _books.DeleteAsync(id, userId, cancellationToken));
    }

    // POST /api/admin/library/books/{id}/upload-cover
    [HttpPost("{id:int}/upload-cover")]
    [RequestSizeLimit(MaxUploadBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadBytes)]
    public async Task<IActionResult> UploadCover(int id, IFormFile file, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();
        return ToAction(await _books.UploadCoverAsync(id, file, userId, cancellationToken));
    }

    // POST /api/admin/library/books/{id}/upload-pdf
    [HttpPost("{id:int}/upload-pdf")]
    [RequestSizeLimit(MaxUploadBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadBytes)]
    public async Task<IActionResult> UploadPdf(int id, IFormFile file, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();
        return ToAction(await _books.UploadPdfAsync(id, file, userId, cancellationToken));
    }

    // ----- helpers -----

    private bool TryGetUserId(out int userId) =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

    private IActionResult InvalidSession() =>
        Unauthorized(ApiResponse.Fail("Invalid session. Please log in again."));

    private IActionResult ToAction<T>(LibraryResult<T> result) =>
        result.Error == null
            ? StatusCode(result.StatusCode, ApiResponse.Ok(result.Data))
            : StatusCode(result.StatusCode, ApiResponse.Fail(result.Error));
}