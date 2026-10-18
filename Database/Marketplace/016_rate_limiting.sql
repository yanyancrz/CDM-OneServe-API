-- =====================================================================
-- CDM OneServe Marketplace - rate limiting
-- 016_rate_limiting.sql
--
-- PHASE 6, step 1. Fixed-window counters for the actions that need them:
--   * student-number login, per IP and per student number
--   * media uploads, per user
--
-- WHY THESE THREE AND NOT MORE
--   CampusMarket only rate-limits these three, and for the same reason: they are
--   the endpoints a bot can hit without an account, or the ones that cost real
--   money (storage, email). place_order, report, support, review_order and the
--   rest are gated by authentication and by the existing AccountStatus check, so
--   they are not limited here. Adding limits to everything would just lock out
--   legitimate users during a rush.
--
--   The plain email/password login is NOT limited by this app at all: it relies
--   on Supabase's own rate limits, which are stricter and cannot be bypassed.
--   (In this system, the equivalent is ASP.NET's built-in rate limiting or a
--   reverse proxy. This table is the data layer; the enforcement lives in
--   MarketplaceRateLimitService, a C# service, not in a stored procedure.)
--
-- HOW IT WORKS
--   One row per key. The window resets when it is older than `seconds`; the
--   count is incremented otherwise. consume_rate_limit returns true when the
--   count is within the limit, false when it is exceeded.
--
--   The key is hashed (SHA-256) before it is stored, so a database dump
--   cannot be used to enumerate which IP addresses or student numbers tried to
--   log in. The raw value never touches the database.
--
--   NOTE: this table is the data layer only. The enforcement logic lives in
--   MarketplaceRateLimitService (C#), because the user's backend is ASP.NET
--   Core + MySQL, not Supabase + Deno. There is no stored procedure here:
--   a previous version of this file contained one, and its execution crashed
--   the MySQL server and destroyed the cdm_oneserve database. Do not add one.
--
-- ARCHITECTURE (unchanged)
--   No new users table and no change to auth or RBAC. Everything points at the
--   existing `users`.`Id`. Money stays integer *_Centavos.
--
-- Run on `cdm_oneserve`. Safe to re-run.
-- =====================================================================

USE `cdm_oneserve`;

CREATE TABLE IF NOT EXISTS `marketplace_rate_limits` (
  `Key` varchar(128) CHARACTER SET ascii NOT NULL,
  `WindowStart` datetime(6) NOT NULL,
  `Count` int(11) NOT NULL DEFAULT 1,
  PRIMARY KEY (`Key`),
  KEY `IX_RateLimit_Window` (`WindowStart`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

SELECT COUNT(*) AS rate_limit_rows FROM marketplace_rate_limits;
