-- One-time MariaDB migration for existing NovaChat databases.
-- Chats.Type becomes human-readable text: Private Chat / Group.
-- Messages.SenderId becomes the sender username.
-- Existing message rows are preserved; only their sender representation is migrated.

ALTER TABLE `Chats`
    MODIFY COLUMN `Type` VARCHAR(32) NOT NULL;

UPDATE `Chats`
SET `Type` = CASE
    WHEN `Type` = '0' OR `Type` = 'Private' OR `Type` = 'Private Chat' THEN 'Private Chat'
    WHEN `Type` = '1' OR `Type` = 'Group' THEN 'Group'
    ELSE `Type`
END;

ALTER TABLE `Messages`
    DROP FOREIGN KEY `FK_Messages_Users_SenderId`;

ALTER TABLE `Messages`
    MODIFY COLUMN `SenderId` VARCHAR(32) NULL;

UPDATE `Messages` m
INNER JOIN `Users` u ON CAST(m.`SenderId` AS UNSIGNED) = u.`Id`
SET m.`SenderId` = u.`Username`
WHERE m.`SenderId` IS NOT NULL AND m.`SenderId` REGEXP '^[0-9]+$';

ALTER TABLE `Messages`
    DROP COLUMN IF EXISTS `SenderUsername`;

ALTER TABLE `Messages`
    MODIFY COLUMN `SenderId` VARCHAR(32) NOT NULL;
