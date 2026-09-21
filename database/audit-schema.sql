-- NovaChat audit database schema.
-- Create the database separately:
-- CREATE DATABASE aaa CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
-- The server also creates this table automatically with AuditDbContext.

CREATE TABLE IF NOT EXISTS `AuditLogs` (
    `Id` BIGINT NOT NULL AUTO_INCREMENT,
    `UserId` BIGINT NULL,
    `Username` VARCHAR(32) NULL,
    `Category` VARCHAR(32) NOT NULL,
    `EventType` VARCHAR(64) NOT NULL,
    `TargetType` VARCHAR(32) NULL,
    `TargetId` VARCHAR(128) NULL,
    `ChatId` INT NULL,
    `MessageId` INT NULL,
    `DeviceId` VARCHAR(64) NULL,
    `RequestId` VARCHAR(64) NULL,
    `IpAddress` VARCHAR(45) NULL,
    `UserAgent` VARCHAR(512) NULL,
    `Succeeded` TINYINT(1) NOT NULL DEFAULT 1,
    `Details` TEXT NULL,
    `CreatedAt` DATETIME(6) NOT NULL,
    PRIMARY KEY (`Id`),
    INDEX `IX_AuditLogs_UserId` (`UserId`),
    INDEX `IX_AuditLogs_EventType` (`EventType`),
    INDEX `IX_AuditLogs_ChatId` (`ChatId`),
    INDEX `IX_AuditLogs_MessageId` (`MessageId`),
    INDEX `IX_AuditLogs_CreatedAt` (`CreatedAt`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
