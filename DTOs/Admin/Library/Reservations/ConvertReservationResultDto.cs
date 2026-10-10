namespace CDM_OneServe_API.DTOs.Admin.Library.Reservations;

public class ConvertReservationResultDto
{
    public int TransactionId { get; set; }
    public DateTime BorrowDate { get; set; }
    public DateTime DueDate { get; set; }
    public AdminReservationDto Reservation { get; set; } = new();
}