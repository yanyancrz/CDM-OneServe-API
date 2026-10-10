namespace CDM_OneServe_API.DTOs.Admin.Library.Students;

public class StudentReservationDto
{
    public int ReservationId { get; set; }
    public string BookTitle { get; set; } = string.Empty;
    public DateTime ReservationAt { get; set; }
    public DateTime ExpirationDate { get; set; }
    public string Status { get; set; } = string.Empty;
}