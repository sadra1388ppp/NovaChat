USE `NovaChat`;

-- NovaChat EF model requires:
-- Chats.Id    = INT
-- Users.Id    = BIGINT
-- ChatMembers.ChatId = INT
-- ChatMembers.UserId = BIGINT
-- The old ChatMembers table was created with string IDs, which prevents
-- the required foreign keys from being created correctly.

SET FOREIGN_KEY_CHECKS = 0;
DROP TABLE IF EXISTS `ChatMembers`;
SET FOREIGN_KEY_CHECKS = 1;

CREATE TABLE `ChatMembers` (
    `Id` INT NOT NULL AUTO_INCREMENT,
    `ChatId` INT NOT NULL,
    `UserId` BIGINT NOT NULL,
    `Role` INT NOT NULL DEFAULT 0,
    `JoinedAt` DATETIME(6) NOT NULL,
    PRIMARY KEY (`Id`),
    UNIQUE KEY `IX_ChatMembers_ChatId_UserId` (`ChatId`, `UserId`),
    KEY `IX_ChatMembers_UserId` (`UserId`),
    CONSTRAINT `FK_ChatMembers_Chats_ChatId`
        FOREIGN KEY (`ChatId`) REFERENCES `Chats` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_ChatMembers_Users_UserId`
        FOREIGN KEY (`UserId`) REFERENCES `Users` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB
  DEFAULT CHARSET=utf8mb4
  COLLATE=utf8mb4_unicode_ci;

-- Verify the repaired table and foreign keys.
SELECT TABLE_NAME, COLUMN_NAME, COLUMN_TYPE, IS_NULLABLE
FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE()
  AND TABLE_NAME = 'ChatMembers'
ORDER BY ORDINAL_POSITION;

SELECT CONSTRAINT_NAME, TABLE_NAME, COLUMN_NAME, REFERENCED_TABLE_NAME, REFERENCED_COLUMN_NAME
FROM information_schema.KEY_COLUMN_USAGE
WHERE TABLE_SCHEMA = DATABASE()
  AND TABLE_NAME = 'ChatMembers'
  AND REFERENCED_TABLE_NAME IS NOT NULL
ORDER BY CONSTRAINT_NAME;
