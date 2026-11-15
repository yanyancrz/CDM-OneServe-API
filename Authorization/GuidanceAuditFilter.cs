using CDM_OneServe_API.Services.Admin.Platform;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace CDM_OneServe_API.Authorization;

/// <summary>Records completed Guidance changes without counseling or request contents.</summary>
public sealed class GuidanceAuditFilter(AdminAuditService audit) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var http = context.HttpContext;
        var request = http.Request;
        var guidance = request.Path.StartsWithSegments("/api/admin/guidance") ||
            request.Path.StartsWithSegments("/api/guidance");
        var mutation = HttpMethods.IsPost(request.Method) || HttpMethods.IsPut(request.Method) ||
            HttpMethods.IsPatch(request.Method) || HttpMethods.IsDelete(request.Method);
        if (!guidance || !mutation || http.User.Identity?.IsAuthenticated != true)
        {
            await next();
            return;
        }

        var executed = await next();
        if (executed.Exception is not null) return;
        var status = executed.Result switch
        {
            ObjectResult result => result.StatusCode ?? 200,
            StatusCodeResult result => result.StatusCode,
            JsonResult result => result.StatusCode ?? 200,
            _ => http.Response.StatusCode
        };
        if (status < 200 || status >= 300) return;

        var action = context.ActionDescriptor as ControllerActionDescriptor;
        var entityId = context.RouteData.Values.Values
            .Select(value => int.TryParse(value?.ToString(), out var id) ? (int?)id : null)
            .FirstOrDefault(value => value.HasValue);
        // Use static controller/action metadata; never serialize patient information,
        // narrative text, messages, uploaded filenames, credentials or query strings.
        var row = await audit.StageAsync(http.User, "Guidance", "Guidance",
            $"{request.Method} {action?.ActionName ?? "Guidance change"}",
            entityType: action?.ControllerName, entityId: entityId,
            details: "Completed Guidance module activity.", ct: CancellationToken.None);
        AdminAuditService.AttachRequest(row, http);
        await audit.SaveAsync(CancellationToken.None);
    }
}
