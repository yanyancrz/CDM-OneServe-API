using Microsoft.AspNetCore.Mvc;

namespace CDM_OneServe_API.Controllers.LostFound;

[ApiController]
[Route("api/lostfound")]
public class LostFoundPhotoController : ControllerBase
{
    private readonly IWebHostEnvironment _environment;

    private static readonly string[] AllowedExtensions =
    {
        ".jpg",
        ".jpeg",
        ".png",
        ".webp"
    };

    private const long MaxFileSize = 5 * 1024 * 1024; // 5 MB

    public LostFoundPhotoController(IWebHostEnvironment environment)
    {
        _environment = environment;
    }

    // POST: /api/lostfound/upload-photo
    [HttpPost("upload-photo")]
    [RequestSizeLimit(MaxFileSize)]
    public async Task<IActionResult> UploadPhoto(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { message = "Please select an image." });

        if (file.Length > MaxFileSize)
            return BadRequest(new { message = "Photo must be 5 MB or smaller." });

        if (string.IsNullOrWhiteSpace(file.ContentType) ||
            !file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { message = "Only image files are allowed." });
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();

        if (!AllowedExtensions.Contains(extension))
            return BadRequest(new { message = "Allowed formats: JPG, JPEG, PNG, and WEBP." });

        var uploadFolder = Path.Combine(
            _environment.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot"),
            "uploads",
            "lostfound");

        Directory.CreateDirectory(uploadFolder);

        var fileName = $"{Guid.NewGuid():N}{extension}";
        var filePath = Path.Combine(uploadFolder, fileName);

        await using var stream = new FileStream(
            filePath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None);

        await file.CopyToAsync(stream);

        var photoUrl = $"/uploads/lostfound/{fileName}";

        return Ok(new
        {
            photo = photoUrl
        });
    }
}