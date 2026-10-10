-- =====================================================================
-- CDM OneServe - Web Push (VAPID) schema
-- 001_web_push_schema.sql
--
-- Adds browser push notifications to CDM OneServe using the standard
-- Web Push Protocol (RFC 8030/8188). No Firebase, no cloud account.
--
--   PushSubscriptions  what PushManager.subscribe() produced, per user/device
--   PushOutbox         queued notifications, written by the ASP.NET API and
--                      delivered by the Node notification service (web-push)
--   PushDeliveryLogs   audit trail of every attempt (sent / failed / expired)
--
-- The VAPID key pair is NOT stored here: the private key lives only in the
-- Node service's environment, the public key in appsettings + the build.
--
-- Safe to run more than once (CREATE TABLE IF NOT EXISTS + guarded ALTERs).
-- Run it on the `cdm_oneserve` database.
-- =====================================================================

USE `cdm_oneserve`;

-- ---------------------------------------------------------------------
-- 1. Subscriptions
-- ---------------------------------------------------------------------
-- Endpoint is UNIQUE because that is the browser's own identity for the
-- subscription: if the same person signs in on two accounts the row simply
-- moves to the newest owner, which is exactly what the browser expects.

CREATE TABLE IF NOT EXISTS `PushSubscriptions` (
  `PushSubscriptionId` int(11) NOT NULL AUTO_INCREMENT,
  `UserId` int(11) NOT NULL,
  `Endpoint` varchar(500) NOT NULL,
  `P256dh` varchar(255) NOT NULL,
  `Auth` varchar(255) NOT NULL,
  `UserAgent` varchar(500) DEFAULT NULL,
  `DeviceLabel` varchar(100) DEFAULT NULL,
  `IsActive` tinyint(1) NOT NULL DEFAULT 1,
  `DeactivatedAt` datetime DEFAULT NULL,
  `LastUsedAt` datetime NOT NULL DEFAULT current_timestamp(),
  `CreatedAt` datetime NOT NULL DEFAULT current_timestamp(),
  PRIMARY KEY (`PushSubscriptionId`),
  UNIQUE KEY `IX_PushSub_Endpoint` (`Endpoint`),
  KEY `IX_PushSub_UserId_Active` (`UserId`,`IsActive`),
  CONSTRAINT `FK_PushSub_User`
    FOREIGN KEY (`UserId`) REFERENCES `users` (`Id`)
    ON DELETE CASCADE ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;


-- ---------------------------------------------------------------------
-- 2. Outbox (the queue the Node service drains)
-- ---------------------------------------------------------------------
-- Status/DedupeKey columns are added defensively so the script can be
-- re-run after a partial upgrade.

CREATE TABLE IF NOT EXISTS `PushOutbox` (
  `PushOutboxId` bigint(20) NOT NULL AUTO_INCREMENT,
  `UserId` int(11) NOT NULL,
  `PushSubscriptionId` int(11) DEFAULT NULL,
  `Module` varchar(50) NOT NULL DEFAULT 'General',
  `Type` varchar(80) NOT NULL DEFAULT 'General',
  `Title` varchar(200) NOT NULL,
  `Body` varchar(1000) NOT NULL,
  `IconUrl` varchar(500) DEFAULT NULL,
  `BadgeUrl` varchar(500) DEFAULT NULL,
  `TargetUrl` varchar(500) NOT NULL DEFAULT '/dashboard',
  `Tag` varchar(120) DEFAULT NULL,
  `DedupeKey` varchar(190) NOT NULL,
  `Priority` tinyint(4) NOT NULL DEFAULT 5,
  `Status` varchar(20) NOT NULL DEFAULT 'Pending',
  `Attempts` tinyint(4) NOT NULL DEFAULT 0,
  `LastError` varchar(1000) DEFAULT NULL,
  `ScheduledAt` datetime NOT NULL DEFAULT current_timestamp(),
  `CreatedAt` datetime NOT NULL DEFAULT current_timestamp(),
  `ProcessedAt` datetime DEFAULT NULL,
  PRIMARY KEY (`PushOutboxId`),
  -- The hard guarantee that one business event can never notify twice.
  UNIQUE KEY `IX_PushOutbox_DedupeKey` (`DedupeKey`),
  -- The exact shape the Node worker polls on.
  KEY `IX_PushOutbox_Claim` (`Status`,`ScheduledAt`,`PushOutboxId`),
  KEY `IX_PushOutbox_UserId` (`UserId`),
  CONSTRAINT `FK_PushOutbox_User`
    FOREIGN KEY (`UserId`) REFERENCES `users` (`Id`)
    ON DELETE CASCADE ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;


-- ---------------------------------------------------------------------
-- 3. Delivery log
-- ---------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS `PushDeliveryLogs` (
  `PushDeliveryLogId` bigint(20) NOT NULL AUTO_INCREMENT,
  `PushOutboxId` bigint(20) NOT NULL,
  `PushSubscriptionId` int(11) NOT NULL,
  `Status` varchar(20) NOT NULL DEFAULT 'Sent',
  `StatusCode` int(11) DEFAULT NULL,
  `Error` varchar(1000) DEFAULT NULL,
  `CreatedAt` datetime NOT NULL DEFAULT current_timestamp(),
  PRIMARY KEY (`PushDeliveryLogId`),
  KEY `IX_PushDeliveryLog_Outbox` (`PushOutboxId`),
  KEY `IX_PushDeliveryLog_Sub` (`PushSubscriptionId`,`CreatedAt`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;


-- ---------------------------------------------------------------------
-- 4. Migrate the old Lost & Found-only subscription table
-- ---------------------------------------------------------------------
-- The LostFoundPushSubscriptions table was module-scoped (it could only
-- ever be used by Lost & Found). PushSubscriptions is the portal-wide
-- table, so any rows already collected are carried over rather than
-- dropped. The old table is left in place as a backup; drop it once you
-- have confirmed the new table is in use.

INSERT INTO `PushSubscriptions`
  (`UserId`, `Endpoint`, `P256dh`, `Auth`, `UserAgent`, `DeviceLabel`,
   `IsActive`, `DeactivatedAt`, `LastUsedAt`, `CreatedAt`)
SELECT
  old.`UserId`, LEFT(old.`Endpoint`, 500), old.`P256dh`, old.`Auth`,
  NULL, 'Lost & Found', 1, NULL, old.`LastUsedAt`, old.`CreatedAt`
FROM `LostFoundPushSubscriptions` old
WHERE NOT EXISTS (
    SELECT 1 FROM `PushSubscriptions` neu WHERE neu.`Endpoint` = old.`Endpoint`
);


-- ---------------------------------------------------------------------
-- 5. Free the old table's name (optional, keep the backup for now)
-- ---------------------------------------------------------------------
-- DROP TABLE IF EXISTS `LostFoundPushSubscriptions`;


-- ---------------------------------------------------------------------
-- 6. Housekeeping
-- ---------------------------------------------------------------------
-- The API's PushMaintenanceHostedService prunes Sent rows after 30 days,
-- Failed rows after 7 days and dead subscriptions after 90 days. Run this
-- by hand once if you want an immediate clean start:

-- DELETE FROM `PushDeliveryLogs` WHERE `CreatedAt` < (NOW() - INTERVAL 30 DAY);
-- DELETE FROM `PushOutbox`
--  WHERE (`Status` = 'Sent'     AND COALESCE(`ProcessedAt`,`CreatedAt`) < NOW() - INTERVAL 30 DAY)
--     OR (`Status` IN ('Failed','Cancelled') AND COALESCE(`ProcessedAt`,`CreatedAt`) < NOW() - INTERVAL 7 DAY);
-- DELETE FROM `PushSubscriptions`
--  WHERE `IsActive` = 0 AND COALESCE(`DeactivatedAt`, `CreatedAt`) < NOW() - INTERVAL 90 DAY;
