-- Ensures deleting a chat removes all dependent database rows.
-- Run this once against the MariaDB NovaChat database.
-- This is safe with existing ON DELETE CASCADE foreign keys because
-- deleting already-removed child rows is simply a no-op.

DROP TRIGGER IF EXISTS TRG_Chats_DeleteMembers;
DROP TRIGGER IF EXISTS TRG_Chats_DeleteMessages;

DELIMITER //

CREATE TRIGGER TRG_Chats_DeleteMessages
BEFORE DELETE ON Chats
FOR EACH ROW
BEGIN
    DELETE FROM Messages
    WHERE ChatId = OLD.Id;
END//

CREATE TRIGGER TRG_Chats_DeleteMembers
BEFORE DELETE ON Chats
FOR EACH ROW
BEGIN
    DELETE FROM ChatMembers
    WHERE ChatId = OLD.Id;
END//

DELIMITER ;
