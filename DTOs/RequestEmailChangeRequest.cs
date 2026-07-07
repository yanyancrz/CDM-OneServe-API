namespace CDM_OneServe_API.DTOs;

public class RequestEmailChangeRequest
{
    public int UserId { get; set; }

    public string NewEmail { get; set; } = "";
}