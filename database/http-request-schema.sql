-- NovaChat HTTP request log database schema.
-- The separate database is configured as "aaa".

CREATE TABLE IF NOT EXISTS `HttpRequests` (
    `Id` BIGINT NOT NULL,
    `Request` LONGTEXT NOT NULL,
    PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
