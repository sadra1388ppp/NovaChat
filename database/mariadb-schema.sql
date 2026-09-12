-- Initial NovaChat schema for a NEW, EMPTY MariaDB database (11.4 or newer).
-- Chat participants are stored as comma-separated usernames in Chats.Members.
-- There is intentionally no ChatMembers table.
-- Chats has no User1Id/User2Id columns.
-- Chat deletion is a soft delete: IsDeleted/DeletedAt preserve records for security and auditability.
-- Messages store SenderId as the sender username for human-readable audit/history display.
-- MessageReads stores durable per-user read receipts for professional unread/seen behavior.
-- Select/create the target database separately. This is not an in-place upgrade.
-- All application dates are UTC.

CREATE TABLE `Users` (
    `Id` BIGINT NOT NULL AUTO_INCREMENT,
    `Username` VARCHAR(32) NOT NULL,
    `DisplayName` VARCHAR(50) NOT NULL,
    `Email` VARCHAR(254) NOT NULL,
    `PhoneNumber` VARCHAR(32) NULL,
    `PasswordHash` VARCHAR(512) NOT NULL,
    `Bio` VARCHAR(160) NOT NULL,
    `AvatarUrl` VARCHAR(512) NULL,
    `LastSeenAt` DATETIME(6) NULL,
    `CreatedAt` DATETIME(6) NOT NULL,
    PRIMARY KEY (`Id`),
    UNIQUE KEY `IX_Users_Username` (`Username`),
    UNIQUE KEY `IX_Users_Email` (`Email`),
    UNIQUE KEY `IX_Users_PhoneNumber` (`PhoneNumber`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE `Chats` (
    `Id` INT NOT NULL AUTO_INCREMENT,
    `Type` VARCHAR(32) NOT NULL,
    `Name` VARCHAR(128) NOT NULL,
    `Members` VARCHAR(4000) NOT NULL,
    `AvatarUrl` VARCHAR(512) NULL,
    `CreatedByUserId` BIGINT NULL,
    `CreatedAt` DATETIME(6) NOT NULL,
    `IsDeleted` TINYINT(1) NOT NULL DEFAULT 0,
    `DeletedAt` DATETIME(6) NULL,
    PRIMARY KEY (`Id`),
    KEY `IX_Chats_CreatedByUserId` (`CreatedByUserId`),
    KEY `IX_Chats_IsDeleted` (`IsDeleted`),
    CONSTRAINT `FK_Chats_Users_CreatedByUserId` FOREIGN KEY (`CreatedByUserId`) REFERENCES `Users` (`Id`) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE `Messages` (
    `Id` INT NOT NULL AUTO_INCREMENT,
    `ChatId` INT NOT NULL,
    `SenderId` VARCHAR(32) NOT NULL,
    `Content` LONGTEXT NOT NULL,
    `SentAt` DATETIME(6) NOT NULL,
    `DeletedForEveryone` TINYINT(1) NOT NULL,
    `DeletedForUserIds` LONGTEXT NOT NULL,
    PRIMARY KEY (`Id`),
    KEY `IX_Messages_ChatId_SentAt_Id` (`ChatId`, `SentAt`, `Id`),
    KEY `IX_Messages_SenderId` (`SenderId`),
    CONSTRAINT `FK_Messages_Chats_ChatId` FOREIGN KEY (`ChatId`) REFERENCES `Chats` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE `Contacts` (
    `Id` INT NOT NULL AUTO_INCREMENT,
    `OwnerUserId` BIGINT NOT NULL,
    `ContactUserId` BIGINT NOT NULL,
    `CreatedAt` DATETIME(6) NOT NULL,
    PRIMARY KEY (`Id`),
    UNIQUE KEY `IX_Contacts_OwnerUserId_ContactUserId` (`OwnerUserId`, `ContactUserId`),
    KEY `IX_Contacts_ContactUserId` (`ContactUserId`),
    CONSTRAINT `FK_Contacts_Users_OwnerUserId` FOREIGN KEY (`OwnerUserId`) REFERENCES `Users` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_Contacts_Users_ContactUserId` FOREIGN KEY (`ContactUserId`) REFERENCES `Users` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE `MessageReads` (
    `MessageId` INT NOT NULL,
    `UserId` BIGINT NOT NULL,
    `ReadAt` DATETIME(6) NOT NULL,
    PRIMARY KEY (`MessageId`, `UserId`),
    KEY `IX_MessageReads_UserId` (`UserId`),
    CONSTRAINT `FK_MessageReads_Messages_MessageId` FOREIGN KEY (`MessageId`) REFERENCES `Messages` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_MessageReads_Users_UserId` FOREIGN KEY (`UserId`) REFERENCES `Users` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
