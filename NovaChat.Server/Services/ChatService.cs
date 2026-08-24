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

    public async Task<Chat?> CreatePrivateChatAsync(
        string currentUserId,
        string otherUserId)
    {
        if (string.IsNullOrWhiteSpace(otherUserId))
        {
            return null;
        }

        if (currentUserId == otherUserId)
        {
            return null;
        }

        var currentUserExists = await _context.Users
            .AnyAsync(u => u.Id == currentUserId);

        var otherUserExists = await _context.Users
            .AnyAsync(u => u.Id == otherUserId);

        if (!currentUserExists || !otherUserExists)
        {
            return null;
        }

        var existingChat = await _context.Chats
            .Include(c => c.User1)
            .Include(c => c.User2)
            .FirstOrDefaultAsync(c =>
                (c.User1Id == currentUserId &&
                 c.User2Id == otherUserId)
                ||
                (c.User1Id == otherUserId &&
                 c.User2Id == currentUserId));

        if (existingChat != null)
        {
            return existingChat;
        }

        var chat = new Chat
        {
            User1Id = currentUserId,
            User2Id = otherUserId
        };

        _context.Chats.Add(chat);

        await _context.SaveChangesAsync();

        await _context.Entry(chat)
            .Reference(c => c.User1)
            .LoadAsync();

        await _context.Entry(chat)
            .Reference(c => c.User2)
            .LoadAsync();

        return chat;
    }

    public async Task<List<Chat>> GetUserChatsAsync(string userId)
    {
        return await _context.Chats
            .Include(c => c.User1)
            .Include(c => c.User2)
            .Where(c =>
                c.User1Id == userId ||
                c.User2Id == userId)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync();
    }

    public async Task<List<Chat>> GetAllChatsAsync()
    {
        return await _context.Chats
            .Include(c => c.User1)
            .Include(c => c.User2)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync();
    }

    public async Task<Chat?> GetChatByIdAsync(int chatId)
    {
        return await _context.Chats
            .Include(c => c.User1)
            .Include(c => c.User2)
            .FirstOrDefaultAsync(c => c.Id == chatId);
    }

    public async Task<bool> CanAccessChatAsync(
        int chatId,
        string userId)
    {
        return await _context.Chats
            .AnyAsync(c =>
                c.Id == chatId &&
                (c.User1Id == userId ||
                 c.User2Id == userId));
    }

    public async Task<Message?> SendMessageAsync(
        int chatId,
        string senderId,
        string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        var chat = await _context.Chats
            .FirstOrDefaultAsync(c => c.Id == chatId);

        if (chat == null)
        {
            return null;
        }

        if (chat.User1Id != senderId &&
            chat.User2Id != senderId)
        {
            return null;
        }

        var message = new Message
        {
            ChatId = chatId,
            SenderId = senderId,
            Content = content.Trim()
        };

        _context.Messages.Add(message);

        await _context.SaveChangesAsync();

        await _context.Entry(message)
            .Reference(m => m.Sender)
            .LoadAsync();

        return message;
    }

    public async Task<List<Message>> GetMessagesAsync(int chatId)
    {
        return await _context.Messages
            .Include(m => m.Sender)
            .Where(m => m.ChatId == chatId)
            .OrderBy(m => m.SentAt)
            .ToListAsync();
    }

    public async Task<bool> DeleteChatAsync(int chatId)
    {
        var chat = await _context.Chats
            .FirstOrDefaultAsync(c => c.Id == chatId);

        if (chat == null)
        {
            return false;
        }

        _context.Chats.Remove(chat);

        await _context.SaveChangesAsync();

        return true;
    }

    public async Task<bool> DeleteMessageAsync(int messageId)
    {
        var message = await _context.Messages
            .FirstOrDefaultAsync(m => m.Id == messageId);

        if (message == null)
        {
            return false;
        }

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