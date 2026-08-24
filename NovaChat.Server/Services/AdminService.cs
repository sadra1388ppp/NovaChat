using Microsoft.EntityFrameworkCore;
using NovaChat.Server.Data;

namespace NovaChat.Server.Services;

public class AdminService
{
    private readonly AppDbContext _db;

    public AdminService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<int> GetUserCountAsync()
    {
        return await _db.Users.CountAsync();
    }

    public async Task<bool> UserExistsAsync(string id)
    {
        return await _db.Users.AnyAsync(u => u.Id == id);
    }
}