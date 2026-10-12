-- =====================================================================
-- CDM Lost & Found - Philippine wall-clock backfill
-- 003_ph_wall_clock.sql
--
-- WHY: the API used to write DateTime.UtcNow straight into `datetime`
-- columns (which carry no zone), while the PWA parses every value as local
-- time. Every stored instant therefore displayed 8 hours early in PH: a
-- 9 AM window showed as 1 AM, and "just now" read as "8 hours ago".
--
-- The service now writes PH wall clocks everywhere (PhNow/ToPhWallClock),
-- so this one-time +8h shift aligns the old rows with the new rule.
-- DateLostFound is deliberately EXCLUDED: old values are UTC midnights
-- from date-only picks, and shifting them would move the calendar day.
-- They already display the right date, and matching compares .Date.
--
-- RUN EXACTLY ONCE, on a database whose rows predate the fix. There is no
-- re-run guard by design: any date filter would wrongly skip future-dated
-- rows (availability windows live in the future). On a fresh database this
-- file is a harmless no-op (nothing to shift).
-- =====================================================================

USE `cdm_oneserve`;

UPDATE `LostFoundReports` SET `CreatedAt` = `CreatedAt` + INTERVAL 8 HOUR;
UPDATE `LostFoundNotifications` SET `CreatedAt` = `CreatedAt` + INTERVAL 8 HOUR;
UPDATE `ClaimRecords` SET `ClaimDate` = `ClaimDate` + INTERVAL 8 HOUR;
UPDATE `RecoveryRecords` SET `CreatedAt` = `CreatedAt` + INTERVAL 8 HOUR, `CompletedAt` = DATE_ADD(`CompletedAt`, INTERVAL 8 HOUR);
UPDATE `ClaimSchedules` SET `CreatedAt` = `CreatedAt` + INTERVAL 8 HOUR, `ScheduledDateTime` = `ScheduledDateTime` + INTERVAL 8 HOUR, `CompletedAt` = DATE_ADD(`CompletedAt`, INTERVAL 8 HOUR), `VerifiedAt` = DATE_ADD(`VerifiedAt`, INTERVAL 8 HOUR);
UPDATE `MatchRecords` SET `CreatedAt` = `CreatedAt` + INTERVAL 8 HOUR;
UPDATE `PickupAvailabilities` SET `StartDateTime` = `StartDateTime` + INTERVAL 8 HOUR, `EndDateTime` = `EndDateTime` + INTERVAL 8 HOUR, `CreatedAt` = `CreatedAt` + INTERVAL 8 HOUR;
