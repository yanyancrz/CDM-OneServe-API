using CDM_OneServe_API.Models.Platform;
using CDM_OneServe_API.Services.Admin.Platform;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace CDM_OneServe_API.Authorization;

/// <summary>
/// Extends the existing Library API to the central Admin while checking the
/// database-backed service permission on every request. Library Head and
/// Staff retain their existing authorization flow.
/// </summary>
public sealed class CentralLibraryPermissionFilter : IAsyncActionFilter
{
    private readonly PermissionService _permissions;

    public CentralLibraryPermissionFilter(PermissionService permissions) => _permissions = permissions;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var request = context.HttpContext.Request;
        if (!request.Path.StartsWithSegments("/api/admin/library") ||
            !context.HttpContext.User.IsInRole(PermissionCatalog.AdminRole))
        {
            await next();
            return;
        }

        var minimum = HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method)
            ? PermissionLevels.View
            : PermissionLevels.Manage;
        var allowed = await _permissions.CanAsync(
            context.HttpContext.User,
            PermissionCatalog.LibraryView,
            PermissionLevels.View,
            context.HttpContext.RequestAborted);

        if (allowed && minimum == PermissionLevels.Manage)
        {
            allowed = await _permissions.CanAsync(
                context.HttpContext.User,
                PermissionCatalog.LibraryManage,
                PermissionLevels.Manage,
                context.HttpContext.RequestAborted);
        }

        if (!allowed)
        {
            context.Result = new ObjectResult(new { message = "You do not have permission to access the Library service." })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
            return;
        }

        await next();
    }
}
