-- Backfill metadata for identifiable legacy Central Audit Logs.
-- ActorName comes from the matching account's current display name.
-- Historical actor roles are left blank where no snapshot was recorded.
-- Safe to rerun because only rows with a missing Module are eligible.

START TRANSACTION;

UPDATE admin_activity_logs AS a
LEFT JOIN users AS u ON u.Id = a.UserId
SET a.Module = 'Library',
    a.Category = COALESCE(a.Category, 'Library'),
    a.ActorName = COALESCE(a.ActorName, NULLIF(TRIM(u.FullName), ''))
WHERE a.Module IS NULL
    AND LEFT(a.Action, 8) = 'LIBRARY_'
    AND LOCATE(':', a.Action) > 8;

UPDATE admin_activity_logs AS a
LEFT JOIN users AS u ON u.Id = a.UserId
SET a.Module = 'Users',
    a.Category = COALESCE(a.Category, 'User Management'),
    a.ActorName = COALESCE(a.ActorName, NULLIF(TRIM(u.FullName), ''))
WHERE a.Module IS NULL AND (
    a.Action IN ('Profile information updated', 'Password changed', 'Updated notification preferences')
    OR a.Action LIKE '% changed their password'
    OR a.Action LIKE '% changed their email to %'
    OR a.Action LIKE 'Created % account for %'
    OR a.Action LIKE 'Restored % account for %'
    OR a.Action LIKE 'Changed role of % from % to %'
    OR a.Action LIKE 'Changed status of % from % to %'
    OR a.Action LIKE 'Updated account details of %'
    OR a.Action LIKE 'Deleted account of %'
);

COMMIT;
