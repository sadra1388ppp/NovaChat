using Microsoft.EntityFrameworkCore;
using NovaChat.Server.Data;
using NovaChat.Server.DTOs;
using NovaChat.Server.Entities;

namespace NovaChat.Server.Services;

public class ContactService
{
    private readonly AppDbContext _context;
    public ContactService(AppDbContext context) => _context = context;

    public async Task<(bool Success, string Message)> AddAsync(string ownerUserId, string contactUserId)
    {
        if (!long.TryParse(ownerUserId, out var ownerId) || !long.TryParse(contactUserId?.Trim(), out var contactId)) return (false, "Valid numeric user ID is required.");
        if (ownerId == contactId) return (false, "You cannot add yourself as a contact.");
        if (!await _context.Users.AnyAsync(u => u.Id == contactId)) return (false, "User not found.");
        if (await _context.Contacts.AnyAsync(c => c.OwnerUserId == ownerId && c.ContactUserId == contactId)) return (false, "This user is already in your contacts.");
        _context.Contacts.Add(new Contact { OwnerUserId = ownerId, ContactUserId = contactId, CreatedAt = DateTime.UtcNow });
        await _context.SaveChangesAsync(); return (true, "Contact added successfully.");
    }

    public async Task<List<ContactResponseDto>> GetAllAsync(string ownerUserId)
    {
        if (!long.TryParse(ownerUserId, out var ownerId)) return [];
        return await _context.Contacts.AsNoTracking().Where(c => c.OwnerUserId == ownerId).OrderBy(c => c.ContactUser.DisplayName)
            .Select(c => new ContactResponseDto { UserId = c.ContactUserId.ToString(), Username = c.ContactUser.Username, DisplayName = c.ContactUser.DisplayName, Email = c.ContactUser.Email, AddedAt = c.CreatedAt }).ToListAsync();
    }

    public async Task<bool> RemoveAsync(string ownerUserId, string contactUserId)
    {
        if (!long.TryParse(ownerUserId, out var ownerId) || !long.TryParse(contactUserId, out var contactId)) return false;
        var contact = await _context.Contacts.FirstOrDefaultAsync(c => c.OwnerUserId == ownerId && c.ContactUserId == contactId);
        if (contact == null) return false;
        _context.Contacts.Remove(contact); await _context.SaveChangesAsync(); return true;
    }
}
