namespace CDM_OneServe_API.DTOs
{
    public class ReservationDto
    {
        public int ReservationId { get; set; }

        public int UserId { get; set; }

        public int BookId { get; set; }

        public string BookTitle { get; set; } = string.Empty;

        public string Author { get; set; } = string.Empty;

        public string? CoverImage { get; set; }

        public DateTime ReservationAt { get; set; }

        public DateTime ExpirationDate { get; set; }

        public string Status { get; set; } = string.Empty;

        public string? Remarks { get; set; }
    }
}