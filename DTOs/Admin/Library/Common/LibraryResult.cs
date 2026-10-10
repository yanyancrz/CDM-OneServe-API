namespace CDM_OneServe_API.DTOs.Admin.Library.Common;

// Lets services report success or a specific HTTP failure without throwing.
public class LibraryResult<T>
{
    public T? Data { get; init; }
    public int StatusCode { get; init; } = 200;
    public string? Error { get; init; }

    public static LibraryResult<T> Success(T data, int statusCode = 200) =>
        new() { Data = data, StatusCode = statusCode };

    public static LibraryResult<T> Failure(int statusCode, string error) =>
        new() { StatusCode = statusCode, Error = error };
}