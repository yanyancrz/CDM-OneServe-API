namespace CDM_OneServe_API.DTOs
{
    public class LibraryActivityDto
    {
        public string Type { get; set; } = "";

        public string Title { get; set; } = "";

        public string Description { get; set; } = "";

        public DateTime Date { get; set; }

        public string? CoverImage { get; set; }

        public int? BorrowId { get; set; }

        public int? BookId { get; set; }

        public int ActivityId { get; set; }
    }
}