using CDM_OneServe_API.DTOs.Admin.Library.Common;
using CDM_OneServe_API.DTOs.Admin.Library.Settings;

namespace CDM_OneServe_API.Interfaces.Admin.Library;

public interface IAdminSettingsService
{
    Task<LibrarySettingsDto> GetAsync(CancellationToken cancellationToken);

    Task<LibraryResult<LibrarySettingsDto>> UpdateAsync(UpdateLibrarySettingsDto dto, int headId, CancellationToken cancellationToken);
}