-- =====================================================================
-- 007_event_dates_and_times.sql
--
-- Purpose:
--   Add EventDatesJson (JSON array of "YYYY-MM-DD" strings), StartTime
--   ("HH:mm"), and EndTime ("HH:mm") columns to the events table.
--
-- Safe to run once against cdm_oneserve (MariaDB 10.4).
-- =====================================================================

ALTER TABLE events
    ADD COLUMN `EventDatesJson` LONGTEXT NULL,
    ADD COLUMN `StartTime`     VARCHAR(10) NULL,
    ADD COLUMN `EndTime`       VARCHAR(10) NULL;
