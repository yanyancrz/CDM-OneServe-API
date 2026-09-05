namespace CDM_OneServe_API.Models.Library
{
    public class LibraryActivity
    {
        public int ActivityId { get; set; }

        public int UserId { get; set; }

        public int? BookId { get; set; }

        public string ActivityType { get; set; } = "";

        public string Title { get; set; } = "";

        public string? Description { get; set; }

        public DateTime CreatedAt { get; set; }

        public User? User { get; set; }

        public Book? Book { get; set; }
    }
}