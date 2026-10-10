using CDM_OneServe_API.Data;
using CDM_OneServe_API.DTOs.Marketplace;
using CDM_OneServe_API.Models;
using CDM_OneServe_API.Models.Marketplace;

using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Services.Marketplace;

/// <summary>
/// Chat / Concerns.
///
/// A Student or Faculty member opens one thread and can link it to an order.
/// Because there is exactly ONE Marketplace Staff account, every conversation
/// lands in a single unified inbox that staff works from.
///
/// The CDM OneServe Admin is NOT staff: they have no read access to messages and
/// no way to reply as Marketplace Staff. That separation is enforced here - the
/// staff check goes through MarketplaceAccessService.GetStaffAsync, which only
/// accepts a row in marketplace_staff.
/// </summary>
public class MarketplaceChatService
{
    private readonly AppDbContext _db;
    private readonly MarketplaceAccessService _access;

    public MarketplaceChatService(AppDbContext db, MarketplaceAccessService access)
    {
        _db = db;
        _access = access;
    }

    // ==========================================================
    // buyer side
    // ==========================================================

    /// <summary>
    /// The buyer's own thread. One per user - asking again reopens the same
    /// thread rather than fragmenting the history.
    /// </summary>
    public async Task<MarketplaceResult<MarketplaceConversationDto>> GetMyConversationAsync(
        int userId,
        CancellationToken ct = default)
    {
        var buyer = await _access.RequireBuyerAsync(userId, ct);
        if (buyer.Actor is null)
            return MarketplaceAccessService.BuyerRefusal<MarketplaceConversationDto>(buyer);

        var conversation = await _db.MarketplaceConversations.AsNoTracking()
            .Where(c => c.UserId == userId)
            .FirstOrDefaultAsync(ct);

        if (conversation is null)
            return MarketplaceResult<MarketplaceConversationDto>.Ok(new MarketplaceConversationDto
            {
                UserId = userId,
                UserName = buyer.Actor.User.FullName,
                AccountType = buyer.Actor.User.Role,
                Status = MarketplaceChatStatus.Open
            });

        await MarkReadAsync(conversation.ConversationId, MarketplaceSender.User, ct);

        return MarketplaceResult<MarketplaceConversationDto>.Ok(await ReadAsync(conversation.ConversationId, ct));
    }

    /// <summary>
    /// Opens (or reopens) the buyer's thread and posts the first message.
    /// </summary>
    public async Task<MarketplaceResult<MarketplaceConversationDto>> StartConversationAsync(
        int userId,
        StartConversationRequest request,
        CancellationToken ct = default)
    {
        var buyer = await _access.RequireBuyerAsync(userId, ct);
        if (buyer.Actor is null)
            return MarketplaceAccessService.BuyerRefusal<MarketplaceConversationDto>(buyer);

        var text = (request.Message ?? "").Trim();
        if (text.Length < 1)
            return MarketplaceResult<MarketplaceConversationDto>.BadRequest("Write your message first.");

        // An order may only be linked if it is actually the buyer's.
        if (request.OrderId is int orderId &&
            !await _access.OwnsOrderAsync(userId, orderId, ct))
        {
            return MarketplaceResult<MarketplaceConversationDto>.BadRequest("That order is not yours.");
        }

        var conversation = await _db.MarketplaceConversations
            .FirstOrDefaultAsync(c => c.UserId == userId, ct);

        if (conversation is null)
        {
            conversation = new MarketplaceConversation
            {
                UserId = userId,
                OrderId = request.OrderId,
                Subject = (request.Subject ?? "").Trim(),
                Status = MarketplaceChatStatus.Open,
                CreatedAt = DateTime.UtcNow
            };

            _db.MarketplaceConversations.Add(conversation);
        }
        else
        {
            // Reopening a resolved thread; keep the original subject but adopt
            // the new order if one was mentioned.
            conversation.Status = MarketplaceChatStatus.Open;
            conversation.UpdatedAt = DateTime.UtcNow;

            if (request.OrderId is int newOrder)
                conversation.OrderId = newOrder;

            var subject = (request.Subject ?? "").Trim();

            if (subject.Length > 0)
                conversation.Subject = subject;
        }

        // Save the conversation FIRST so it has a key. Setting the navigation
        // (Conversation = conversation) on a brand-new, unsaved row leaves EF
        // unable to resolve the foreign key, so it invents a shadow column that
        // does not exist in the table. The explicit ConversationId has no such
        // ambiguity.
        await _db.SaveChangesAsync(ct);

        _db.MarketplaceMessages.Add(new MarketplaceMessage
        {
            ConversationId = conversation.ConversationId,
            SenderType = MarketplaceSender.User,
            SenderId = userId,
            Message = text
        });

        await _db.SaveChangesAsync(ct);

        return MarketplaceResult<MarketplaceConversationDto>.Ok(
            await ReadAsync(conversation.ConversationId, ct),
            "Message sent to Marketplace Staff.");
    }

