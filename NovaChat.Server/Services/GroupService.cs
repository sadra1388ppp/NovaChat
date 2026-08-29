using Microsoft.EntityFrameworkCore;
using NovaChat.Server.Data;
using NovaChat.Server.DTOs;
using NovaChat.Server.Entities;

namespace NovaChat.Server.Services;

public class GroupService
{
    private readonly AppDbContext _context;

    public GroupService(AppDbContext context) => _context = context;

    public async Task<GroupResponseDto?> CreateAsync(string userId, CreateGroupDto dto)
    {
        var name = dto.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100) return null;
        if (!await _context.Users.AnyAsync(u => u.Id == userId)) return null;
        var group = new Group { Name = name, Description = dto.Description?.Trim() ?? string.Empty, CreatorId = userId };
        group.Members.Add(new GroupMember { UserId = userId, Role = GroupMemberRole.Owner });
        _context.Groups.Add(group);
        await _context.SaveChangesAsync();
        return ToDto(group);
    }

    public async Task<List<GroupResponseDto>> GetMyGroupsAsync(string userId) => await _context.GroupMembers.AsNoTracking().Where(m => m.UserId == userId).OrderByDescending(m => m.Group.CreatedAt).Select(m => new GroupResponseDto { Id = m.GroupId, Name = m.Group.Name, Description = m.Group.Description, CreatorId = m.Group.CreatorId, CreatedAt = m.Group.CreatedAt }).ToListAsync();

    public async Task<GroupResponseDto?> GetAsync(int groupId, string userId)
    {
        if (!await IsMemberAsync(groupId, userId)) return null;
        return await _context.Groups.AsNoTracking().Where(g => g.Id == groupId).Select(g => new GroupResponseDto { Id = g.Id, Name = g.Name, Description = g.Description, CreatorId = g.CreatorId, CreatedAt = g.CreatedAt }).FirstOrDefaultAsync();
    }

    public async Task<List<GroupMemberResponseDto>?> GetMembersAsync(int groupId, string userId)
    {
        if (!await IsMemberAsync(groupId, userId)) return null;
        return await _context.GroupMembers.AsNoTracking().Where(m => m.GroupId == groupId).OrderBy(m => m.Role).ThenBy(m => m.User.DisplayName).Select(m => new GroupMemberResponseDto { UserId = m.UserId, DisplayName = m.User.DisplayName, Role = m.Role.ToString(), JoinedAt = m.JoinedAt }).ToListAsync();
    }

    public async Task<(bool Success, string Message)> AddMemberAsync(int groupId, string actorId, string targetId)
    {
        if (!await CanManageAsync(groupId, actorId)) return (false, "Only the group owner or an admin can manage members.");
        if (string.IsNullOrWhiteSpace(targetId)) return (false, "User ID is required.");
        if (!await _context.Users.AnyAsync(u => u.Id == targetId.Trim())) return (false, "User not found.");
        if (await _context.GroupMembers.AnyAsync(m => m.GroupId == groupId && m.UserId == targetId.Trim())) return (false, "User is already a member.");
        _context.GroupMembers.Add(new GroupMember { GroupId = groupId, UserId = targetId.Trim(), Role = GroupMemberRole.Member });
        await _context.SaveChangesAsync();
        return (true, "Member added successfully.");
    }

    public async Task<(bool Success, string Message)> RemoveMemberAsync(int groupId, string actorId, string targetId)
    {
        var group = await _context.Groups.FindAsync(groupId);
        if (group == null) return (false, "Group not found.");
        var actor = await _context.GroupMembers.FirstOrDefaultAsync(m => m.GroupId == groupId && m.UserId == actorId);
        var target = await _context.GroupMembers.FirstOrDefaultAsync(m => m.GroupId == groupId && m.UserId == targetId);
        if (actor == null || target == null) return (false, "Member not found.");
        if (target.Role == GroupMemberRole.Owner) return (false, "The group owner cannot be removed.");
        if (actor.Role != GroupMemberRole.Owner && (actor.Role != GroupMemberRole.Admin || target.Role == GroupMemberRole.Admin)) return (false, "You cannot remove this member.");
        _context.GroupMembers.Remove(target);
        await _context.SaveChangesAsync();
        return (true, "Member removed successfully.");
    }

    public async Task<(bool Success, string Message)> LeaveAsync(int groupId, string userId)
    {
        var member = await _context.GroupMembers.FirstOrDefaultAsync(m => m.GroupId == groupId && m.UserId == userId);
        if (member == null) return (false, "You are not a member of this group.");
        if (member.Role == GroupMemberRole.Owner) return (false, "The group owner cannot leave. Transfer ownership or delete the group first.");
        _context.GroupMembers.Remove(member);
        await _context.SaveChangesAsync();
        return (true, "You left the group.");
    }

    public async Task<(bool Success, string Message)> SetRoleAsync(int groupId, string actorId, string targetId, string role)
    {
        var actor = await _context.GroupMembers.FirstOrDefaultAsync(m => m.GroupId == groupId && m.UserId == actorId);
        var target = await _context.GroupMembers.FirstOrDefaultAsync(m => m.GroupId == groupId && m.UserId == targetId);
        if (actor?.Role != GroupMemberRole.Owner) return (false, "Only the group owner can change roles.");
        if (target == null) return (false, "Member not found.");
        if (target.Role == GroupMemberRole.Owner) return (false, "The owner role cannot be changed here.");
        if (!Enum.TryParse<GroupMemberRole>(role, true, out var parsed) || parsed == GroupMemberRole.Owner) return (false, "Role must be Member or Admin.");
        target.Role = parsed;
        await _context.SaveChangesAsync();
        return (true, "Member role updated.");
    }

    public async Task<(bool Success, string Message)> DeleteAsync(int groupId, string userId)
    {
        var group = await _context.Groups.FindAsync(groupId);
        if (group == null) return (false, "Group not found.");
        if (group.CreatorId != userId) return (false, "Only the group owner can delete the group.");
        _context.Groups.Remove(group);
        await _context.SaveChangesAsync();
        return (true, "Group deleted successfully.");
    }

    public async Task<List<GroupMessageResponseDto>?> GetMessagesAsync(int groupId, string userId, int take = 100)
    {
        if (!await IsMemberAsync(groupId, userId)) return null;
        take = Math.Clamp(take, 1, 200);
        var messages = await _context.GroupMessages.AsNoTracking().Include(m => m.Sender).Where(m => m.GroupId == groupId).OrderByDescending(m => m.SentAt).ThenByDescending(m => m.Id).Take(take).ToListAsync();
        return messages.OrderBy(m => m.SentAt).ThenBy(m => m.Id).Select(m => new GroupMessageResponseDto { Id = m.Id, GroupId = m.GroupId, SenderId = m.SenderId, SenderName = m.Sender.DisplayName, Content = m.DeletedForEveryone ? "This message was deleted." : m.Content, SentAt = m.SentAt, DeletedForEveryone = m.DeletedForEveryone, DeletedForMe = IsDeletedForUser(m, userId) }).Where(m => !m.DeletedForMe).ToList();
    }

    public async Task<GroupMessage?> SendMessageAsync(int groupId, string senderId, string content)
    {
        if (!await IsMemberAsync(groupId, senderId)) return null;
        content = content.Trim();
        if (string.IsNullOrWhiteSpace(content) || content.Length > 4000) return null;
        var message = new GroupMessage { GroupId = groupId, SenderId = senderId, Content = content };
        _context.GroupMessages.Add(message);
        await _context.SaveChangesAsync();
        await _context.Entry(message).Reference(m => m.Sender).LoadAsync();
        return message;
    }

    public async Task<(bool Success, string Message, GroupMessageResponseDto? Data)> DeleteMessageAsync(int messageId, string userId, bool forEveryone)
    {
        var message = await _context.GroupMessages.Include(m => m.Sender).FirstOrDefaultAsync(m => m.Id == messageId);
        if (message == null) return (false, "Message not found.", null);
        if (!await IsMemberAsync(message.GroupId, userId)) return (false, "You are not a member of this group.", null);
        if (message.DeletedForEveryone) return (false, "This message has already been deleted for everyone.", null);

        if (forEveryone)
        {
            if (!string.Equals(message.SenderId, userId, StringComparison.OrdinalIgnoreCase)) return (false, "Only the sender can delete a message for everyone.", null);
            message.DeletedForEveryone = true;
            message.Content = string.Empty;
        }
        else
        {
            var ids = ParseDeletedIds(message.DeletedForUserIds);
            if (!ids.Contains(userId, StringComparer.OrdinalIgnoreCase)) ids.Add(userId);
            message.DeletedForUserIds = string.Join(",", ids);
        }

        await _context.SaveChangesAsync();
        return (true, "Message deleted successfully.", new GroupMessageResponseDto { Id = message.Id, GroupId = message.GroupId, SenderId = message.SenderId, SenderName = message.Sender?.DisplayName ?? string.Empty, Content = message.DeletedForEveryone ? "This message was deleted." : message.Content, SentAt = message.SentAt, DeletedForEveryone = message.DeletedForEveryone, DeletedForMe = !message.DeletedForEveryone && !forEveryone });
    }

    public Task<bool> IsMemberAsync(int groupId, string userId) => _context.GroupMembers.AnyAsync(m => m.GroupId == groupId && m.UserId == userId);
    private Task<bool> CanManageAsync(int groupId, string userId) => _context.GroupMembers.AnyAsync(m => m.GroupId == groupId && m.UserId == userId && (m.Role == GroupMemberRole.Owner || m.Role == GroupMemberRole.Admin));
    private static bool IsDeletedForUser(GroupMessage message, string userId) => ParseDeletedIds(message.DeletedForUserIds).Contains(userId, StringComparer.OrdinalIgnoreCase);
    private static List<string> ParseDeletedIds(string value) => string.IsNullOrWhiteSpace(value) ? [] : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    private static GroupResponseDto ToDto(Group g) => new() { Id = g.Id, Name = g.Name, Description = g.Description, CreatorId = g.CreatorId, CreatedAt = g.CreatedAt };
}