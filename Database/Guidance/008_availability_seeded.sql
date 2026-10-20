-- =====================================================================
-- 008_availability_seeded.sql
--
-- Purpose:
--   1. Add AvailabilitySeeded column to guidance_counselor_profiles.
--   2. Add unique constraint on guidance_availability for counselorId + dayOfWeek + startTime + endTime.
--   3. Delete existing blocks with isActive = false (they should not become bookable).
--   4. Seed default schedule for counselors with no blocks.
--   5. Set AvailabilitySeeded = true for all counselors.
--
-- Safe to run once against cdm_oneserve (MariaDB 10.4).
-- =====================================================================

-- 1. Add AvailabilitySeeded column
ALTER TABLE guidance_counselor_profiles
    ADD COLUMN AvailabilitySeeded TINYINT(1) NOT NULL DEFAULT 0;

-- 2. Add unique constraint (drop old index first if exists)
ALTER TABLE guidance_availability
    ADD UNIQUE INDEX IX_guidance_availability_CounselorId_DayOfWeek_StartTime_EndTime
    (CounselorId, DayOfWeek, StartTime, EndTime);

-- 3. Delete inactive blocks (they should not become bookable)
DELETE FROM guidance_availability WHERE IsActive = 0;

-- 4. Seed default schedule for counselors with no blocks
-- Default: Mon-Fri, 8:00-12:00 and 13:00-17:00
INSERT INTO guidance_availability (CounselorId, DayOfWeek, StartTime, EndTime, IsActive)
SELECT cp.UserId, d.DayOfWeek, t.StartTime, t.EndTime, 1
FROM guidance_counselor_profiles cp
CROSS JOIN (SELECT 1 AS DayOfWeek UNION ALL SELECT 2 UNION ALL SELECT 3 UNION ALL SELECT 4 UNION ALL SELECT 5) d
CROSS JOIN (
    SELECT '08:00:00' AS StartTime, '12:00:00' AS EndTime
    UNION ALL
    SELECT '13:00:00', '17:00:00'
) t
WHERE NOT EXISTS (
    SELECT 1 FROM guidance_availability ga WHERE ga.CounselorId = cp.UserId
);

-- 5. Set AvailabilitySeeded = true for all counselors
UPDATE guidance_counselor_profiles SET AvailabilitySeeded = 1;
