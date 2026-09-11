-- One-time migration for an EXISTING NovaChat MariaDB database.
-- Converts ChatMembers rows into Chats.Members as comma-separated usernames,
-- then removes the ChatMembers table.
-- Run this once in the NovaChat database after pulling the refactored main branch.

USE `NovaChat`;

SET FOREIGN_KEY_CHECKS = 0;

ALTER TABLE `Chats`
    ADD COLUMN `Members` VARCHAR(4000) NOT NULL DEFAULT '' AFTER `Name`;

UPDATE `Chats` AS c
LEFT JOIN (
    SELECT
        cm.`ChatId`,
        GROUP_CONCAT(u.`Username` ORDER BY cm.`Id` SEPARATOR ', ') AS `Members`
    FROM `ChatMembers` AS cm
    INNER JOIN `Users` AS u ON u.`Id` = cm.`UserId`
    GROUP BY cm.`ChatId`
) AS members ON members.`ChatId` = c.`Id`
SET c.`Members` = COALESCE(members.`Members`, '');

DROP TABLE IF EXISTS `ChatMembers`;

SET FOREIGN_KEY_CHECKS = 1;

SELECT `Id`, `Type`, `Name`, `Members`, `CreatedByUserId`, `CreatedAt`
FROM `Chats`
ORDER BY `Id`;
