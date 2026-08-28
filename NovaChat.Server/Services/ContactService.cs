using Microsoft.EntityFrameworkCore;
using NovaChat.Server.Data;
using NovaChat.Server.DTOs;
using NovaChat.Server.Entities;

namespace NovaChat.Server.Services;

public class ContactService
{
    private readonly AppDbContext _context;

    public ContactService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<(bool Success, string Message)> AddAsync(string ownerUserId, string contactUserId)
    {
        if (string.IsNullOrWhiteSpace(contactUserId))
            return (false, "User ID is required.");

        contactUserId = contactUserId.Trim();

        if (string.Equals(ownerUserId, contactUserId, StringComparison.OrdinalIgnoreCase))
            return (false, "You cannot add yourself as a contact.");

        var contactUser = await _context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == contactUserId);

        if (contactUser == null)
            return (false, "User not found.");

        var exists = await _context.Contacts
            .AnyAsync(c => c.OwnerUserId == ownerUserId && c.ContactUserId == contactUserId);

        if (exists)
            return (false, "This user is already in your contacts.");

        _context.Contacts.Add(new Contact
        {
            OwnerUserId = ownerUserId,
            ContactUserId = contactUserId,
            CreatedAt = DateTime.UtcNow
        });

        await _context.SaveChangesAsync();
        return (true, "Contact added successfully.");
    }

    public async Task<List<ContactResponseDto>> GetAllAsync(string ownerUserId)
    {
        return await _context.Contacts
            .AsNoTracking()
            .Where(c => c.OwnerUserId == ownerUserId)
            .OrderBy(c => c.ContactUser.DisplayName)
            .Select(c => new ContactResponseDto
            {
                UserId = c.ContactUserId,
                DisplayName = c.ContactUser.DisplayName,
                Email = c.ContactUser.Email,
                AddedAt = c.CreatedAt
            })
            .ToListAsync();
    }

    public async Task<bool> RemoveAsync(string ownerUserId, string contactUserId)
    {
        var contact = await _context.Contacts
            .FirstOrDefaultAsync(c =>
                c.OwnerUserId == ownerUserId &&
                c.ContactUserId == contactUserId);

        if (contact == null)
            return false;

        _context.Contacts.Remove(contact);
        await _context.SaveChangesAsync();
        return true;
    }
}
