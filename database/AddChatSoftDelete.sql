-- One-time MariaDB migration for existing NovaChat databases.
-- Adds secure soft-delete fields to Chats without deleting any existing chat data.

ALTER TABLE `Chats`
    ADD COLUMN `IsDeleted` TINYINT(1) NOT NULL DEFAULT 0 AFTER `CreatedAt`,
    ADD COLUMN `DeletedAt` DATETIME(6) NULL AFTER `IsDeleted`;

CREATE INDEX `IX_Chats_IsDeleted` ON `Chats` (`IsDeleted`);
