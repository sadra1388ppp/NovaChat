using Microsoft.EntityFrameworkCore;
using NovaChat.Server.Data;
using NovaChat.Server.DTOs;
using NovaChat.Server.Entities;

namespace NovaChat.Server.Services;

public class ContactService
{
    private readonly AppDbContext _context;

    public ContactService(AppDbContext context) => _context = context;

    public async Task<List<ContactDto>> GetContactsAsync(long ownerId)
    {
        var contacts = await _context.Contacts
            .AsNoTracking()
            .Include(c => c.ContactUser)
            .Where(c => c.OwnerUserId == ownerId)
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new ContactDto
            {
                Id = c.Id,
                UserId = c.ContactUserId.ToString(),
                Username = c.ContactUser.Username,
                DisplayName = c.ContactUser.DisplayName,
                Email = c.ContactUser.Email,
                AddedAt = c.CreatedAt
            })
            .ToListAsync();

        var ownerUsername = await _context.Users.AsNoTracking()
            .Where(u => u.Id == ownerId)
            .Select(u => u.Username)
            .FirstOrDefaultAsync() ?? string.Empty;

        // Chats.Members is the single source of membership now. Load private chats
        // and resolve the other username in memory to keep the query MariaDB-safe.
        var privateChats = await _context.Chats.AsNoTracking()
            .Where(c => c.Type == ChatType.Private)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync();

        var usernameRows = privateChats
            .SelectMany(chat => ParseMembers(chat.Members)
                .Where(username => !string.Equals(username, ownerUsername, StringComparison.OrdinalIgnoreCase))
                .Select(username => new { Username = username, chat.CreatedAt }))
            .GroupBy(x => x.Username, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(x => x.CreatedAt).First())
            .ToList();

        var contactUsernames = usernameRows.Select(x => x.Username).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var existing = contacts.Select(c => c.Username).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var row in usernameRows)
        {
            if (existing.Contains(row.Username)) continue;
            var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Username == row.Username);
            if (user == null) continue;
            contacts.Add(new ContactDto
            {
                Id = 0,
                UserId = user.Id.ToString(),
                Username = user.Username,
                DisplayName = user.DisplayName,
                Email = user.Email,
                AddedAt = row.CreatedAt
            });
        }

        return contacts;
    }

    private static List<string> ParseMembers(string? members) =>
        string.IsNullOrWhiteSpace(members)
            ? []
            : members.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
}
