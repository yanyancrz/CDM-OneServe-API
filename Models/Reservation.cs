using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CDM_OneServe_API.Models
{
    [Table("reservations")]
    public class Reservation
    {
        [Key]
        public int ReservationId { get; set; }

        public int UserId { get; set; }

        public int BookId { get; set; }

        public DateTime ReservationAt { get; set; }

        public DateTime ExpirationDate { get; set; }

        public string Status { get; set; } = string.Empty;

        public string? Remarks { get; set; }

        public DateTime CreatedAt { get; set; }

        public User? User { get; set; }

        public Book? Book { get; set; }
    }
}