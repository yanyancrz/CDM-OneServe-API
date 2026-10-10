using System.Text.Json.Serialization;

namespace CDM_OneServe_API.DTOs.Admin.Library.Common;

/// <summary>
/// Consistent response envelope for every Library admin endpoint.
///
/// Success:    { "success": true,  "data": {...}, "message": "..." }
/// Error:      { "success": false, "message": "..." }
/// Validation: { "success": false, "message": "...", "errors": { "Field": ["..."] } }
/// </summary>
public class ApiResponse<T>
{
    public bool Success { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public T? Data { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Message { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string[]>? Errors { get; set; }
}

public static class ApiResponse
{
    public static ApiResponse<T> Ok<T>(T data, string? message = null) => new()
    {
        Success = true,
        Data = data,
        Message = message
    };

    public static ApiResponse<object> Ok(string? message = null) => new()
    {
        Success = true,
        Message = message
    };

    public static ApiResponse<object> Fail(
        string message,
        Dictionary<string, string[]>? errors = null) => new()
        {
            Success = false,
            Message = message,
            Errors = errors
        };
}