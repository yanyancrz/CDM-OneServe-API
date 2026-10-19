namespace CDM_OneServe_API.DTOs.Admin.Library.Terms;

public class LibraryTermDto
{
    public int TermId { get; set; }
    public string TermKey { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class UpdateTermsDto
{
    public List<UpdateTermItemDto> Sections { get; set; } = new();
}

public class UpdateTermItemDto
{
    public string TermKey { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}