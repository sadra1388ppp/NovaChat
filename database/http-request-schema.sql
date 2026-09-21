-- NovaChat HTTP request log database schema.
-- Create the separate database once:
-- CREATE DATABASE aaa CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `HttpRequests` (
    `Id` BIGINT NOT NULL AUTO_INCREMENT,
    `RequestId` VARCHAR(100) NOT NULL,
    `Method` VARCHAR(16) NOT NULL,
    `Scheme` VARCHAR(16) NOT NULL,
    `Host` VARCHAR(255) NOT NULL,
    `Path` VARCHAR(2048) NOT NULL,
    `QueryString` VARCHAR(4096) NULL,
    `Protocol` VARCHAR(32) NOT NULL,
    `StatusCode` INT NOT NULL,
    `IsAuthenticated` TINYINT(1) NOT NULL,
    `UserId` BIGINT NULL,
    `Username` VARCHAR(32) NULL,
    `IpAddress` VARCHAR(45) NULL,
    `UserAgent` VARCHAR(1024) NULL,
    `RequestContentType` VARCHAR(255) NULL,
    `RequestContentLength` BIGINT NULL,
    `ResponseContentType` VARCHAR(255) NULL,
    `ResponseContentLength` BIGINT NULL,
    `StartedAt` DATETIME(6) NOT NULL,
    `CompletedAt` DATETIME(6) NOT NULL,
    `DurationMs` BIGINT NOT NULL,
    `Succeeded` TINYINT(1) NOT NULL,
    `ExceptionType` VARCHAR(512) NULL,
    PRIMARY KEY (`Id`),
    INDEX `IX_HttpRequests_RequestId` (`RequestId`),
    INDEX `IX_HttpRequests_UserId` (`UserId`),
    INDEX `IX_HttpRequests_StatusCode` (`StatusCode`),
    INDEX `IX_HttpRequests_StartedAt` (`StartedAt`),
    INDEX `IX_HttpRequests_Path` (`Path`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
