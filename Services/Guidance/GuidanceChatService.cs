using CDM_OneServe_API.Authorization;
using CDM_OneServe_API.Data;
using CDM_OneServe_API.DTOs.Guidance;
using CDM_OneServe_API.Hubs;
using CDM_OneServe_API.Models.Guidance;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Services.Guidance;

/// <summary>
/// Guidance chat. A conversation is always one Student + one Counselor.
/// The signed-in user (JWT) can only ever see their own conversations.
/// </summary>
public class GuidanceChatService
{
    private readonly AppDbContext _context;
    private readonly IHubContext<GuidanceHub> _hub;

    public GuidanceChatService(AppDbContext context, IHubContext<GuidanceHub> hub)
    {
        _context = context;
        _hub = hub;
    }

    // ---------------- conversations ----------------

    public async Task<List<ChatConversationDto>> GetConversationsAsync(
        int userId, bool isCounselor, CancellationToken ct)
    {
        var q = _context.GuidanceChatConversations.AsNoTracking()
            .Include(c => c.Student)
            .Include(c => c.Counselor)
            .Where(c => isCounselor ? c.CounselorId == userId : c.StudentId == userId);

        var convos = await q
            .OrderByDescending(c => c.LastMessageAt ?? c.CreatedAt)
            .ToListAsync(ct);

        var ids = convos.Select(c => c.Id).ToList();

        var lastMessages = await _context.GuidanceChatMessages.AsNoTracking()
            .Where(m => ids.Contains(m.ConversationId))
            .GroupBy(m => m.ConversationId)
            .Select(g => g.OrderByDescending(m => m.Id).First())
            .ToListAsync(ct);

        var unread = await _context.GuidanceChatMessages.AsNoTracking()
            .Where(m => ids.Contains(m.ConversationId) && m.SenderId != userId && m.ReadAt == null)
            .GroupBy(m => m.ConversationId)
            .Select(g => new { ConversationId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ConversationId, x => x.Count, ct);

        return convos.Select(c =>
        {
            var partner = isCounselor ? c.Student : c.Counselor;
            var last = lastMessages.FirstOrDefault(m => m.ConversationId == c.Id);

            return new ChatConversationDto
            {
                Id = c.Id,
                PartnerId = isCounselor ? c.StudentId : c.CounselorId,
                PartnerName = partner?.FullName ?? "Unknown",
                PartnerProfilePicture = partner?.ProfilePicture,
                LastMessageBody = last?.Body,
                LastMessageAt = last?.CreatedAt ?? c.CreatedAt,
                UnreadCount = unread.GetValueOrDefault(c.Id, 0)
            };
        }).ToList();
    }

    public async Task<GuidanceResult<ChatConversationRef>> StartConversationAsync(
        int studentId, int counselorId, CancellationToken ct)
    {
        // The student must have at least one appointment with this counselor.
        var owns = await _context.GuidanceAppointments.AsNoTracking()
            .AnyAsync(a => a.StudentId == studentId && a.CounselorId == counselorId, ct);
        if (!owns)
            return GuidanceResult<ChatConversationRef>.Fail(403, "You can only chat with a counselor you have booked with.");

        var convo = await _context.GuidanceChatConversations
            .FirstOrDefaultAsync(c => c.StudentId == studentId && c.CounselorId == counselorId, ct);

        if (convo == null)
        {
            convo = new GuidanceChatConversation { StudentId = studentId, CounselorId = counselorId };
            _context.GuidanceChatConversations.Add(convo);
            await _context.SaveChangesAsync(ct);
        }

        return GuidanceResult<ChatConversationRef>.Ok(new ChatConversationRef { Id = convo.Id }, "Conversation ready.");
    }

    // ---------------- messages ----------------

    public async Task<List<ChatMessageDto>?> GetMessagesAsync(
        int userId, bool isCounselor, int conversationId, int? take, CancellationToken ct)
    {
        var convo = await _context.GuidanceChatConversations.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == conversationId
                && (isCounselor ? c.CounselorId == userId : c.StudentId == userId), ct);
        if (convo == null) return null;

        var rows = await _context.GuidanceChatMessages.AsNoTracking()
            .Where(m => m.ConversationId == conversationId)
            .OrderByDescending(m => m.Id)
            .Take(take ?? 50)
            .ToListAsync(ct);

        // Mark the other side's messages as read.
        var unread = await _context.GuidanceChatMessages
            .Where(m => m.ConversationId == conversationId && m.SenderId != userId && m.ReadAt == null)
            .ToListAsync(ct);
        if (unread.Count > 0)
        {
            foreach (var m in unread) m.ReadAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(ct);
        }

        rows.Reverse();
        return rows.Select(m => ToDto(m, userId)).ToList();
    }

