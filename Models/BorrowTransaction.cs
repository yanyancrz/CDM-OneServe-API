using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CDM_OneServe_API.Models;

[Table("borrowtransactions")]
public class BorrowTransaction
{
    [Key]
    public int BorrowId { get; set; }

    [Required]
    public int UserId { get; set; }

    [Required]
    public int BookId { get; set; }

    public DateTime BorrowDate { get; set; }

    public DateTime DueDate { get; set; }

    public DateTime? ReturnDate { get; set; }

    public int RenewalCount { get; set; }

    [MaxLength(20)]
    public string Status { get; set; } = "Borrowed";

    public decimal Fine { get; set; }

    public string? Remarks { get; set; }

    public DateTime CreatedAt { get; set; }

    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }

    [ForeignKey(nameof(BookId))]
    public Book? Book { get; set; }
}