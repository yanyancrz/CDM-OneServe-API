using CDM_OneServe_API.DTOs.Admin.Library.Borrowing;
using CDM_OneServe_API.DTOs.Admin.Library.Common;

namespace CDM_OneServe_API.Interfaces.Admin.Library;

public interface IAdminBorrowingService
{
    Task<PagedResult<BorrowListItemDto>> GetBorrowingsAsync(BorrowFilterDto filter, CancellationToken cancellationToken);

    Task<LibraryResult<BorrowListItemDto>> GetBorrowingAsync(int id, CancellationToken cancellationToken);

    Task<List<PatronLookupDto>> SearchPatronsAsync(string? search, CancellationToken cancellationToken);

    // identifier = student/employee ID number, or "uid:<userId>"
    Task<LibraryResult<PatronLookupDto>> GetPatronAsync(string identifier, CancellationToken cancellationToken);

    // qrData = the raw text decoded by the existing scanner
    Task<LibraryResult<PatronLookupDto>> ScanPatronAsync(string qrData, CancellationToken cancellationToken);

    Task<LibraryResult<BorrowResponseDto>> BorrowAsync(BorrowRequestDto dto, int staffId, CancellationToken cancellationToken);
}