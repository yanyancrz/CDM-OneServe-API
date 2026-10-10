-- =====================================================================
-- CDM OneServe - user sessions ("signed in on another device")
-- 001_user_sessions.sql
--
-- One row per successful login. The JWT stays stateless; this only answers
-- "did somebody else sign in after you?" so the dashboard can warn.
--
-- Nothing in the auth path reads this table - deleting a row does not
-- invalidate a token.
--
-- Run on the `cdm_oneserve` database. Safe to re-run.
-- =====================================================================

USE `cdm_oneserve`;

CREATE TABLE IF NOT EXISTS `UserSessions` (
  `UserSessionId` int(11) NOT NULL AUTO_INCREMENT,
  `UserId` int(11) NOT NULL,
  `SessionKey` varchar(64) NOT NULL,
  `DeviceLabel` varchar(100) DEFAULT NULL,
  `UserAgent` varchar(500) DEFAULT NULL,
  `IpAddress` varchar(64) DEFAULT NULL,
  `CreatedAt` datetime NOT NULL DEFAULT current_timestamp(),
  `LastSeenAt` datetime NOT NULL DEFAULT current_timestamp(),
  `IsRevoked` tinyint(1) NOT NULL DEFAULT 0,
  PRIMARY KEY (`UserSessionId`),
  UNIQUE KEY `IX_UserSession_Key` (`SessionKey`),
  KEY `IX_UserSession_User_Recent` (`UserId`,`CreatedAt`),
  CONSTRAINT `FK_UserSession_User`
    FOREIGN KEY (`UserId`) REFERENCES `users` (`Id`)
    ON DELETE CASCADE ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Optional: prune long-dead rows (180 days) from a scheduled task.
-- DELETE FROM `UserSessions` WHERE `CreatedAt` < (NOW() - INTERVAL 180 DAY);