    /// <summary>Posts into the buyer's thread. Ownership is checked on the row.</summary>
    public async Task<MarketplaceResult<MarketplaceConversationDto>> SendMessageAsync(
        int userId,
        int conversationId,
        PostMessageRequest request,
        CancellationToken ct = default)
    {
        var buyer = await _access.RequireBuyerAsync(userId, ct);
        if (buyer.Actor is null) return MarketplaceAccessService.BuyerRefusal<MarketplaceConversationDto>(buyer);

        if (!await _access.OwnsConversationAsync(userId, conversationId, ct))
            return MarketplaceResult<MarketplaceConversationDto>.NotFound("Conversation not found.");

        var text = (request.Message ?? "").Trim();
        if (text.Length < 1)
            return MarketplaceResult<MarketplaceConversationDto>.BadRequest("Write your message first.");

        var conversation = await _db.MarketplaceConversations
            .FirstAsync(c => c.ConversationId == conversationId, ct);

        _db.MarketplaceMessages.Add(new MarketplaceMessage
        {
            ConversationId = conversationId,
            SenderType = MarketplaceSender.User,
            SenderId = userId,
            Message = text
        });

        conversation.Status = MarketplaceChatStatus.Open;
        conversation.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        return MarketplaceResult<MarketplaceConversationDto>.Ok(
            await ReadAsync(conversationId, ct));
    }

    // ==========================================================
    // staff side
    // ==========================================================

    /// <summary>
    /// The unified staff inbox - every conversation in one list, because there is
    /// only one staff account to read them all.
    /// </summary>
    public async Task<MarketplaceResult<List<MarketplaceConversationDto>>> ListForStaffAsync(
        int staffId,
        string? status,
        CancellationToken ct = default)
    {
        var actor = await _access.GetOperatorAsync(staffId, ct);
        if (actor is null) return MarketplaceResult<List<MarketplaceConversationDto>>.Forbidden(StaffOnly);

        var conversations = await _db.MarketplaceConversations.AsNoTracking()
            .Include(c => c.Order)
            .OrderByDescending(c => c.UpdatedAt ?? c.CreatedAt)
            .ToListAsync(ct);

        var ids = conversations.Select(c => c.ConversationId).ToList();

        var messages = await _db.MarketplaceMessages.AsNoTracking()
            .Where(m => ids.Contains(m.ConversationId))
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(ct);

        var users = await _db.Users.AsNoTracking()
            .Where(u => conversations.Select(c => c.UserId).Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, ct);

        var rows = conversations.Select(c =>
        {
            var mine = messages.Where(m => m.ConversationId == c.ConversationId).ToList();
            var last = mine.LastOrDefault();

            return new MarketplaceConversationDto
            {
                ConversationId = c.ConversationId,
                UserId = c.UserId,
                UserName = users.GetValueOrDefault(c.UserId)?.FullName ?? "",
                AccountType = users.GetValueOrDefault(c.UserId)?.Role ?? "",
                OrderId = c.OrderId,
                OrderReference = c.Order?.OrderReference,
                Status = c.Status,
                Subject = c.Subject,
                CreatedAt = c.CreatedAt,
                UpdatedAt = c.UpdatedAt,
                // Unread FROM THE BUYER - this is the staff unread badge.
                UnreadCount = mine.Count(m => m.SenderType == MarketplaceSender.User && !m.IsRead),
                LastMessage = last?.Message ?? "",
                LastMessageAt = last?.CreatedAt
            };
        }).ToList();

        if (!string.IsNullOrWhiteSpace(status))
        {
            var wanted = status.Trim().ToLowerInvariant();
            rows = rows.Where(r => r.Status == wanted).ToList();
        }

        return MarketplaceResult<List<MarketplaceConversationDto>>.Ok(rows);
    }

    /// <summary>One thread with its full message history.</summary>
    public async Task<MarketplaceResult<MarketplaceConversationDto>> GetForStaffAsync(
        int staffId,
        int conversationId,
        CancellationToken ct = default)
    {
        var actor = await _access.GetOperatorAsync(staffId, ct);
        if (actor is null) return MarketplaceResult<MarketplaceConversationDto>.Forbidden(StaffOnly);

        // Opening it clears the staff-side unread badge.
        await MarkReadAsync(conversationId, MarketplaceSender.User, ct);

        return MarketplaceResult<MarketplaceConversationDto>.Ok(await ReadAsync(conversationId, ct));
    }

