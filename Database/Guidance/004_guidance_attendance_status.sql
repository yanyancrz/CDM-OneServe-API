-- Guidance attendance statuses: allow "Arrived" and "NoShow"
--
-- WHY: the counselor's attendance actions (Arrived / No-show - terminate)
-- write Status values the table's CHECK constraint did not list, so every
-- click failed with:
--   MySqlException: CONSTRAINT `ck_gapp_status` failed for guidance_appointments
-- surfaced to the UI as a 500 / "Action failed."
--
-- The constraint predates the attendance feature: it covers only
-- Pending/Confirmed/Cancelled/Completed/Rejected/Expired. This migration
-- adds the two attendance statuses, and re-aligns the generated "active"
-- keys with GuidanceScheduleService.OccupyingStatuses, which already
-- counts Arrived as occupying a slot (an in-progress session must keep
-- blocking the counselor's slot in the DB, not only in the C# pre-check).
--
-- Idempotent-safe: re-running fails loudly on DROP CONSTRAINT if already
-- applied, so check information_schema.CHECK_CONSTRAINTS first.

-- 1. The status vocabulary -------------------------------------------------
ALTER TABLE `guidance_appointments`
    DROP CONSTRAINT `ck_gapp_status`,
    ADD CONSTRAINT `ck_gapp_status`
        CHECK (`Status` IN (
            'Pending','Confirmed','Arrived','NoShow',
            'Completed','Cancelled','Rejected','Expired'
        ));

-- 2. Generated keys for the two unique slot/student indexes ------------------
-- Arrived now blocks the slot like Confirmed does. NoShow deliberately does
-- NOT: a terminated session frees the slot for other students.
ALTER TABLE `guidance_appointments`
    DROP INDEX `uq_ga_active_slot`,
    DROP INDEX `uq_ga_active_student_day`,
    DROP COLUMN `ActiveCounselorKey`,
    DROP COLUMN `ActiveStudentKey`;

ALTER TABLE `guidance_appointments`
    ADD COLUMN `ActiveCounselorKey` int
        GENERATED ALWAYS AS (
            IF(`Status` IN ('Pending','Confirmed','Arrived','Completed'),
               `CounselorId`, NULL)
        ) STORED,
    ADD COLUMN `ActiveStudentKey` int
        GENERATED ALWAYS AS (
            IF(`Status` IN ('Pending','Confirmed','Arrived','Completed'),
               `StudentId`, NULL)
        ) STORED;

ALTER TABLE `guidance_appointments`
    ADD UNIQUE KEY `uq_ga_active_slot` (`ActiveCounselorKey`, `Date`, `TimeSlot`),
    ADD UNIQUE KEY `uq_ga_active_student_day` (`ActiveStudentKey`, `Date`);
