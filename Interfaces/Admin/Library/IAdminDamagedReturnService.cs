using CDM_OneServe_API.DTOs.Admin.Library.Common;
using CDM_OneServe_API.DTOs.Admin.Library.Returns;

namespace CDM_OneServe_API.Interfaces.Admin.Library;

public interface IAdminDamagedReturnService
{
    // Returned copies recorded as Damaged, with their replacement deadline.
    Task<PagedResult<DamagedReturnDto>> GetAsync(DamagedReturnFilterDto filter, CancellationToken cancellationToken);

    // The library sets (or changes) the deadline for the patron.
    Task<LibraryResult<DamagedReturnDto>> SetDueDateAsync(
        int transactionId, SetDamagedDueDateDto dto, int staffId, CancellationToken cancellationToken);

    // The replacement/repaired copy is back on the shelf. Restores one copy.
    Task<LibraryResult<DamagedReturnDto>> ResolveAsync(
        int transactionId, int staffId, CancellationToken cancellationToken);
}
