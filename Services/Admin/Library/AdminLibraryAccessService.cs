using System.Security.Claims;

using CDM_OneServe_API.Authorization;
using CDM_OneServe_API.Data;
using CDM_OneServe_API.DTOs.Admin.Library.Access;
using CDM_OneServe_API.Interfaces.Admin.Library;
using CDM_OneServe_API.Models.Platform;
using CDM_OneServe_API.Services.Admin.Platform;
using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Services.Admin.Library;

public class AdminLibraryAccessService : IAdminLibraryAccessService
{
    private readonly AppDbContext _context;
    private readonly PermissionService _permissions;

    public AdminLibraryAccessService(AppDbContext context, PermissionService permissions)
    {
        _context = context;
        _permissions = permissions;
    }

    public async Task<LibraryAccessResult> GetAccessAsync(
        int userId,
        CancellationToken cancellationToken = default)
    {
        var user = await _context.Users
            .AsNoTracking()
            .Where(x => x.Id == userId)
            .Select(x => new { x.Id, x.FullName, x.Email, x.Role, x.AdminModule, x.AccountStatus })
            .FirstOrDefaultAsync(cancellationToken);

        if (user == null) return LibraryAccessResult.Denied(LibraryAccessFailure.UserNotFound);
        if (!string.Equals(user.AccountStatus, "Active", StringComparison.OrdinalIgnoreCase))
            return LibraryAccessResult.Denied(LibraryAccessFailure.AccountInactive);

        var isCentralAdmin = string.Equals(user.Role, PermissionCatalog.AdminRole, StringComparison.Ordinal);
        AdminIdentity? centralIdentity = null;
        if (isCentralAdmin)
        {
            var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString())
            }, "central-admin"));
            centralIdentity = await _permissions.ResolveAsync(principal, cancellationToken);
        }

        if (!LibraryRoles.IsLibraryRole(user.Role) && !isCentralAdmin)
            return LibraryAccessResult.Denied(LibraryAccessFailure.NotLibraryRole);

        if (isCentralAdmin && (centralIdentity is null ||
            !centralIdentity.Can(PermissionCatalog.LibraryView, PermissionLevels.View)))
            return LibraryAccessResult.Denied(LibraryAccessFailure.NotLibraryRole);

        var isHead = user.Role == LibraryRoles.Head ||
            (isCentralAdmin && centralIdentity!.Can(PermissionCatalog.LibraryManage, PermissionLevels.Manage));

        return LibraryAccessResult.Success(new LibraryAccessDto
        {
            Id = user.Id,
            FullName = user.FullName ?? string.Empty,
            Email = user.Email ?? string.Empty,
            Role = user.Role!,
            RoleLabel = isCentralAdmin ? "Central Admin" : LibraryRoles.Label(user.Role),
            IsHead = isHead,
            IsStaff = !isHead && !isCentralAdmin,
            AdminModule = user.AdminModule,
            Permissions = BuildPermissions(isHead, isCentralAdmin)
        });
    }

    private static LibraryPermissionsDto BuildPermissions(bool isHead, bool isCentralAdmin) => new()
    {
        CanViewDashboard = true,
        CanManageBooks = !isCentralAdmin || isHead,
        CanDeleteBooks = isHead,
        CanViewPatrons = true,
        CanManageCirculation = !isCentralAdmin || isHead,
        CanManageReservations = !isCentralAdmin || isHead,
        CanViewReports = true,
        CanManageStaff = isHead,
        CanManageSettings = isHead,
        CanEditTerms = isHead
    };
}
