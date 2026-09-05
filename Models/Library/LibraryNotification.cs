namespace CDM_OneServe_API.Models.Library
{
    public class LibraryNotification
    {
        public int NotificationId { get; set; }

        public int UserId { get; set; }

        public string Type { get; set; } = "general";

        public string Title { get; set; } = "";

        public string Message { get; set; } = "";

        public bool IsRead { get; set; }

        public DateTime CreatedAt { get; set; }
    }
}