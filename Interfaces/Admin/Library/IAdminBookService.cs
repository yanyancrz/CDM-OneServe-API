using CDM_OneServe_API.DTOs.Admin.Library.Books;
using CDM_OneServe_API.DTOs.Admin.Library.Common;

using Microsoft.AspNetCore.Http;

namespace CDM_OneServe_API.Interfaces.Admin.Library;

public interface IAdminBookService
{
    Task<PagedResult<BookDto>> GetBooksAsync(BookFilterDto filter, CancellationToken cancellationToken);

    Task<LibraryResult<BookDto>> GetBookAsync(int id, CancellationToken cancellationToken);

    Task<LibraryResult<BookDto>> CreateAsync(CreateBookDto dto, int userId, CancellationToken cancellationToken);

    Task<LibraryResult<BookDto>> UpdateAsync(int id, UpdateBookDto dto, int userId, CancellationToken cancellationToken);

    Task<LibraryResult<bool>> DeleteAsync(int id, int userId, CancellationToken cancellationToken);

    Task<LibraryResult<BookDto>> UploadCoverAsync(int id, IFormFile file, int userId, CancellationToken cancellationToken);

    Task<LibraryResult<BookDto>> UploadPdfAsync(int id, IFormFile file, int userId, CancellationToken cancellationToken);
}