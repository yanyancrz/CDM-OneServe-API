namespace CDM_OneServe_API.Models;

// =========================================================
// SCHOOL RECORD
// The official list of ID Numbers allowed to register.
// Registration is rejected unless the submitted ID Number
// exists in this table. IDs live ONLY here — never in code.
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
