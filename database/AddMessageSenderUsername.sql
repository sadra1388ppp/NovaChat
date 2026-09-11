-- One-time MariaDB migration for existing NovaChat databases.
-- Preserves SenderId for relational/audit integrity and adds a human-readable sender username.

ALTER TABLE `Messages`
    ADD COLUMN `SenderUsername` VARCHAR(32) NULL AFTER `SenderId`;

UPDATE `Messages` m
INNER JOIN `Users` u ON u.`Id` = m.`SenderId`
SET m.`SenderUsername` = u.`Username`
WHERE m.`SenderUsername` IS NULL OR m.`SenderUsername` = '';

ALTER TABLE `Messages`
    MODIFY COLUMN `SenderUsername` VARCHAR(32) NOT NULL;

CREATE INDEX `IX_Messages_SenderUsername` ON `Messages` (`SenderUsername`);
