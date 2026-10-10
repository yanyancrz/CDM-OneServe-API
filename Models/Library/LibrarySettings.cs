using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CDM_OneServe_API.Models.Library;

// One row. Edited only by the Library Head.
[Table("librarysettings")]
public class LibrarySettings
{
    [Key]
    public int SettingId { get; set; }

    public decimal OverdueFinePerDay { get; set; } = 10m;
    public int StudentBorrowLimit { get; set; } = 3;
    public int FacultyBorrowLimit { get; set; } = 5;
    public int DefaultLoanDays { get; set; } = 7;
    public int ReservationPickupDays { get; set; } = 3;

    public DateTime UpdatedAt { get; set; }
    public int? UpdatedBy { get; set; }
}