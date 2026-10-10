using CDM_OneServe_API.Data;
using CDM_OneServe_API.DTOs.Admin.Library.Common;
using CDM_OneServe_API.DTOs.Marketplace;
using CDM_OneServe_API.Services.Marketplace;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Controllers.Marketplace;

/// <summary>
/// Product PHOTOS - a real upload, not a pasted URL.
///
///   POST   /api/marketplace/staff/products/{productId}/image
///   DELETE /api/marketplace/staff/products/{productId}/image
///
/// Why an upload rather than a URL field:
/// a staff member adding a uniform should pick the photo off their phone or
/// laptop, not be asked to host an image somewhere first and paste a link. The
/// file lands in wwwroot/uploads/market and the product stores the served path,
/// which is the same arrangement the profile picture and physical-ID uploads
/// already use.
///
/// Staff and Head only. Buyers cannot touch product media.
[ApiController]
[Route("api/marketplace/staff/products")]
[Authorize]
public class MarketplaceProductImageController : MarketplaceControllerBase
{
    private readonly AppDbContext _db;
    private readonly MarketplaceAccessService _access;

    public MarketplaceProductImageController(AppDbContext db, MarketplaceAccessService access)
    {
        _db = db;
        _access = access;
    }

    /// <summary>
    /// Uploads (or replaces) the photo for one product.
    ///
    /// The stored name is generated here, never taken from the client: a
    /// user-supplied filename is both a path-traversal risk and a collision
    /// waiting to happen when two staff members upload "uniform.png".
    /// </summary>
    [HttpPost("{productId:int}/image")]
    [RequestSizeLimit(8 * 1024 * 1024)]
    public async Task<IActionResult> Upload(int productId, IFormFile file, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        var actor = await _access.GetOperatorAsync(userId, ct);
        if (actor is null)
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse.Fail("Marketplace Staff access required."));

        if (file is null || file.Length == 0)
            return BadRequest(new { success = false, message = "Choose a photo to upload." });

        // 5 MB of pixels is far more than any product photo needs, and a bigger
        // ceiling would just invite someone to upload a 40 MB phone dump.
        if (file.Length > MaxBytes)
            return BadRequest(new
            {
                success = false,
                message = $"That photo is too large. Maximum is {MaxBytes / 1024 / 1024} MB."
            });

        // The extension comes from the sniffed content type, never from
        // FileName - otherwise an upload could be named ".html" or ".aspx" and
        // be served back as something executable.
        var extension = ExtensionFor(file.ContentType);
        if (extension is null)
            return BadRequest(new
            {
                success = false,
                message = "Only JPG, PNG, WEBP or GIF photos are accepted."
            });

        var product = await _db.MarketplaceProducts
            .FirstOrDefaultAsync(p => p.ProductId == productId, ct);

        if (product is null)
            return NotFound(new { success = false, message = "Product not found." });

        var folder = Path.Combine(
            Directory.GetCurrentDirectory(), "wwwroot", "uploads", "marketplace");

        Directory.CreateDirectory(folder);

        var fileName = $"{Guid.NewGuid():N}{extension}";
        var fullPath = Path.Combine(folder, fileName);

        await using (var stream = new FileStream(fullPath, FileMode.Create))
        {
            await file.CopyToAsync(stream, ct);
        }

        // Replace, don't accumulate: the old file is deleted so the uploads
        // folder does not fill with photos nobody references any more.
        DeleteStoredFile(product.ImageUrl, folder);

        product.ImageUrl = $"/uploads/marketplace/{fileName}";
        product.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        return Ok(new
        {
            success = true,
            imageUrl = product.ImageUrl,
            fileName,
            sizeBytes = file.Length,
            message = "Photo uploaded."
        });
    }

    /// <summary>
    /// Removes the product photo. The product falls back to the initials
    /// placeholder in the UI, which is why this is allowed and not just
    /// "replace with a blank file".
    /// </summary>
    [HttpDelete("{productId:int}/image")]
    public async Task<IActionResult> Remove(int productId, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        var actor = await _access.GetOperatorAsync(userId, ct);
        if (actor is null)
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse.Fail("Marketplace Staff access required."));

        var product = await _db.MarketplaceProducts
            .FirstOrDefaultAsync(p => p.ProductId == productId, ct);

        if (product is null)
            return NotFound(new { success = false, message = "Product not found." });

        var folder = Path.Combine(
            Directory.GetCurrentDirectory(), "wwwroot", "uploads", "marketplace");

        DeleteStoredFile(product.ImageUrl, folder);

        product.ImageUrl = null;
        product.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        return Ok(new { success = true, message = "Photo removed." });
    }

    // ==========================================================
    // helpers
    // ==========================================================

    private const long MaxBytes = 5 * 1024 * 1024;

    private static readonly Dictionary<string, string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = ".jpg",
        ["image/jpg"] = ".jpg",
        ["image/png"] = ".png",
        ["image/webp"] = ".webp",
        ["image/gif"] = ".gif"
    };

    private static string? ExtensionFor(string? contentType) =>
        contentType is not null && AllowedTypes.TryGetValue(contentType, out var extension)
            ? extension
            : null;

    /// <summary>
    /// Deletes a previously stored product photo, ignoring anything that was not
    /// one of ours.
    ///
    /// The check matters: ImageUrl is a database string, and if it ever held an
    /// absolute path or a traversal sequence, blindly combining it with the
    /// folder could delete a file outside uploads. Only names that look like the
    /// GUIDs this controller writes are removed.
    /// </summary>
    private static void DeleteStoredFile(string? imageUrl, string folder)
    {
        if (string.IsNullOrWhiteSpace(imageUrl)) return;

        var prefix = "/uploads/marketplace/";

        if (!imageUrl.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return;

        var name = imageUrl[prefix.Length..];

        if (string.IsNullOrWhiteSpace(name)) return;
        if (name.Contains('/') || name.Contains('\\') || name.Contains("..")) return;

        try
        {
            var fullPath = Path.Combine(folder, name);

            // Fully qualified: ControllerBase also has a File method, so the bare
            // name would bind to that instead of System.IO.File.
            if (System.IO.File.Exists(fullPath)) System.IO.File.Delete(fullPath);
        }
        catch (IOException)
        {
            // A leftover file is untidy, not broken. Never fail the request over it.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}