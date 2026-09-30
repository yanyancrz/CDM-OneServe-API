using CDM_OneServe_API.Services.Library;
using Microsoft.AspNetCore.Mvc;

namespace CDM_OneServe_API.Controllers.Library;

[ApiController]
[Route("api/library/access-pass")]
public class LibraryAccessPassController : ControllerBase
{
    private readonly LibraryAccessPassService _accessPassService;

    public LibraryAccessPassController(
        LibraryAccessPassService accessPassService)
    {
        _accessPassService = accessPassService;
    }

    [HttpGet("{userId}")]
    public async Task<IActionResult> GetAccessPass(int userId)
    {
        var result = await _accessPassService
            .GetAccessPassAsync(userId);

        if (result == null)
        {
            return NotFound(new
            {
                success = false,
                message = "Student account not found."
            });
        }

        return Ok(new
        {
            success = true,
            data = result
        });
    }
}