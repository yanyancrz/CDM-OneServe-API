using CDM_OneServe_API.DTOs.Admin.Library.Common;
using CDM_OneServe_API.Interfaces.Admin.Library;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CDM_OneServe_API.Controllers.Library;

// GET /api/library/policy
// Any signed-in account (student, faculty, library staff). Read-only.
[ApiController]
[Route("api/library/policy")]
[Authorize]
public class LibraryPolicyController : ControllerBase
{
    private readonly ILibraryPolicyService _policy;

    public LibraryPolicyController(ILibraryPolicyService policy)
    {
        _policy = policy;
    }

    [HttpGet]
    public async Task<IActionResult> GetPolicy(CancellationToken cancellationToken)
        => Ok(ApiResponse.Ok(await _policy.GetPolicyAsync(cancellationToken)));
}