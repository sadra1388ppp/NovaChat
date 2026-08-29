using Microsoft.EntityFrameworkCore;
using NovaChat.Server.Data;
using NovaChat.Server.Entities;

namespace NovaChat.Server.Services;

public class ChatService
{
    private readonly AppDbContext _context;

    public ChatService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<Chat?> CreatePrivateChatAsync(string currentUserId, string otherUserId)
    {
        currentUserId = currentUserId?.Trim() ?? string.Empty;
        otherUserId = otherUserId?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(currentUserId) || string.IsNullOrWhiteSpace(otherUserId))
            return null;

        if (string.Equals(currentUserId, otherUserId, StringComparison.OrdinalIgnoreCase))
            return null;

        var currentUser = await _context.Users
            .FirstOrDefaultAsync(u => u.Id.ToLower() == currentUserId.ToLower());

        var otherUser = await _context.Users
            .FirstOrDefaultAsync(u => u.Id.ToLower() == otherUserId.ToLower());

        if (currentUser == null || otherUser == null)
            return null;

        var existingChat = await _context.Chats
            .Include(c => c.User1)
            .Include(c => c.User2)
            .FirstOrDefaultAsync(c =>
                (c.User1Id.ToLower() == currentUser.Id.ToLower() && c.User2Id.ToLower() == otherUser.Id.ToLower()) ||
                (c.User1Id.ToLower() == otherUser.Id.ToLower() && c.User2Id.ToLower() == currentUser.Id.ToLower()));

        if (existingChat != null)
            return existingChat;

        var chat = new Chat
        {
            User1Id = currentUser.Id,
            User2Id = otherUser.Id,
            CreatedAt = DateTime.UtcNow
        };

        _context.Chats.Add(chat);
        await _context.SaveChangesAsync();

        chat.User1 = currentUser;
        chat.User2 = otherUser;
        return chat;
    }

    public async Task<List<Chat>> GetUserChatsAsync(string userId)
    {
        return await _context.Chats
            .Include(c => c.User1)
            .Include(c => c.User2)
            .Where(c => c.User1Id == userId || c.User2Id == userId)
            .OrderByDescending(c =>
                c.Messages
                    .OrderByDescending(m => m.SentAt)
                    .ThenByDescending(m => m.Id)
                    .Select(m => (DateTime?)m.SentAt)
                    .FirstOrDefault() ?? c.CreatedAt)
            .ToListAsync();
    }

    public async Task<List<Chat>> GetAllChatsAsync()
    {
        return await _context.Chats
            .Include(c => c.User1)
            .Include(c => c.User2)
            .OrderByDescending(c =>
                c.Messages
                    .OrderByDescending(m => m.SentAt)
                    .ThenByDescending(m => m.Id)
                    .Select(m => (DateTime?)m.SentAt)
                    .FirstOrDefault() ?? c.CreatedAt)
            .ToListAsync();
    }

    public async Task<Chat?> GetChatByIdAsync(int chatId)
    {
        return await _context.Chats
            .Include(c => c.User1)
            .Include(c => c.User2)
            .FirstOrDefaultAsync(c => c.Id == chatId);
    }

    public async Task<bool> CanAccessChatAsync(int chatId, string userId)
    {
        return await _context.Chats.AnyAsync(c =>
            c.Id == chatId &&
            (c.User1Id == userId || c.User2Id == userId));
    }

    public async Task<Message?> SendMessageAsync(int chatId, string senderId, string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return null;

        var chat = await _context.Chats.FirstOrDefaultAsync(c => c.Id == chatId);
        if (chat == null)
            return null;

        if (chat.User1Id != senderId && chat.User2Id != senderId)
            return null;

        var message = new Message
        {
            ChatId = chatId,
            SenderId = senderId,
            Content = content.Trim(),
            SentAt = DateTime.UtcNow
        };

        _context.Messages.Add(message);
        await _context.SaveChangesAsync();

        await _context.Entry(message).Reference(m => m.Sender).LoadAsync();
        return message;
    }

    public async Task<List<Message>> GetMessagesAsync(int chatId, int? beforeMessageId = null, int pageSize = 50)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _context.Messages
            .AsNoTracking()
            .Include(m => m.Sender)
            .Where(m => m.ChatId == chatId);

        if (beforeMessageId.HasValue)
        {
            var beforeMessage = await _context.Messages
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == beforeMessageId.Value && m.ChatId == chatId);

            if (beforeMessage != null)
            {
                query = query.Where(m =>
                    m.SentAt < beforeMessage.SentAt ||
                    (m.SentAt == beforeMessage.SentAt && m.Id < beforeMessage.Id));
            }
        }

        var messages = await query
            .OrderByDescending(m => m.SentAt)
            .ThenByDescending(m => m.Id)
            .Take(pageSize + 1)
            .ToListAsync();

        if (messages.Count > pageSize)
            messages.RemoveAt(messages.Count - 1);

        return messages.OrderBy(m => m.SentAt).ThenBy(m => m.Id).ToList();
    }

    public async Task<bool> HasOlderMessagesAsync(int chatId, int firstMessageId)
    {
        var firstMessage = await _context.Messages
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == firstMessageId && m.ChatId == chatId);

        if (firstMessage == null)
            return false;

        return await _context.Messages
            .AsNoTracking()
            .AnyAsync(m =>
                m.ChatId == chatId &&
                (m.SentAt < firstMessage.SentAt ||
                 (m.SentAt == firstMessage.SentAt && m.Id < firstMessage.Id)));
    }

    public async Task<Message?> GetLastMessageAsync(int chatId)
    {
        return await _context.Messages
            .AsNoTracking()
            .Include(m => m.Sender)
            .Where(m => m.ChatId == chatId)
            .OrderByDescending(m => m.SentAt)
            .ThenByDescending(m => m.Id)
            .FirstOrDefaultAsync();
    }

    public async Task<Message?> GetMessageByIdAsync(int messageId)
    {
        return await _context.Messages
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == messageId);
    }

    public async Task<bool> DeleteChatAsync(int chatId)
    {
        var chat = await _context.Chats.FirstOrDefaultAsync(c => c.Id == chatId);
        if (chat == null)
            return false;

        _context.Chats.Remove(chat);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteMessageAsync(int messageId)
    {
        var message = await _context.Messages.FirstOrDefaultAsync(m => m.Id == messageId);
        if (message == null)
            return false;

        _context.Messages.Remove(message);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<int?> GetMessageChatIdAsync(int messageId)
    {
        return await _context.Messages
            .Where(m => m.Id == messageId)
            .Select(m => (int?)m.ChatId)
            .FirstOrDefaultAsync();
    }
}
