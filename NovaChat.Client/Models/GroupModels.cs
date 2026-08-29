namespace NovaChat.Client.Models;

public class GroupModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string CreatorId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class GroupMemberModel
{
    public string UserId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public DateTime JoinedAt { get; set; }
}

public class GroupMessageModel
{
    public int Id { get; set; }
    public int GroupId { get; set; }
    public string SenderId { get; set; } = string.Empty;
    public string SenderName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime SentAt { get; set; }
    public bool DeletedForEveryone { get; set; }
    public bool DeletedForMe { get; set; }
}

public class CreateGroupRequest
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public class AddGroupMemberRequest
{
    public string UserId { get; set; } = string.Empty;
}

public class GroupRoleRequest
{
    public string Role { get; set; } = "Member";
}