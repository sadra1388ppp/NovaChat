using Microsoft.EntityFrameworkCore;
using NovaChat.Server.Data;
using NovaChat.Server.Entities;

namespace NovaChat.Server.Services;

public class ChatService
{
    private static readonly SemaphoreSlim CreateChatLock = new(1, 1);
    private readonly AppDbContext _context;
    public ChatService(AppDbContext context) => _context = context;

    public Task<bool> UserExistsAsync(long userId) => _context.Users.AsNoTracking().AnyAsync(u => u.Id == userId);

    public async Task<User?> GetUserByUsernameAsync(string username) =>
        await _context.Users.FirstOrDefaultAsync(u => u.Username == username.Trim().ToLowerInvariant());

    public async Task<Chat?> CreatePrivateChatAsync(long currentUserId, long otherUserId)
    {
        if (currentUserId <= 0 || otherUserId <= 0 || currentUserId == otherUserId) return null;
        if (!await UserExistsAsync(currentUserId) || !await UserExistsAsync(otherUserId)) return null;
        await CreateChatLock.WaitAsync();
        try
        {
            var existing = await _context.Chats.Include(c => c.User1).Include(c => c.User2)
                .Where(c => (c.User1Id == currentUserId && c.User2Id == otherUserId) || (c.User1Id == otherUserId && c.User2Id == currentUserId))
                .OrderBy(c => c.Id).ToListAsync();
            if (existing.Count > 0)
            {
                if (existing.Count > 1) { _context.Chats.RemoveRange(existing.Skip(1)); await _context.SaveChangesAsync(); }
                return existing[0];
            }
            var chat = new Chat { User1Id = currentUserId, User2Id = otherUserId };
            _context.Chats.Add(chat); await _context.SaveChangesAsync();
            await _context.Entry(chat).Reference(c => c.User1).LoadAsync();
            await _context.Entry(chat).Reference(c => c.User2).LoadAsync();
            return chat;
        }
        finally { CreateChatLock.Release(); }
    }

    public async Task<List<Chat>> GetUserChatsAsync(long userId)
    {
        var chats = await _context.Chats.AsNoTracking().Include(c => c.User1).Include(c => c.User2)
            .Where(c => c.User1Id == userId || c.User2Id == userId).OrderByDescending(c => c.CreatedAt).ThenByDescending(c => c.Id).ToListAsync();
        foreach (var chat in chats)
        {
            var last = await GetLastMessageAsync(chat.Id);
            chat.Messages = last == null ? [] : [last];
        }
        return chats.OrderByDescending(c => c.Messages.FirstOrDefault()?.SentAt ?? c.CreatedAt).ThenByDescending(c => c.Id).ToList();
    }

    public async Task<List<Chat>> GetAllChatsAsync()
    {
        var chats = await _context.Chats.AsNoTracking().Include(c => c.User1).Include(c => c.User2).OrderByDescending(c => c.CreatedAt).ThenByDescending(c => c.Id).ToListAsync();
        foreach (var chat in chats)
        {
            var last = await GetLastMessageAsync(chat.Id); chat.Messages = last == null ? [] : [last];
        }
        return chats.OrderByDescending(c => c.Messages.FirstOrDefault()?.SentAt ?? c.CreatedAt).ThenByDescending(c => c.Id).ToList();
    }

    public Task<Chat?> GetChatByIdAsync(int chatId) => _context.Chats.Include(c => c.User1).Include(c => c.User2).FirstOrDefaultAsync(c => c.Id == chatId);

    public Task<bool> CanAccessChatAsync(int chatId, long userId) => _context.Chats.AnyAsync(c => c.Id == chatId && (c.User1Id == userId || c.User2Id == userId));

    public async Task<Message?> SendMessageAsync(int chatId, long senderId, string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return null;
        var chat = await _context.Chats.FirstOrDefaultAsync(c => c.Id == chatId);
        if (chat == null || (chat.User1Id != senderId && chat.User2Id != senderId)) return null;
        var message = new Message { ChatId = chatId, SenderId = senderId, Content = content.Trim() };
        _context.Messages.Add(message); await _context.SaveChangesAsync(); await _context.Entry(message).Reference(m => m.Sender).LoadAsync(); return message;
    }

    public async Task<List<Message>> GetMessagesAsync(int chatId, int? beforeMessageId = null, int pageSize = 50)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = _context.Messages.AsNoTracking().Include(m => m.Sender).Where(m => m.ChatId == chatId && !m.DeletedForEveryone);
        if (beforeMessageId.HasValue)
        {
            var before = await _context.Messages.AsNoTracking().FirstOrDefaultAsync(m => m.Id == beforeMessageId.Value && m.ChatId == chatId && !m.DeletedForEveryone);
            if (before != null) query = query.Where(m => m.SentAt < before.SentAt || (m.SentAt == before.SentAt && m.Id < before.Id));
        }
        var messages = await query.OrderByDescending(m => m.SentAt).ThenByDescending(m => m.Id).Take(pageSize + 1).ToListAsync();
        if (messages.Count > pageSize) messages.RemoveAt(messages.Count - 1);
        return messages.OrderBy(m => m.SentAt).ThenBy(m => m.Id).ToList();
    }

    public async Task<bool> HasOlderMessagesAsync(int chatId, int firstMessageId)
    {
        var first = await _context.Messages.AsNoTracking().FirstOrDefaultAsync(m => m.Id == firstMessageId && m.ChatId == chatId && !m.DeletedForEveryone);
        return first != null && await _context.Messages.AsNoTracking().AnyAsync(m => m.ChatId == chatId && !m.DeletedForEveryone && (m.SentAt < first.SentAt || (m.SentAt == first.SentAt && m.Id < first.Id)));
    }

    public Task<Message?> GetLastMessageAsync(int chatId) => _context.Messages.AsNoTracking().Include(m => m.Sender).Where(m => m.ChatId == chatId && !m.DeletedForEveryone).OrderByDescending(m => m.SentAt).ThenByDescending(m => m.Id).FirstOrDefaultAsync();
    public Task<Message?> GetMessageByIdAsync(int messageId) => _context.Messages.AsNoTracking().FirstOrDefaultAsync(m => m.Id == messageId);

    public async Task<bool> DeleteChatAsync(int chatId)
    {
        var chat = await _context.Chats.FirstOrDefaultAsync(c => c.Id == chatId); if (chat == null) return false;
        _context.Chats.Remove(chat); await _context.SaveChangesAsync(); return true;
    }

    public async Task<bool> DeleteMessageAsync(int messageId)
    {
        var message = await _context.Messages.FirstOrDefaultAsync(m => m.Id == messageId); if (message == null) return false;
        _context.Messages.Remove(message); await _context.SaveChangesAsync(); return true;
    }

    public Task<int?> GetMessageChatIdAsync(int messageId) => _context.Messages.Where(m => m.Id == messageId).Select(m => (int?)m.ChatId).FirstOrDefaultAsync();
}
