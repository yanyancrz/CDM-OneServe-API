namespace CDM_OneServe_API.Services.Marketplace;

/// <summary>
/// Result envelope for every marketplace service call.
///
/// Services never throw for expected outcomes - a bad request, a missing row or
/// a refused action comes back as a failed <see cref="MarketplaceResult{T}"/>
/// carrying the HTTP status and a message meant for the buyer.
/// </summary>
public sealed class MarketplaceResult<T>
{
    private MarketplaceResult(bool success, int status, T? data, string? message)
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

    public static MarketplaceResult<T> Ok(T data, string? message = null) =>
        new(true, 200, data, message);

    public static MarketplaceResult<T> Created(T data, string? message = null) =>
        new(true, 201, data, message);

    public static MarketplaceResult<T> BadRequest(string message) =>
        new(false, 400, default, message);

    public static MarketplaceResult<T> Unauthorized(string message) =>
        new(false, 401, default, message);

    public static MarketplaceResult<T> Forbidden(string message) =>
        new(false, 403, default, message);

    public static MarketplaceResult<T> NotFound(string message) =>
        new(false, 404, default, message);

    public static MarketplaceResult<T> Conflict(string message) =>
        new(false, 409, default, message);
}

/// <summary>The caller, plus why they were refused when they were.</summary>
public sealed class MarketplaceAuth
{
    public MarketplaceAuth(MarketplaceActor? actor, string? error = null)
    {
        Actor = actor;
        Error = error;
    }

    public MarketplaceActor? Actor { get; }
    public string? Error { get; }
    public bool Success => Actor is not null;
}