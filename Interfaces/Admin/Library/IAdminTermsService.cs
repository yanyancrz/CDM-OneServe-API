using CDM_OneServe_API.DTOs.Admin.Library.Common;
using CDM_OneServe_API.DTOs.Admin.Library.Terms;

namespace CDM_OneServe_API.Interfaces.Admin.Library;

public interface IAdminTermsService
{
    Task<List<LibraryTermDto>> GetAsync(CancellationToken cancellationToken);

    Task<LibraryResult<List<LibraryTermDto>>> UpdateAsync(UpdateTermsDto dto, int headId, CancellationToken cancellationToken);
}