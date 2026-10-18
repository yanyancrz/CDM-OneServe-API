-- =====================================================================
-- CDM OneServe Marketplace - email outbox
-- 017_email_outbox.sql
--
-- PHASE 6, step 2. A durable queue for the emails the marketplace sends:
-- receipts (on payment) and refunds (on a confirmed refund).
--
-- WHY AN OUTBOX
--   Sending an email inside the request that created the order means a
--   transient SMTP failure loses the receipt forever, and a slow provider
--   makes the buyer wait. The outbox decouples the two: the order is
--   committed, a row is queued, and a worker sends it with retries.
--
--   The CampusMarket original used Resend with an Idempotency-Key of
--   campusmarket-email-{jobId}, so a retry after a timeout never double-sends.
--   This system uses Gmail SMTP (MailKit) through the existing EmailService,
--   which has no idempotency key - so the worker must be careful never to
--   claim the same row twice. The claim logic lives in C#
--   (MarketplaceEmailOutboxService), not in a stored procedure.
--
--   Emails are created ONLY from a confirmed cash payment, a verified online
--   payment, or a confirmed refund. Never from a request that might still fail.
--
-- NO STORED PROCEDURES
--   A previous version of this file (and of 016_rate_limiting.sql) contained
--   MySQL stored procedures. Their execution crashed the MySQL server and
--   destroyed the cdm_oneserve database, taking every table and every row of
--   every module with it. There are no stored procedures here, and there will
--   not be any. All logic lives in C#.
--
-- ARCHITECTURE (unchanged)
--   No new users table and no change to auth or RBAC. Everything points at the
--   existing `users`.`Id`. Money stays integer *_Centavos.
--
-- Run on `cdm_oneserve`. Safe to re-run.
-- =====================================================================

USE `cdm_oneserve`;

-- ---------------------------------------------------------------------
-- 1. THE OUTBOX
-- ---------------------------------------------------------------------
--   `kind` is 'receipt' or 'refund' - one of each per order, enforced by the
--   UNIQUE (order_id, kind) key.
--
--   `attempts` counts how many times the worker has tried. After 8, the row is
--   abandoned (first_attempt_at older than 23 hours) so a permanently broken
--   recipient does not block the queue forever.
--
--   `next_attempt_at` is when the worker may next try. Backoff: 2^min(attempts,7)
--   minutes (2, 4, 8, ... 128). This matches Resend's idempotency window so a
--   switch to Resend later needs no schema change.
--
--   `lease_until` / `lease_id` are the worker's lock: a row being sent is
--   leased for 2 minutes, and a crashed worker's lease expires so the next
--   worker picks it up.

CREATE TABLE IF NOT EXISTS `marketplace_email_outbox` (
  `Id` bigint(20) NOT NULL AUTO_INCREMENT,
  `OrderId` int(11) NOT NULL,
  `Kind` varchar(16) CHARACTER SET utf8mb4 NOT NULL,
  -- receipt | refund
  `Attempts` int(11) NOT NULL DEFAULT 0,
  `NextAttemptAt` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `LeaseUntil` datetime(6) NULL DEFAULT NULL,
  `LeaseId` char(36) CHARACTER SET ascii NULL DEFAULT NULL,
  `SentAt` datetime(6) NULL DEFAULT NULL,
  `ProviderEmailId` varchar(128) CHARACTER SET utf8mb4 NULL DEFAULT NULL,
  `LastError` varchar(500) CHARACTER SET utf8mb4 NULL DEFAULT NULL,
  `FirstAttemptAt` datetime(6) NULL DEFAULT NULL,
  `CreatedAt` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  PRIMARY KEY (`Id`),
  UNIQUE KEY `UQ_EmailOutbox_OrderKind` (`OrderId`,`Kind`),
  KEY `IX_EmailOutbox_Due` (`SentAt`,`NextAttemptAt`,`LeaseUntil`,`Id`),
  CONSTRAINT `FK_EmailOutbox_Order` FOREIGN KEY (`OrderId`)
    REFERENCES `marketplace_orders` (`OrderId`) ON DELETE CASCADE ON UPDATE NO ACTION,
  CONSTRAINT `CK_EmailOutbox_Kind` CHECK (`Kind` IN ('receipt','refund'))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ---------------------------------------------------------------------
-- 2. Confirm
-- ---------------------------------------------------------------------

SELECT COUNT(*) AS email_outbox_rows FROM marketplace_email_outbox;
