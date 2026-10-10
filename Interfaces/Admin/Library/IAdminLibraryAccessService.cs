using CDM_OneServe_API.DTOs.Admin.Library.Access;

namespace CDM_OneServe_API.Interfaces.Admin.Library;

public interface IAdminLibraryAccessService
{
    /// <summary>
    /// Re-reads the user from the database (not from the JWT) and returns
    /// their current Library role and permissions.
    /// </summary>
    Task<LibraryAccessResult> GetAccessAsync(
        int userId,
        CancellationToken cancellationToken = default);
}