    public async Task<GuidanceResult<ChatMessageDto>> SendMessageAsync(
        int userId, bool isCounselor, int conversationId, SendMessageRequest req, CancellationToken ct)
    {
        var convo = await _context.GuidanceChatConversations
            .FirstOrDefaultAsync(c => c.Id == conversationId
                && (isCounselor ? c.CounselorId == userId : c.StudentId == userId), ct);
        if (convo == null)
            return GuidanceResult<ChatMessageDto>.Fail(404, "Conversation not found.");

        // Idempotency: a retried send with the same ClientId returns the original.
        if (req.ClientId.HasValue)
        {
            var existing = await _context.GuidanceChatMessages.AsNoTracking()
                .FirstOrDefaultAsync(m => m.ConversationId == conversationId
                    && m.SenderId == userId && m.ClientId == req.ClientId, ct);
            if (existing != null)
                return GuidanceResult<ChatMessageDto>.Ok(ToDto(existing, userId), "Message already sent.");
        }

        var message = new GuidanceChatMessage
        {
            ConversationId = conversationId,
            SenderId = userId,
            Body = req.Body.Trim(),
            ClientId = req.ClientId
        };
        _context.GuidanceChatMessages.Add(message);

        convo.LastMessageAt = message.CreatedAt;
        await _context.SaveChangesAsync(ct);

        // Real-time: push to the conversation group and to the recipient's personal group.
        // Neutral payload: the client decides if the message is "mine" via senderId.
        var recipientId = isCounselor ? convo.StudentId : convo.CounselorId;
        var realtimePayload = new
        {
            conversationId,
            id = message.Id,
            senderId = message.SenderId,
            body = message.Body,
            createdAt = message.CreatedAt
        };

        try
        {
            await _hub.Clients.Group($"user:{recipientId}").SendAsync("ChatMessage", realtimePayload, ct);
            await _hub.Clients.Group($"convo:{conversationId}").SendAsync("ChatMessage", realtimePayload, ct);
        }
        catch
        {
            // Real-time must never fail the send itself.
        }

        return GuidanceResult<ChatMessageDto>.Created(ToDto(message, userId), "Message sent.");
    }

    public async Task<bool> MarkReadAsync(int userId, bool isCounselor, int conversationId, CancellationToken ct)
    {
        var convo = await _context.GuidanceChatConversations.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == conversationId
                && (isCounselor ? c.CounselorId == userId : c.StudentId == userId), ct);
        if (convo == null) return false;

        var unread = await _context.GuidanceChatMessages
            .Where(m => m.ConversationId == conversationId && m.SenderId != userId && m.ReadAt == null)
            .ToListAsync(ct);
        if (unread.Count > 0)
        {
            foreach (var m in unread) m.ReadAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(ct);
        }
        return true;
    }

    private static ChatMessageDto ToDto(GuidanceChatMessage m, int userId) => new()
    {
        Id = m.Id,
        ConversationId = m.ConversationId,
        SenderId = m.SenderId,
        Mine = m.SenderId == userId,
        Body = m.Body,
        CreatedAt = m.CreatedAt,
        ReadAt = m.ReadAt
    };
}
