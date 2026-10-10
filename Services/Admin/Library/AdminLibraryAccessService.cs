using CDM_OneServe_API.Authorization;
using CDM_OneServe_API.Data;
using CDM_OneServe_API.DTOs.Admin.Library.Access;
using CDM_OneServe_API.Interfaces.Admin.Library;
using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Services.Admin.Library;

public class AdminLibraryAccessService : IAdminLibraryAccessService
{
    private readonly AppDbContext _context;

    public AdminLibraryAccessService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<LibraryAccessResult> GetAccessAsync(
        int userId,
        CancellationToken cancellationToken = default)
    {
        // Only the columns we need. Never touches PasswordHash / OTP data.
        var user = await _context.Users
            .AsNoTracking()
            .Where(x => x.Id == userId)
            .Select(x => new
            {
                x.Id,
                x.FullName,
                x.Email,
                x.Role,
                x.AdminModule,
                x.AccountStatus
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (user == null)
        {
            return LibraryAccessResult.Denied(LibraryAccessFailure.UserNotFound);
        }

        // The JWT stays valid for 8 hours, so the account can be suspended or
        // deleted after the token was issued. Always check the live status.
        if (!string.Equals(user.AccountStatus, "Active", StringComparison.OrdinalIgnoreCase))
        {
            return LibraryAccessResult.Denied(LibraryAccessFailure.AccountInactive);
        }

        // Same reason: the role may have changed since login.
        if (!LibraryRoles.IsLibraryRole(user.Role))
        {
            return LibraryAccessResult.Denied(LibraryAccessFailure.NotLibraryRole);
        }

        var isHead = user.Role == LibraryRoles.Head;

        return LibraryAccessResult.Success(new LibraryAccessDto
        {
            Id = user.Id,
            FullName = user.FullName ?? string.Empty,
            Email = user.Email ?? string.Empty,
            Role = user.Role!,
            RoleLabel = LibraryRoles.Label(user.Role),
            IsHead = isHead,
            IsStaff = !isHead,
            AdminModule = user.AdminModule,
            Permissions = BuildPermissions(isHead)
        });
    }

    private static LibraryPermissionsDto BuildPermissions(bool isHead) => new()
    {
        CanViewDashboard = true,
        CanManageBooks = true,
        CanDeleteBooks = isHead,
        CanViewPatrons = true,
        CanManageCirculation = true,
        CanManageReservations = true,
        CanViewReports = true,
        CanManageStaff = isHead,
        CanManageSettings = isHead,
        CanEditTerms = isHead
    };
}