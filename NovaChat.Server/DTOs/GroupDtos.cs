namespace NovaChat.Server.DTOs;

public class CreateGroupDto
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public class AddGroupMemberDto
{
    public string UserId { get; set; } = string.Empty;
}

public class GroupRoleDto
{
    public string Role { get; set; } = "Member";
}

public class GroupResponseDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string CreatorId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class GroupMemberResponseDto
{
    public string UserId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public DateTime JoinedAt { get; set; }
}

public class GroupMessageResponseDto
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

public class SendGroupMessageDto
{
    public string Content { get; set; } = string.Empty;
}

public class DeleteGroupMessageDto
{
    public bool ForEveryone { get; set; }
}