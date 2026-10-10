using System.ComponentModel.DataAnnotations;

namespace CDM_OneServe_API.DTOs.Guidance;

public class ChatConversationDto
{
    public int Id { get; set; }
    /// <summary>The OTHER user in the conversation.</summary>
    public int PartnerId { get; set; }
    public string PartnerName { get; set; } = "";
    public string? PartnerProfilePicture { get; set; }
    public string? LastMessageBody { get; set; }
    public DateTime? LastMessageAt { get; set; }
    public int UnreadCount { get; set; }
}

public class ChatMessageDto
{
    public long Id { get; set; }
    public int ConversationId { get; set; }
    public int SenderId { get; set; }
    public bool Mine { get; set; }
    public string Body { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? ReadAt { get; set; }
}

public class ChatConversationRef
{
    public int Id { get; set; }
}

public class StartConversationRequest
{
    [Required] public int CounselorId { get; set; }
}

public class SendMessageRequest
{
    [Required, StringLength(4000, MinimumLength = 1)]
    public string Body { get; set; } = "";

    /// <summary>Client-generated id so a retried send is recognised (idempotency).</summary>
    public Guid? ClientId { get; set; }
}
