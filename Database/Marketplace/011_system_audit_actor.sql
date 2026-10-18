-- =====================================================================
-- CDM OneServe Marketplace - allow system-initiated stock movements
-- 011_system_audit_actor.sql
--
-- WHY THIS FILE EXISTS
--   010_order_lifecycle.sql added a background job that expires orders nobody
--   collected and releases their stock. That release must be audited like any
--   other stock movement, but there is no human to attribute it to: it happens
--   on a timer, possibly weeks after the order was placed.
--
--   marketplace_inventory_transactions.ActorUserId is NOT NULL with a foreign
--   key to users(Id), so the first attempt wrote 0 and failed:
--
--       Cannot add or update a child row: a foreign key constraint fails
--       (`cdm_oneserve`.`marketplace_inventory_transactions`,
--        CONSTRAINT `FK_InvTxn_Actor` ... REFERENCES `users` (`Id`))
--
--   because there is no user with Id 0.
--
-- THE FIX
--   ActorUserId becomes nullable. A stock movement is a fact about the shelf,
--   and the shelf does not care whether a person or a schedule caused it. The
--   row keeps its WorkspaceId, OrderId, quantity change and ActionType, so
--   "what moved and why" is still fully answerable - only "who" is now allowed
--   to be "nobody", which is what a scheduled expiry really is.
--
--   Nothing is lost for human actions: every existing row has a real ActorUserId
--   and keeps it. Only new system-initiated movements can leave it NULL.
--
-- Safe to re-run. Run on `cdm_oneserve`.
-- =====================================================================

USE `cdm_oneserve`;

SET @has_nullable := (
    SELECT COUNT(*) FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'marketplace_inventory_transactions'
      AND COLUMN_NAME = 'ActorUserId'
      AND IS_NULLABLE = 'YES'
);

SET @ddl := IF(@has_nullable = 0,
    'ALTER TABLE `marketplace_inventory_transactions`
        MODIFY COLUMN `ActorUserId` int(11) NULL DEFAULT NULL',
    'SELECT "inventory_transactions.ActorUserId already nullable" AS note');
PREPARE stmt FROM @ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- ---------------------------------------------------------------------
-- Confirm no existing row lost its actor
-- ---------------------------------------------------------------------

SELECT COUNT(*) AS total_rows,
       SUM(`ActorUserId` IS NOT NULL) AS with_actor,
       SUM(`ActorUserId` IS NULL) AS without_actor
FROM `marketplace_inventory_transactions`;
