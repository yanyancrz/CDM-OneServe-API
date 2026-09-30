using CDM_OneServe_API.Services.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CDM_OneServe_API.Controllers.Library;

[ApiController]
[Route("api/library/scanner")]
[Authorize(Roles = "LibraryAdmin,LibraryStaff")]
public class LibraryScannerController : ControllerBase
{
    private readonly LibraryScannerService _scannerService;

    public LibraryScannerController(
        LibraryScannerService scannerService)
    {
        _scannerService = scannerService;
    }

    // POST: api/library/scanner/verify
    [HttpPost("verify")]
    public async Task<IActionResult> VerifyQr(
        [FromBody] VerifyQrRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.QrData))
        {
            return BadRequest(new
            {
                success = false,
                message = "QR data is required."
            });
        }

        var result =
            await _scannerService.VerifyQrAsync(request.QrData);

        if (result == null)
        {
            return NotFound(new
            {
                success = false,
                message = "Account not found."
            });
        }

        return Ok(new
        {
            success = true,
            data = result
        });
    }
}

public class VerifyQrRequest
{
    public string QrData { get; set; } = string.Empty;
}