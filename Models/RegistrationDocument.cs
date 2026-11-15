using System.Text.Json.Serialization;

namespace CDM_OneServe_API.Models;

public class RegistrationDocument
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    [JsonIgnore]
    public byte[] Content { get; set; } = Array.Empty<byte>();
    public DateTime UploadedAtUtc { get; set; } = DateTime.UtcNow;
    [JsonIgnore]
    public User User { get; set; } = null!;
}
