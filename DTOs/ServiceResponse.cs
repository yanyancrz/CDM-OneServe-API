namespace CDM_OneServe_API.DTOs;

public class ServiceResponse<T>
{
    public bool Success { get; set; }

    public string Message { get; set; } = string.Empty;

    public T? Data { get; set; }
}