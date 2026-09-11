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
    public Task<User?> GetUserByUsernameAsync(string username) => _context.Users.FirstOrDefaultAsync(u => u.Username == username.Trim().ToLowerInvariant());
    public Task SaveChangesAsync() => _context.SaveChangesAsync();

    public async Task<Chat?> CreatePrivateChatAsync(long currentUserId, long otherUserId)
    {
        if (currentUserId <= 0 || otherUserId <= 0 || currentUserId == otherUserId) return null;
        if (!await UserExistsAsync(currentUserId) || !await UserExistsAsync(otherUserId)) return null;

        await CreateChatLock.WaitAsync();
        try
        {
            var existing = await _context.Chats
                .Include(c => c.Members)
                    .ThenInclude(m => m.User)
                .Where(c => c.Type == ChatType.Private &&
                    c.Members.Any(m => m.UserId == currentUserId) &&
                    c.Members.Any(m => m.UserId == otherUserId))
                .OrderByDescending(c => c.Id)
                .FirstOrDefaultAsync();

            if (existing != null)
                return existing;

            var newChat = new Chat
            {
                Type = ChatType.Private,
                Name = string.Empty,
                CreatedByUserId = currentUserId
            };

            _context.Chats.Add(newChat);
            await _context.SaveChangesAsync();

            _context.ChatMembers.AddRange(
                new ChatMember
                {
                    ChatId = newChat.Id,
                    UserId = currentUserId,
                    Role = ChatMemberRole.Owner,
                    JoinedAt = newChat.CreatedAt
                },
                new ChatMember
                {
                    ChatId = newChat.Id,
                    UserId = otherUserId,
                    Role = ChatMemberRole.Member,
                    JoinedAt = newChat.CreatedAt
                });

            await _context.SaveChangesAsync();
            return await GetChatByIdAsync(newChat.Id);
        }
        finally
        {
            CreateChatLock.Release();
        }
    }

    public async Task<Chat?> CreateGroupChatAsync(long creatorId, string name, IEnumerable<string> usernames)
    {
        name = name.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 128) return null;

        var normalized = usernames
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim().ToLowerInvariant())
            .Distinct()
            .ToList();

        var users = await _context.Users.Where(u => normalized.Contains(u.Username)).ToListAsync();
        if (users.Count != normalized.Count) return null;

        users.RemoveAll(u => u.Id == creatorId);
        var creator = await _context.Users.FirstOrDefaultAsync(u => u.Id == creatorId);
        if (creator == null) return null;

        var chat = new Chat
        {
            Type = ChatType.Group,
            Name = name,
            CreatedByUserId = creatorId
        };

        _context.Chats.Add(chat);
        await _context.SaveChangesAsync();

        _context.ChatMembers.Add(new ChatMember
        {
            ChatId = chat.Id,
            UserId = creatorId,
            Role = ChatMemberRole.Owner,
            JoinedAt = chat.CreatedAt
        });

        foreach (var user in users)
        {
            _context.ChatMembers.Add(new ChatMember
            {
                ChatId = chat.Id,
                UserId = user.Id,
                Role = ChatMemberRole.Member,
                JoinedAt = chat.CreatedAt
            });
        }

        await _context.SaveChangesAsync();
        return await GetChatByIdAsync(chat.Id);
    }

    public async Task<List<Chat>> GetUserChatsAsync(long userId)
    {
        var chats = await _context.Chats
            .AsNoTracking()
            .Include(c => c.Members)
                .ThenInclude(m => m.User)
            .Where(c => c.Members.Any(m => m.UserId == userId))
            .OrderByDescending(c => c.CreatedAt)
            .ThenByDescending(c => c.Id)
            .ToListAsync();

        foreach (var chat in chats)
        {
            var last = await GetLastMessageAsync(chat.Id, userId);
            chat.Messages = last == null ? [] : [last];
        }

        return chats
            .OrderByDescending(c => c.Messages.FirstOrDefault()?.SentAt ?? c.CreatedAt)
            .ThenByDescending(c => c.Id)
            .ToList();
    }

    public async Task<List<Chat>> GetAllChatsAsync()
    {
        var chats = await _context.Chats
            .AsNoTracking()
            .Include(c => c.Members)
                .ThenInclude(m => m.User)
            .OrderByDescending(c => c.CreatedAt)
            .ThenByDescending(c => c.Id)
            .ToListAsync();

        foreach (var chat in chats)
        {
            var last = await GetLastMessageAsync(chat.Id);
            chat.Messages = last == null ? [] : [last];
        }

        return chats
            .OrderByDescending(c => c.Messages.FirstOrDefault()?.SentAt ?? c.CreatedAt)
            .ThenByDescending(c => c.Id)
            .ToList();
    }

    public Task<Chat?> GetChatByIdAsync(int chatId) => _context.Chats
        .Include(c => c.Members)
            .ThenInclude(m => m.User)
        .FirstOrDefaultAsync(c => c.Id == chatId);

    public Task<bool> CanAccessChatAsync(int chatId, long userId) =>
        _context.Chats.AnyAsync(c => c.Id == chatId && c.Members.Any(m => m.UserId == userId));

    public async Task<Message?> SendMessageAsync(int chatId, long senderId, string content)
    {
        if (string.IsNullOrWhiteSpace(content) || !await CanAccessChatAsync(chatId, senderId)) return null;

        var chat = await _context.Chats.FirstOrDefaultAsync(c => c.Id == chatId);
        if (chat == null) return null;

        var message = new Message
        {
            ChatId = chatId,
            SenderId = senderId,
            Content = content.Trim()
        };

        _context.Messages.Add(message);
        await _context.SaveChangesAsync();
        await _context.Entry(message).Reference(m => m.Sender).LoadAsync();
        return message;
    }

    public async Task<List<Message>> GetMessagesAsync(int chatId, long viewerUserId, int? beforeMessageId = null, int pageSize = 50)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _context.Messages
            .AsNoTracking()
            .Include(m => m.Sender)
            .Where(m => m.ChatId == chatId && !m.DeletedForEveryone);

        if (beforeMessageId.HasValue)
        {
            var before = await _context.Messages
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == beforeMessageId.Value && m.ChatId == chatId && !m.DeletedForEveryone);

            if (before != null)
            {
                query = query.Where(m =>
                    m.SentAt < before.SentAt ||
                    (m.SentAt == before.SentAt && m.Id < before.Id));
            }
        }

        var messages = await query
            .OrderByDescending(m => m.SentAt)
            .ThenByDescending(m => m.Id)
            .Take(pageSize + 1)
            .ToListAsync();

        messages = messages
            .Where(m => !IsDeletedForUser(m, viewerUserId))
            .ToList();

        var hasExtra = messages.Count > pageSize;
        if (hasExtra)
            messages.RemoveAt(messages.Count - 1);

        return messages
            .OrderBy(m => m.SentAt)
            .ThenBy(m => m.Id)
            .ToList();
    }

    public async Task<bool> HasOlderMessagesAsync(int chatId, long viewerUserId, int firstMessageId)
    {
        var first = await _context.Messages
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == firstMessageId && m.ChatId == chatId && !m.DeletedForEveryone);

        if (first == null || IsDeletedForUser(first, viewerUserId))
            return false;

        var older = await _context.Messages
            .AsNoTracking()
            .Where(m => m.ChatId == chatId && !m.DeletedForEveryone &&
                (m.SentAt < first.SentAt || (m.SentAt == first.SentAt && m.Id < first.Id)))
            .OrderByDescending(m => m.SentAt)
            .ThenByDescending(m => m.Id)
            .Take(101)
            .ToListAsync();

        return older.Any(m => !IsDeletedForUser(m, viewerUserId));
    }

    public Task<Message?> GetLastMessageAsync(int chatId) => _context.Messages
        .AsNoTracking()
        .Include(m => m.Sender)
        .Where(m => m.ChatId == chatId && !m.DeletedForEveryone)
        .OrderByDescending(m => m.SentAt)
        .ThenByDescending(m => m.Id)
        .FirstOrDefaultAsync();

    public async Task<Message?> GetLastMessageAsync(int chatId, long viewerUserId)
    {
        var messages = await _context.Messages
            .AsNoTracking()
            .Include(m => m.Sender)
            .Where(m => m.ChatId == chatId && !m.DeletedForEveryone)
            .OrderByDescending(m => m.SentAt)
            .ThenByDescending(m => m.Id)
            .Take(100)
            .ToListAsync();

        return messages.FirstOrDefault(m => !IsDeletedForUser(m, viewerUserId));
    }

    public Task<Message?> GetMessageByIdAsync(int messageId) =>
        _context.Messages.AsNoTracking().FirstOrDefaultAsync(m => m.Id == messageId);

    public async Task<List<ChatMember>> GetMembersAsync(int chatId) => await _context.ChatMembers
        .AsNoTracking()
        .Include(m => m.User)
        .Where(m => m.ChatId == chatId)
        .OrderByDescending(m => m.Role)
        .ThenBy(m => m.JoinedAt)
        .ToListAsync();

    public Task<ChatMember?> GetMemberAsync(int chatId, long userId) =>
        _context.ChatMembers.FirstOrDefaultAsync(m => m.ChatId == chatId && m.UserId == userId);

    public async Task<bool> AddMemberAsync(int chatId, long actorId, long userId)
    {
        var chat = await _context.Chats.FirstOrDefaultAsync(c => c.Id == chatId && c.Type == ChatType.Group);
        if (chat == null) return false;

        var actor = await GetMemberAsync(chatId, actorId);
        if (actor == null || actor.Role == ChatMemberRole.Member) return false;
        if (await GetMemberAsync(chatId, userId) != null) return true;
        if (!await UserExistsAsync(userId)) return false;

        _context.ChatMembers.Add(new ChatMember { ChatId = chatId, UserId = userId, Role = ChatMemberRole.Member });
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> RemoveMemberAsync(int chatId, long actorId, long userId)
    {
        var chat = await _context.Chats.FirstOrDefaultAsync(c => c.Id == chatId && c.Type == ChatType.Group);
        if (chat == null) return false;

        var actor = await GetMemberAsync(chatId, actorId);
        var target = await GetMemberAsync(chatId, userId);
        if (actor == null || target == null || actor.Role == ChatMemberRole.Member || target.Role == ChatMemberRole.Owner) return false;
        if (actor.Role == ChatMemberRole.Admin && target.Role != ChatMemberRole.Member) return false;

        _context.ChatMembers.Remove(target);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> LeaveGroupAsync(int chatId, long userId)
    {
        var member = await GetMemberAsync(chatId, userId);
        if (member == null || member.Role == ChatMemberRole.Owner) return false;
        _context.ChatMembers.Remove(member);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> RenameGroupAsync(int chatId, long actorId, string name)
    {
        name = name.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 128) return false;

        var chat = await _context.Chats.FirstOrDefaultAsync(c => c.Id == chatId && c.Type == ChatType.Group);
        if (chat == null) return false;

        var actor = await GetMemberAsync(chatId, actorId);
        if (actor == null || actor.Role == ChatMemberRole.Member) return false;

        chat.Name = name;
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteChatAsync(int chatId)
    {
        var chat = await _context.Chats.FirstOrDefaultAsync(c => c.Id == chatId);
        if (chat == null) return false;
        _context.Chats.Remove(chat);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteMessageAsync(int messageId)
    {
        var message = await _context.Messages.FirstOrDefaultAsync(m => m.Id == messageId);
        if (message == null) return false;
        _context.Messages.Remove(message);
        await _context.SaveChangesAsync();
        return true;
    }

    public Task<int?> GetMessageChatIdAsync(int messageId) =>
        _context.Messages.Where(m => m.Id == messageId).Select(m => (int?)m.ChatId).FirstOrDefaultAsync();

    private static bool IsDeletedForUser(Message message, long userId)
    {
        if (userId <= 0 || string.IsNullOrWhiteSpace(message.DeletedForUserIds))
            return false;

        return message.DeletedForUserIds
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(id => long.TryParse(id, out var parsed) && parsed == userId);
    }
}
