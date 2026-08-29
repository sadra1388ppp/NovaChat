using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NovaChat.Server.DTOs;
using NovaChat.Server.Services;
using System.Security.Claims;

namespace NovaChat.Server.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class GroupController : ControllerBase
{
    private readonly GroupService _groups;
    public GroupController(GroupService groups) => _groups = groups;

    [HttpPost]
    public async Task<IActionResult> Create(CreateGroupDto dto) => Ok(await _groups.CreateAsync(UserId(), dto));

    [HttpGet]
    public async Task<IActionResult> GetMine() => Ok(await _groups.GetMyGroupsAsync(UserId()));

    [HttpGet("{groupId:int}")]
    public async Task<IActionResult> Get(int groupId) => Result(await _groups.GetAsync(groupId, UserId()));

    [HttpGet("{groupId:int}/members")]
    public async Task<IActionResult> Members(int groupId) => Result(await _groups.GetMembersAsync(groupId, UserId()));

    [HttpPost("{groupId:int}/members")]
    public async Task<IActionResult> AddMember(int groupId, AddGroupMemberDto dto) => MessageResult(await _groups.AddMemberAsync(groupId, UserId(), dto.UserId));

    [HttpDelete("{groupId:int}/members/{userId}")]
    public async Task<IActionResult> RemoveMember(int groupId, string userId) => MessageResult(await _groups.RemoveMemberAsync(groupId, UserId(), userId));

    [HttpPost("{groupId:int}/leave")]
    public async Task<IActionResult> Leave(int groupId) => MessageResult(await _groups.LeaveAsync(groupId, UserId()));

    [HttpPut("{groupId:int}/members/{userId}/role")]
    public async Task<IActionResult> SetRole(int groupId, string userId, GroupRoleDto dto) => MessageResult(await _groups.SetRoleAsync(groupId, UserId(), userId, dto.Role));

    [HttpDelete("{groupId:int}")]
    public async Task<IActionResult> Delete(int groupId) => MessageResult(await _groups.DeleteAsync(groupId, UserId()));

    [HttpGet("{groupId:int}/messages")]
    public async Task<IActionResult> Messages(int groupId, [FromQuery] int take = 100) => Result(await _groups.GetMessagesAsync(groupId, UserId(), take));

    private async Task<IActionResult> SendMessage(int groupId, string content)
    {
        var message = await _groups.SendMessageAsync(groupId, UserId(), content);
        if (message == null) return BadRequest(new { message = "Unable to send message or you are not a group member." });
        return Ok(new GroupMessageResponseDto { Id = message.Id, GroupId = message.GroupId, SenderId = message.SenderId, SenderName = message.Sender.DisplayName, Content = message.Content, SentAt = message.SentAt });
    }

    [HttpPost("{groupId:int}/messages")]
    public Task<IActionResult> Send(int groupId, SendGroupMessageDto dto) => SendMessage(groupId, dto.Content);

    private string UserId() => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    private IActionResult Result<T>(T? value) => value == null ? NotFound() : Ok(value);
    private IActionResult MessageResult((bool Success, string Message) result) => result.Success ? Ok(new { message = result.Message }) : BadRequest(new { message = result.Message });
}