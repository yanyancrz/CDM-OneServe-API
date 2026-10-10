using CDM_OneServe_API.DTOs.Admin.Library.Policy;

namespace CDM_OneServe_API.Interfaces.Admin.Library;

// Read-only. Shared by the student and faculty app.
public interface ILibraryPolicyService
{
    Task<LibraryPolicyDto> GetPolicyAsync(CancellationToken cancellationToken);
}