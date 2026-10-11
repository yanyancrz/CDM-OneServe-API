using CDM_OneServe_API.Models.LostFound;

namespace CDM_OneServe_API.Services.LostFound;

/// <summary>
/// Where proof-of-ownership files live and who may read them.
///
/// They are stored OUTSIDE the served web root (App_Data/claim-proofs):
/// a public item listing can never leak a private attachment, because
/// the only way to read a file is through the authorized endpoint that
/// goes through this class.
/// </summary>
public interface IClaimProofStorage
{
    /// <summary>True when the content type is allowed (image or PDF).</summary>
    bool IsAllowed(string contentType);

    /// <summary>Max allowed size in bytes.</summary>
    long MaxBytes { get; }

    Task<string> SaveAsync(int userId, Stream content, string extension, CancellationToken ct);

    /// <summary>Opens a stored file for reading, or null if it is gone.</summary>
    Task<ClaimProofFile?> OpenAsync(string storedName, CancellationToken ct);

    Task DeleteAsync(string storedName, CancellationToken ct);
}

/// <summary>A proof file opened for an authorized reader.</summary>
public sealed record ClaimProofFile(Stream Content, string ContentType, string FileName);

public class ClaimProofStorage : IClaimProofStorage
{
    /// <summary>5 MB - the same ceiling the report photos already use.</summary>
    public const long MaxProofBytes = 5 * 1024 * 1024;

    private static readonly string[] AllowedTypes =
        { "image/jpeg", "image/png", "image/webp", "application/pdf" };

    private readonly string _directory;

    public ClaimProofStorage(IWebHostEnvironment environment)
    {
        // App_Data is not served by the static-file middleware, so a file
        // in here is unreachable without going through the API.
        _directory = Path.Combine(environment.ContentRootPath, "App_Data", "claim-proofs");
    }

    public long MaxBytes => MaxProofBytes;

    public bool IsAllowed(string contentType) =>
        AllowedTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase);

    public async Task<string> SaveAsync(int userId, Stream content, string extension, CancellationToken ct)
    {
        Directory.CreateDirectory(_directory);

        var storedName = $"{userId}_{Guid.NewGuid():N}{extension}";
        var fullPath = Path.Combine(_directory, storedName);

        await using (var target = new FileStream(fullPath, FileMode.CreateNew))
        {
            await content.CopyToAsync(target, ct);
        }

        // Only the generated name is stored - never the absolute path and
        // never the original file name - so a database dump cannot reveal
        // the server's layout.
        return storedName;
    }

    public async Task<ClaimProofFile?> OpenAsync(string storedName, CancellationToken ct)
    {
        // Defensive: a stored name is always ours, but never trust one
        // that could point outside the directory.
        if (string.IsNullOrWhiteSpace(storedName) ||
            storedName.Contains('/') || storedName.Contains('\\') ||
            storedName.Contains(".."))
            return null;

        var fullPath = Path.Combine(_directory, storedName);
        if (!System.IO.File.Exists(fullPath)) return null;

        var info = new FileInfo(fullPath);
        var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read);

        return await Task.FromResult(
            new ClaimProofFile(stream, GuessContentType(storedName), storedName));
    }

    public Task DeleteAsync(string storedName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(storedName) ||
            storedName.Contains('/') || storedName.Contains('\\') ||
            storedName.Contains(".."))
            return Task.CompletedTask;

        var fullPath = Path.Combine(_directory, storedName);
        try
        {
            if (System.IO.File.Exists(fullPath)) System.IO.File.Delete(fullPath);
        }
        catch
        {
            // A missing or locked file must never fail the caller's action.
        }

        return Task.CompletedTask;
    }

    private static string GuessContentType(string storedName)
    {
        var extension = Path.GetExtension(storedName).ToLowerInvariant();
        return extension switch
        {
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => "image/jpeg",
        };
    }
}
