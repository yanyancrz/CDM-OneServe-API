using CDM_OneServe_API.DTOs.Admin.Library.Common;
using CDM_OneServe_API.DTOs.Admin.Library.Librarians;

namespace CDM_OneServe_API.Interfaces.Admin.Library;

public interface IAdminLibrarianService
{
    Task<PagedResult<LibrarianDto>> GetLibrariansAsync(LibrarianFilterDto filter, CancellationToken cancellationToken);

    Task<LibraryResult<LibrarianDto>> GetLibrarianAsync(int id, CancellationToken cancellationToken);

    Task<LibraryResult<LibrarianDto>> CreateAsync(CreateLibrarianDto dto, int headId, CancellationToken cancellationToken);

    Task<LibraryResult<LibrarianDto>> UpdateAsync(int id, UpdateLibrarianDto dto, int headId, CancellationToken cancellationToken);

    // Active -> Suspended (login is blocked). Reversible.
    Task<LibraryResult<LibrarianDto>> DisableAsync(int id, int headId, CancellationToken cancellationToken);

    Task<LibraryResult<LibrarianDto>> EnableAsync(int id, int headId, CancellationToken cancellationToken);

    Task<LibraryResult<ResetPasswordResultDto>> ResetPasswordAsync(int id, int headId, CancellationToken cancellationToken);

    Task<LibraryResult<List<LibrarianActivityDto>>> GetActivityAsync(int id, CancellationToken cancellationToken);
}