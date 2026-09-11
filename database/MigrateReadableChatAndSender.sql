-- One-time MariaDB migration for an existing NovaChat database.
-- This migration preserves existing chats and messages.
-- Chats.Type becomes human-readable text: Private Chat / Group.
-- Messages.SenderId becomes the sender username.
-- No message rows are deleted.

-- ============================================================
-- 1. Convert Chats.Type from the legacy numeric representation
-- ============================================================
ALTER TABLE `Chats`
    MODIFY COLUMN `Type` VARCHAR(32) NOT NULL;

UPDATE `Chats`
SET `Type` = CASE
    WHEN `Type` IN ('0', 'Private', 'Private Chat') THEN 'Private Chat'
    WHEN `Type` IN ('1', 'Group') THEN 'Group'
    ELSE `Type`
END;

-- ============================================================
-- 2. Remove the old numeric SenderId foreign key if it exists
--    We discover its actual constraint name instead of assuming it.
-- ============================================================
SET @sender_fk = (
    SELECT kcu.CONSTRAINT_NAME
    FROM information_schema.KEY_COLUMN_USAGE kcu
    WHERE kcu.TABLE_SCHEMA = DATABASE()
      AND kcu.TABLE_NAME = 'Messages'
      AND kcu.COLUMN_NAME = 'SenderId'
      AND kcu.REFERENCED_TABLE_NAME = 'Users'
    LIMIT 1
);

SET @drop_sender_fk = IF(
    @sender_fk IS NULL,
    'SELECT 1',
    CONCAT('ALTER TABLE `Messages` DROP FOREIGN KEY `', @sender_fk, '`')
);

PREPARE stmt FROM @drop_sender_fk;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

-- ============================================================
-- 3. Convert numeric Message.SenderId to username text
-- ============================================================
ALTER TABLE `Messages`
    MODIFY COLUMN `SenderId` VARCHAR(32) NULL;

UPDATE `Messages` m
INNER JOIN `Users` u
    ON CAST(m.`SenderId` AS UNSIGNED) = u.`Id`
SET m.`SenderId` = u.`Username`
WHERE m.`SenderId` IS NOT NULL
  AND m.`SenderId` REGEXP '^[0-9]+$';

-- Older intermediate builds may have created SenderUsername.
ALTER TABLE `Messages`
    DROP COLUMN IF EXISTS `SenderUsername`;

-- Every message must have a readable sender username after migration.
ALTER TABLE `Messages`
    MODIFY COLUMN `SenderId` VARCHAR(32) NOT NULL;

-- ============================================================
-- 4. Verification queries
-- ============================================================
SELECT `Id`, `Type`, `Name`, `Members`
FROM `Chats`
ORDER BY `Id`;

SELECT `Id`, `ChatId`, `SenderId`, `Content`
FROM `Messages`
ORDER BY `Id` DESC
LIMIT 50;
