namespace CDM_OneServe_API.DTOs.Library
{
    public class LibraryNotificationDto
    {
        public int Id { get; set; }

        public string Type { get; set; } = "general";

        public string Title { get; set; } = "";

        public string Description { get; set; } = "";

        public DateTime CreatedAt { get; set; }

        public bool Read { get; set; }
    }
}