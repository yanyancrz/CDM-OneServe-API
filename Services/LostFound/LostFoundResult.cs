namespace CDM_OneServe_API.Services.LostFound;

/// <summary>Result envelope for every lost-and-found service call.</summary>
public sealed class LostFoundResult<T>
{
    private LostFoundResult(bool success, int status, T? data, string? message)
    {
        Success = success;
        Status = status;
        Data = data;
        Message = message;
    }

    public bool Success { get; }
    public int Status { get; }
    public T? Data { get; }
    public string? Message { get; }

    public static LostFoundResult<T> Ok(T data, string? message = null) =>
        new(true, 200, data, message);

    public static LostFoundResult<T> Created(T data, string? message = null) =>
        new(true, 201, data, message);

    public static LostFoundResult<T> BadRequest(string message) =>
        new(false, 400, default, message);

    public static LostFoundResult<T> Forbidden(string message) =>
        new(false, 403, default, message);

    public static LostFoundResult<T> NotFound(string message) =>
        new(false, 404, default, message);

    public static LostFoundResult<T> Conflict(string message) =>
        new(false, 409, default, message);
}

/// <summary>Non-generic twin for actions that return no payload.</summary>
public sealed class LostFoundResult
{
    private LostFoundResult(bool success, int status, string? message)
    {
        Success = success;
        Status = status;
        Message = message;
    }

    public bool Success { get; }

    public int Status { get; }

    public string? Message { get; }

    public static LostFoundResult Ok(string? message = null) =>
        new(true, 200, message);

    public static LostFoundResult Created(string? message = null) =>
        new(true, 201, message);

    public static LostFoundResult BadRequest(string message) =>
        new(false, 400, message);

    public static LostFoundResult Forbidden(string message) =>
        new(false, 403, message);

    public static LostFoundResult NotFound(string message) =>
        new(false, 404, message);

    public static LostFoundResult Conflict(string message) =>
        new(false, 409, message);
}