    /// <summary>Staff replies. SenderType is hard-coded to `staff`.</summary>
    public async Task<MarketplaceResult<MarketplaceConversationDto>> ReplyAsync(
        int staffId,
        int conversationId,
        PostMessageRequest request,
        CancellationToken ct = default)
    {
        var actor = await _access.GetOperatorAsync(staffId, ct);
        if (actor is null) return MarketplaceResult<MarketplaceConversationDto>.Forbidden(StaffOnly);

        var text = (request.Message ?? "").Trim();
        if (text.Length < 1)
            return MarketplaceResult<MarketplaceConversationDto>.BadRequest("Write your reply first.");

        var conversation = await _db.MarketplaceConversations
            .FirstOrDefaultAsync(c => c.ConversationId == conversationId, ct);

        if (conversation is null)
            return MarketplaceResult<MarketplaceConversationDto>.NotFound("Conversation not found.");

        _db.MarketplaceMessages.Add(new MarketplaceMessage
        {
            ConversationId = conversationId,
            // Hard-coded. The OneServe Admin can never reach this path.
            SenderType = MarketplaceSender.Staff,
            SenderId = staffId,
            Message = text
        });

        conversation.Status = MarketplaceChatStatus.InProgress;
        conversation.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        return MarketplaceResult<MarketplaceConversationDto>.Ok(
            await ReadAsync(conversationId, ct));
    }

    public async Task<MarketplaceResult<object>> UpdateStatusAsync(
        int staffId,
        int conversationId,
        string status,
        CancellationToken ct = default)
    {
        var actor = await _access.GetOperatorAsync(staffId, ct);
        if (actor is null) return MarketplaceResult<object>.Forbidden(StaffOnly);

        var wanted = (status ?? "").Trim().ToLowerInvariant();
        if (!MarketplaceChatStatus.All.Contains(wanted))
            return MarketplaceResult<object>.BadRequest("Choose a valid conversation status.");

        var conversation = await _db.MarketplaceConversations
            .FirstOrDefaultAsync(c => c.ConversationId == conversationId, ct);

        if (conversation is null)
            return MarketplaceResult<object>.NotFound("Conversation not found.");

        conversation.Status = wanted;
        conversation.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        return MarketplaceResult<object>.Ok(new { }, $"Conversation marked {wanted}.");
    }

    // ==========================================================
    // helpers
    // ==========================================================

    /// <summary>
    /// Marks messages from <paramref name="fromSender"/> as read for the other
    /// side. Called when a buyer opens their thread (clears the staff badge) and
    /// when staff opens it (clears the buyer badge).
    /// </summary>
    private async Task MarkReadAsync(int conversationId, string fromSender, CancellationToken ct)
    {
        await _db.MarketplaceMessages
            .Where(m => m.ConversationId == conversationId &&
                        m.SenderType == fromSender &&
                        !m.IsRead)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.IsRead, true), ct);
    }

    private async Task<MarketplaceConversationDto> ReadAsync(int conversationId, CancellationToken ct)
    {
        var conversation = await _db.MarketplaceConversations.AsNoTracking()
            .Where(c => c.ConversationId == conversationId)
            .Include(c => c.Order)
            .FirstOrDefaultAsync(ct);

        if (conversation is null) return new MarketplaceConversationDto();

        var messages = await _db.MarketplaceMessages.AsNoTracking()
            .Where(m => m.ConversationId == conversationId)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(ct);

        var user = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == conversation.UserId, ct);

        return new MarketplaceConversationDto
        {
            ConversationId = conversation.ConversationId,
            UserId = conversation.UserId,
            UserName = user?.FullName ?? "",
            AccountType = user?.Role ?? "",
            OrderId = conversation.OrderId,
            OrderReference = conversation.Order?.OrderReference,
            Status = conversation.Status,
            Subject = conversation.Subject,
            CreatedAt = conversation.CreatedAt,
            UpdatedAt = conversation.UpdatedAt,
            UnreadCount = messages.Count(m => m.SenderType == MarketplaceSender.User && !m.IsRead),
            LastMessage = messages.LastOrDefault()?.Message ?? "",
            LastMessageAt = messages.LastOrDefault()?.CreatedAt,
            Messages = messages.Select(m => new MarketplaceMessageDto
            {
                MessageId = m.MessageId,
                SenderType = m.SenderType,
                SenderId = m.SenderId,
                Message = m.Message,
                IsRead = m.IsRead,
                CreatedAt = m.CreatedAt
            }).ToList()
        };
    }

    private const string StaffOnly = "Marketplace Staff access required.";
}
