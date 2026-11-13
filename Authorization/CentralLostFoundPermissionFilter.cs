using CDM_OneServe_API.Models.Platform;
using CDM_OneServe_API.Services.Admin.Platform;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace CDM_OneServe_API.Authorization;

/// <summary>Checks central Admin Lost &amp; Found access against the live permission matrix.</summary>
public sealed class CentralLostFoundPermissionFilter : IAsyncActionFilter
{
    private readonly PermissionService _permissions;

    public CentralLostFoundPermissionFilter(PermissionService permissions) => _permissions = permissions;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var request = context.HttpContext.Request;
        if (!request.Path.StartsWithSegments("/api/lostfound/admin") ||
            !context.HttpContext.User.IsInRole(PermissionCatalog.AdminRole) &&
            !context.HttpContext.User.IsInRole(PermissionCatalog.SuperAdminRole))
        {
            await next();
            return;
        }

        var minimum = HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method)
            ? PermissionLevels.View
            : PermissionLevels.Manage;
        var allowed = await _permissions.CanAsync(context.HttpContext.User,
            PermissionCatalog.LostFoundView, PermissionLevels.View, context.HttpContext.RequestAborted);
        if (allowed && minimum == PermissionLevels.Manage)
        {
            allowed = await _permissions.CanAsync(context.HttpContext.User,
                PermissionCatalog.LostFoundManage, PermissionLevels.Manage, context.HttpContext.RequestAborted);
        }

        if (!allowed)
        {
            context.Result = new ObjectResult(new { message = "You do not have permission to access the Lost & Found service." })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
            return;
        }

        await next();
    }
}
