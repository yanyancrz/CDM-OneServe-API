using CDM_OneServe_API.Models.Platform;
using CDM_OneServe_API.Services.Admin.Platform;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace CDM_OneServe_API.Authorization;

/// <summary>Applies live platform service permissions to central Admin Guidance API calls.</summary>
public sealed class CentralGuidancePermissionFilter : IAsyncActionFilter
{
    private readonly PermissionService _permissions;

    public CentralGuidancePermissionFilter(PermissionService permissions) => _permissions = permissions;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var request = context.HttpContext.Request;
        var path = request.Path.Value ?? string.Empty;
        if (!path.StartsWith("/api/admin/guidance", StringComparison.OrdinalIgnoreCase) ||
            !context.HttpContext.User.IsInRole(PermissionCatalog.AdminRole) &&
            !context.HttpContext.User.IsInRole(PermissionCatalog.SuperAdminRole))
        {
            await next();
            return;
        }

        // Counseling narratives and attachments remain exclusively in the
        // authorized Guidance Head console, regardless of platform grants.
        if (path.Contains("narrative-reports", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("reports/narrative", StringComparison.OrdinalIgnoreCase))
        {
            context.Result = new ObjectResult(new { message = "Counseling narrative contents are restricted to authorized Guidance staff." })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
            return;
        }

        if (path.StartsWith("/api/admin/guidance/students/", StringComparison.OrdinalIgnoreCase))
        {
            context.Result = new ObjectResult(new { message = "Central Admin can view the Guidance student directory and aggregate appointment counts, but not individual counseling histories." })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
            return;
        }

        var minimum = HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method)
            ? PermissionLevels.View
            : PermissionLevels.Manage;
        var allowed = await _permissions.CanAsync(context.HttpContext.User,
            PermissionCatalog.GuidanceView, PermissionLevels.View, context.HttpContext.RequestAborted);
        if (allowed && minimum == PermissionLevels.Manage)
        {
            allowed = await _permissions.CanAsync(context.HttpContext.User,
                PermissionCatalog.GuidanceManage, PermissionLevels.Manage, context.HttpContext.RequestAborted);
        }

        if (!allowed)
        {
            context.Result = new ObjectResult(new { message = "You do not have permission to access the Guidance service." })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
            return;
        }

        await next();
    }
}
