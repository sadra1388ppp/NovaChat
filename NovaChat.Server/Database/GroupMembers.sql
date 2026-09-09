-- Read-only database view for group members.
-- The source of truth remains ChatMembers; this view only makes
-- group membership easy to inspect from MariaDB clients.

CREATE OR REPLACE VIEW GroupMembers AS
SELECT
    c.Id AS GroupId,
    c.Name AS GroupName,
    u.Id AS UserId,
    u.Username,
    u.DisplayName,
    cm.Role,
    CASE cm.Role
        WHEN 2 THEN 'Owner'
        WHEN 1 THEN 'Admin'
        ELSE 'Member'
    END AS RoleName,
    cm.JoinedAt
FROM Chats AS c
INNER JOIN ChatMembers AS cm ON cm.ChatId = c.Id
INNER JOIN Users AS u ON u.Id = cm.UserId
WHERE c.Type = 1;
