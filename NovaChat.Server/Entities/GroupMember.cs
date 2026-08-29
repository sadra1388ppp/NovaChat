namespace NovaChat.Server.Entities;

public enum GroupMemberRole
{
    Member = 0,
    Admin = 1,
    Owner = 2
}

public class GroupMember
{
    public int GroupId { get; set; }
    public Group Group { get; set; } = null!;

    public string UserId { get; set; } = string.Empty;
    public User User { get; set; } = null!;

    public GroupMemberRole Role { get; set; } = GroupMemberRole.Member;
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
}