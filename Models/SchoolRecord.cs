namespace CDM_OneServe_API.Models;

// =========================================================
// SCHOOL RECORD
// School records maintained by administrators. Public registration
// accepts IDs without a roster entry. The legacy IsUsed field remains
// for existing school records and administrative workflows.
// =========================================================
public class SchoolRecord
{
    public int Id { get; set; }

    public string IdNumber { get; set; } = "";

    // "Student" or "Faculty"
    public string Role { get; set; } = "Student";

    // True once someone registered with this ID.
    public bool IsUsed { get; set; } = false;

    public DateTime CreatedAt { get; set; }
        = DateTime.Now;
}
