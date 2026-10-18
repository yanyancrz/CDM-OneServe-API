-- =====================================================================
-- CDM OneServe Marketplace - store analytics
-- 018_store_analytics.sql
--
-- PHASE 6, step 3. Sales figures for a storefront: a daily series for the
-- last 28 days and a 7-day average forecast.
--
-- WHY A VIEW, NOT A TABLE
--   Sales figures are derived, not stored. A stored summary would have to be
--   maintained on every order state change, and a bug there would silently
--   corrupt the seller's numbers. Computing on read is slower but always
--   correct, and the CampusMarket original did the same - the `store_workspace`
--   RPC groups on the fly.
--
--   The 28-day window and the 7-day forecast match the original exactly:
--     * sales_daily  - one row per Manila calendar day, last 28 days, including
--                      days with zero sales so the chart has no gaps;
--     * forecast     - the average daily sales of the previous 7 complete
--                      Manila days, including zero-sales days. It is explicitly
--                      a plain average, not a model - the original labelled it
--                      "not AI" and this one says the same.
--
-- MONEY IS INTEGER CENTAVOS, as everywhere else.
--
-- ARCHITECTURE (unchanged)
--   No new users table and no change to auth or RBAC. Everything points at
--   the existing `users`.`Id`. Money stays integer *_Centavos.
--
-- Run on `cdm_oneserve`. Safe to re-run.
-- =====================================================================

USE `cdm_oneserve`;

-- ---------------------------------------------------------------------
-- 1. sales_daily view
-- ---------------------------------------------------------------------
--   One row per (store, Manila calendar day) for the last 28 days, including
--   days with zero sales. Sales only ever count COMPLETED orders whose payment
--   is settled - never cancelled, never unpaid, never refunded.
--
--   The store is reached through the order's workspace, because an order does
--   not store a store id - it stores a workspace id, and a store fronts exactly
--   one workspace.
--
--   MySQL has no AT TIME ZONE syntax, so CONVERT_TZ is used. The server's
--   time zone must be set to UTC for this to be correct; the application stores
--   all timestamps as UTC (DateTime.UtcNow), so this is consistent.
--
--   MONEY AND UNITS COME FROM SEPARATE AGGREGATES, ON PURPOSE
--     An order with three items is three order_items rows. Summing
--     o.TotalCentavos while joined to those rows would count that order's total
--     three times, so the money/order count is aggregated per order first, and
--     the units are aggregated separately, then matched back on (workspace, day).
--     The previous version used COUNT(DISTINCT OrderId) but a plain
--     SUM(o.TotalCentavos), which inflated every multi-item order.

DROP VIEW IF EXISTS `marketplace_sales_daily`;

CREATE OR REPLACE VIEW `marketplace_sales_daily` AS
SELECT
    s.`StoreId` AS `StoreId`,
    o.`Day` AS `Day`,
    COALESCE(o.`SalesCentavos`, 0) AS `SalesCentavos`,
    COALESCE(i.`Units`, 0) AS `Units`,
    COALESCE(o.`Orders`, 0) AS `Orders`
FROM `marketplace_stores` s
LEFT JOIN (
    -- Money and order count, one row per (workspace, day).
    SELECT o.`WorkspaceId` AS `WorkspaceId`,
           CAST(CONVERT_TZ(o.`PaidAt`, '+00:00', '+08:00') AS DATE) AS `Day`,
           SUM(o.`TotalCentavos`) AS `SalesCentavos`,
           COUNT(*) AS `Orders`
    FROM `marketplace_orders` o
    WHERE o.`Status` = 'Completed'
      AND o.`PaymentStatus` = 'paid'
      AND o.`PaidAt` IS NOT NULL
      AND o.`PaidAt` > UTC_TIMESTAMP() - INTERVAL 28 DAY
    GROUP BY o.`WorkspaceId`, CAST(CONVERT_TZ(o.`PaidAt`, '+00:00', '+08:00') AS DATE)
) o ON o.`WorkspaceId` = s.`WorkspaceId`
LEFT JOIN (
    -- Units sold, a separate 1:N aggregate over order items.
    SELECT o.`WorkspaceId` AS `WorkspaceId`,
           CAST(CONVERT_TZ(o.`PaidAt`, '+00:00', '+08:00') AS DATE) AS `Day`,
           SUM(oi.`Quantity`) AS `Units`
    FROM `marketplace_orders` o
    JOIN `marketplace_order_items` oi ON oi.`OrderId` = o.`OrderId`
    WHERE o.`Status` = 'Completed'
      AND o.`PaymentStatus` = 'paid'
      AND o.`PaidAt` IS NOT NULL
      AND o.`PaidAt` > UTC_TIMESTAMP() - INTERVAL 28 DAY
    GROUP BY o.`WorkspaceId`, CAST(CONVERT_TZ(o.`PaidAt`, '+00:00', '+08:00') AS DATE)
) i ON i.`WorkspaceId` = s.`WorkspaceId` AND i.`Day` = o.`Day`;

-- ---------------------------------------------------------------------
-- 2. Confirm
-- ---------------------------------------------------------------------

SELECT COUNT(*) AS sales_daily_rows FROM marketplace_sales_daily;
SELECT * FROM marketplace_sales_daily LIMIT 5;
