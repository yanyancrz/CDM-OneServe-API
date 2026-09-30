using System.Security.Claims;

using CDM_OneServe_API.Services.Library;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CDM_OneServe_API.Controllers.Library;

[ApiController]
[Route("api/library/scanner")]
[Authorize(Roles = "LibraryAdmin,LibraryStaff,SuperAdmin")]
public class LibraryScannerController : ControllerBase
{
    private readonly LibraryScannerService _scannerService;
    private readonly ILogger<LibraryScannerController> _logger;

    public LibraryScannerController(
        LibraryScannerService scannerService,
        ILogger<LibraryScannerController> logger)
    {
        _scannerService = scannerService;
        _logger = logger;
    }

    // POST: api/library/scanner/verify
    [HttpPost("verify")]
    public async Task<IActionResult> VerifyQr(
        [FromBody] VerifyQrRequest request)
    {
        if (request == null ||
            string.IsNullOrWhiteSpace(request.QrData))
        {
            return BadRequest(new
            {
                success = false,
                message = "QR data is required."
            });
        }

        try
        {
            var scannedBy =
                User.FindFirstValue(ClaimTypes.NameIdentifier);

            _logger.LogInformation(
                "Scanner verify called by user {UserId} ({Role})",
                scannedBy,
                User.FindFirstValue(ClaimTypes.Role));

            var result =
                await _scannerService.VerifyQrAsync(
                    request.QrData.Trim());

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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Scanner verify failed.");

            return StatusCode(500, new
            {
                success = false,
                message = "Unable to verify QR code.",
                error = ex.Message
            });
        }
    }
}

public class VerifyQrRequest
{
    public string QrData { get; set; } = string.Empty;
}