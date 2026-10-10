using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CDM_OneServe_API.Models.Library;

[Table("libraryterms")]
public class LibraryTerm
{
    [Key]
    public int TermId { get; set; }

    [Required, MaxLength(50)]
    public string TermKey { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string Title { get; set; } = string.Empty;

    [Required]
    public string Content { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    public DateTime UpdatedAt { get; set; }
    public int? UpdatedBy { get; set; }
}