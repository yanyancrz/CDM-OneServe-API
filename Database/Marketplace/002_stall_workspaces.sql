-- =====================================================================
-- CDM OneServe Marketplace - per-stall accounts + workspaces
-- 002_stall_workspaces.sql
--
-- ARCHITECTURE (per approved decision):
--   EVERY STALL HAS ITS OWN LOGIN ACCOUNT. After signing in with the
--   stall account, the human on duty enters their OPERATOR NAME once.
--   That name - never a per-request client value - is recorded from the
--   server-validated operator session on every inventory update.
--
--   * marketplace_stall_locations : FoodHub locations (e.g. "Main FoodHub").
--   * marketplace_workspaces      : one row per stall + the BusinessHub
--                                   workspace. All products, inventory (via
--                                   product) and orders are scoped to one.
--   * marketplace_stall_accounts  : WorkspaceId <-> users.Id link. The user
--                                   row uses Role = 'MarketplaceStaff', so it
--                                   can never buy (buyer endpoints only allow
--                                   Student/Faculty) and can only operate.
--   * marketplace_operator_sessions : the validated session holding the
--                                   entered operator name. Mutations require
--                                   it; reads only need the login.
--   * marketplace_inventory_transactions : append-only audit of every stock
--                                   movement, with actor + operator name.
--
-- Legacy rows (created before workspaces existed) are assigned once:
--   Food products        -> Stall 1 of the seeded FoodHub location.
--   Uniforms/Merchandise -> the BusinessHub workspace.
--   Orders               -> the workspace of their first item.
-- Run on the `cdm_oneserve` database. Safe to re-run.
-- =====================================================================

USE `cdm_oneserve`;

-- RequestKey: the buyer's original checkout key, shared by every order one
-- checkout produced (one order per workspace). Lets a retried checkout with
-- an emptied cart return the original group instead of failing.
SET @has_rk := (
    SELECT COUNT(*) FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'marketplace_orders'
      AND COLUMN_NAME = 'RequestKey'
);
SET @ddl := IF(@has_rk = 0,
    'ALTER TABLE `marketplace_orders` ADD COLUMN `RequestKey` char(36) NULL AFTER `IdempotencyKey`, ADD KEY `IX_Order_RequestKey` (`UserId`, `RequestKey`)',
    'SELECT "orders.RequestKey already present" AS note');
PREPARE stmt FROM @ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- ---------------------------------------------------------
-- 1. FoodHub stall locations
-- ---------------------------------------------------------

CREATE TABLE IF NOT EXISTS `marketplace_stall_locations` (
  `StallLocationId` int(11) NOT NULL AUTO_INCREMENT,
  `Name` varchar(120) NOT NULL,
  `Description` varchar(500) NOT NULL DEFAULT '',
  `IsActive` tinyint(1) NOT NULL DEFAULT 1,
  `SortOrder` int(11) NOT NULL DEFAULT 0,
  `CreatedAt` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  PRIMARY KEY (`StallLocationId`),
  UNIQUE KEY `IX_StallLocation_Name` (`Name`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ---------------------------------------------------------
-- 2. Workspaces (stalls + BusinessHub)
-- ---------------------------------------------------------

CREATE TABLE IF NOT EXISTS `marketplace_workspaces` (
  `WorkspaceId` int(11) NOT NULL AUTO_INCREMENT,
  `WorkspaceType` varchar(20) NOT NULL DEFAULT 'Stall',
  `Name` varchar(120) NOT NULL,
  `StallLocationId` int(11) DEFAULT NULL,
  `IsActive` tinyint(1) NOT NULL DEFAULT 1,
  `SortOrder` int(11) NOT NULL DEFAULT 0,
  `CreatedAt` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  PRIMARY KEY (`WorkspaceId`),
  KEY `IX_Workspace_Location` (`StallLocationId`),
  KEY `IX_Workspace_Type_Active` (`WorkspaceType`, `IsActive`),
  CONSTRAINT `FK_Workspace_StallLocation`
    FOREIGN KEY (`StallLocationId`) REFERENCES `marketplace_stall_locations` (`StallLocationId`)
    ON DELETE SET NULL ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ---------------------------------------------------------
-- 3. Stall accounts (workspace <-> user link)
-- ---------------------------------------------------------

CREATE TABLE IF NOT EXISTS `marketplace_stall_accounts` (
  `WorkspaceId` int(11) NOT NULL,
  `UserId` int(11) NOT NULL,
  `AssignedBy` int(11) DEFAULT NULL,
  `AssignedAt` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `IsActive` tinyint(1) NOT NULL DEFAULT 1,
  PRIMARY KEY (`WorkspaceId`),
  UNIQUE KEY `IX_StallAccount_User` (`UserId`),
  CONSTRAINT `FK_StallAccount_Workspace`
    FOREIGN KEY (`WorkspaceId`) REFERENCES `marketplace_workspaces` (`WorkspaceId`)
    ON DELETE CASCADE ON UPDATE NO ACTION,
  CONSTRAINT `FK_StallAccount_User`
    FOREIGN KEY (`UserId`) REFERENCES `users` (`Id`)
    ON DELETE CASCADE ON UPDATE NO ACTION,
  CONSTRAINT `FK_StallAccount_Assigner`
    FOREIGN KEY (`AssignedBy`) REFERENCES `users` (`Id`)
    ON DELETE SET NULL ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ---------------------------------------------------------
-- 4. Operator sessions (validated operator-name context)
-- ---------------------------------------------------------

CREATE TABLE IF NOT EXISTS `marketplace_operator_sessions` (
  `SessionId` char(36) NOT NULL,
  `UserId` int(11) NOT NULL,
  `WorkspaceId` int(11) NOT NULL,
  `OperatorName` varchar(120) NOT NULL,
  `CreatedAt` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `ExpiresAt` datetime(6) NOT NULL,
  `EndedAt` datetime(6) DEFAULT NULL,
  PRIMARY KEY (`SessionId`),
  KEY `IX_OperatorSession_User` (`UserId`, `EndedAt`),
  KEY `IX_OperatorSession_Workspace` (`WorkspaceId`),
  CONSTRAINT `FK_OperatorSession_User`
    FOREIGN KEY (`UserId`) REFERENCES `users` (`Id`)
    ON DELETE CASCADE ON UPDATE NO ACTION,
  CONSTRAINT `FK_OperatorSession_Workspace`
    FOREIGN KEY (`WorkspaceId`) REFERENCES `marketplace_workspaces` (`WorkspaceId`)
    ON DELETE CASCADE ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ---------------------------------------------------------
-- 5. Inventory audit (append-only through app workflows)
-- ---------------------------------------------------------

CREATE TABLE IF NOT EXISTS `marketplace_inventory_transactions` (
  `TransactionId` int(11) NOT NULL AUTO_INCREMENT,
  `ProductId` int(11) DEFAULT NULL,
  `VariantId` int(11) DEFAULT NULL,
  `WorkspaceId` int(11) NOT NULL,
  `ActorUserId` int(11) NOT NULL,
  `OperatorName` varchar(120) DEFAULT NULL,
  `PreviousQuantity` int(11) NOT NULL,
  `QuantityChanged` int(11) NOT NULL,
  `NewQuantity` int(11) NOT NULL,
  `ActionType` varchar(32) NOT NULL,
  `Reason` varchar(500) DEFAULT NULL,
  `OrderId` int(11) DEFAULT NULL,
  `CreatedAt` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  PRIMARY KEY (`TransactionId`),
  KEY `IX_InvTxn_Workspace_Created` (`WorkspaceId`, `CreatedAt`),
  KEY `IX_InvTxn_Product` (`ProductId`),
  KEY `IX_InvTxn_Order` (`OrderId`),
  KEY `IX_InvTxn_Actor` (`ActorUserId`),
  CONSTRAINT `FK_InvTxn_Product`
    FOREIGN KEY (`ProductId`) REFERENCES `marketplace_products` (`ProductId`)
    ON DELETE SET NULL ON UPDATE NO ACTION,
  CONSTRAINT `FK_InvTxn_Variant`
    FOREIGN KEY (`VariantId`) REFERENCES `marketplace_product_variants` (`VariantId`)
    ON DELETE SET NULL ON UPDATE NO ACTION,
  CONSTRAINT `FK_InvTxn_Workspace`
    FOREIGN KEY (`WorkspaceId`) REFERENCES `marketplace_workspaces` (`WorkspaceId`)
    ON DELETE CASCADE ON UPDATE NO ACTION,
  CONSTRAINT `FK_InvTxn_Actor`
    FOREIGN KEY (`ActorUserId`) REFERENCES `users` (`Id`)
    ON DELETE CASCADE ON UPDATE NO ACTION,
  CONSTRAINT `FK_InvTxn_Order`
    FOREIGN KEY (`OrderId`) REFERENCES `marketplace_orders` (`OrderId`)
    ON DELETE SET NULL ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ---------------------------------------------------------
-- 6. Workspace columns on products + orders (guarded ADDs:
--    MySQL has no ADD COLUMN IF NOT EXISTS)
-- ---------------------------------------------------------

SET @has_pws := (
    SELECT COUNT(*) FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'marketplace_products'
      AND COLUMN_NAME = 'WorkspaceId'
);
SET @ddl := IF(@has_pws = 0,
    'ALTER TABLE `marketplace_products` ADD COLUMN `WorkspaceId` int(11) NULL AFTER `SubCategory`, ADD KEY `IX_Product_Workspace` (`WorkspaceId`), ADD CONSTRAINT `FK_Product_Workspace` FOREIGN KEY (`WorkspaceId`) REFERENCES `marketplace_workspaces` (`WorkspaceId`) ON DELETE SET NULL ON UPDATE NO ACTION',
    'SELECT "products.WorkspaceId already present" AS note');
PREPARE stmt FROM @ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @has_ows := (
    SELECT COUNT(*) FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'marketplace_orders'
      AND COLUMN_NAME = 'WorkspaceId'
);
SET @ddl := IF(@has_ows = 0,
    'ALTER TABLE `marketplace_orders` ADD COLUMN `WorkspaceId` int(11) NULL AFTER `UserId`, ADD KEY `IX_Order_Workspace` (`WorkspaceId`), ADD CONSTRAINT `FK_Order_Workspace` FOREIGN KEY (`WorkspaceId`) REFERENCES `marketplace_workspaces` (`WorkspaceId`) ON DELETE SET NULL ON UPDATE NO ACTION',
    'SELECT "orders.WorkspaceId already present" AS note');
PREPARE stmt FROM @ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- ---------------------------------------------------------
-- 7. Seeds: locations, the BusinessHub, and the first stalls
--
--   Canteen                       → CDM BusinessHub
--   Old Building Lobby            → Main FoodHub
--   CDM Ground — Near the Canteen → Stall 1
-- ---------------------------------------------------------

INSERT INTO `marketplace_stall_locations` (`Name`, `Description`, `IsActive`, `SortOrder`)
SELECT 'Old Building Lobby', 'Food hall in the old building lobby.', 1, 0
FROM DUAL
WHERE NOT EXISTS (
    SELECT 1 FROM `marketplace_stall_locations` WHERE `Name` = 'Old Building Lobby'
);

INSERT INTO `marketplace_stall_locations` (`Name`, `Description`, `IsActive`, `SortOrder`)
SELECT 'Canteen', 'BusinessHub site at the campus canteen.', 1, 1
FROM DUAL
WHERE NOT EXISTS (
    SELECT 1 FROM `marketplace_stall_locations` WHERE `Name` = 'Canteen'
);

INSERT INTO `marketplace_stall_locations` (`Name`, `Description`, `IsActive`, `SortOrder`)
SELECT 'CDM Ground — Near the Canteen', 'Ground stalls near the canteen.', 1, 2
FROM DUAL
WHERE NOT EXISTS (
    SELECT 1 FROM `marketplace_stall_locations` WHERE `Name` = 'CDM Ground — Near the Canteen'
);

INSERT INTO `marketplace_workspaces` (`WorkspaceType`, `Name`, `StallLocationId`, `IsActive`, `SortOrder`)
SELECT 'BusinessHub', 'CDM BusinessHub', sl.`StallLocationId`, 1, 0
FROM `marketplace_stall_locations` sl
WHERE sl.`Name` = 'Canteen'
  AND NOT EXISTS (
      SELECT 1 FROM `marketplace_workspaces` WHERE `WorkspaceType` = 'BusinessHub' AND `Name` = 'CDM BusinessHub'
  );

INSERT INTO `marketplace_workspaces` (`WorkspaceType`, `Name`, `StallLocationId`, `IsActive`, `SortOrder`)
SELECT 'Stall', 'Main FoodHub', sl.`StallLocationId`, 1, 0
FROM `marketplace_stall_locations` sl
WHERE sl.`Name` = 'Old Building Lobby'
  AND NOT EXISTS (
      SELECT 1 FROM `marketplace_workspaces` w WHERE w.`WorkspaceType` = 'Stall' AND w.`Name` = 'Main FoodHub'
  );

INSERT INTO `marketplace_workspaces` (`WorkspaceType`, `Name`, `StallLocationId`, `IsActive`, `SortOrder`)
SELECT 'Stall', 'Stall 1', sl.`StallLocationId`, 1, 1
FROM `marketplace_stall_locations` sl
WHERE sl.`Name` = 'CDM Ground — Near the Canteen'
  AND NOT EXISTS (
      SELECT 1 FROM `marketplace_workspaces` w WHERE w.`WorkspaceType` = 'Stall' AND w.`Name` = 'Stall 1'
  );

-- ---------------------------------------------------------
-- 8. One-time legacy assignment (only touches NULL rows)
-- ---------------------------------------------------------

UPDATE `marketplace_products` p
JOIN `marketplace_workspaces` w
  ON w.`WorkspaceType` = 'Stall' AND w.`Name` = 'Stall 1'
SET p.`WorkspaceId` = w.`WorkspaceId`
WHERE p.`WorkspaceId` IS NULL AND p.`Category` = 'Food';

UPDATE `marketplace_products` p
JOIN `marketplace_workspaces` w
  ON w.`WorkspaceType` = 'BusinessHub' AND w.`Name` = 'CDM BusinessHub'
SET p.`WorkspaceId` = w.`WorkspaceId`
WHERE p.`WorkspaceId` IS NULL AND p.`Category` IN ('Uniforms', 'Merchandise');

UPDATE `marketplace_orders` o
LEFT JOIN (
    SELECT oi.`OrderId`, MIN(oi.`OrderItemId`) AS `FirstItem`
    FROM `marketplace_order_items` oi
    GROUP BY oi.`OrderId`
) f ON f.`OrderId` = o.`OrderId`
LEFT JOIN `marketplace_order_items` fi ON fi.`OrderItemId` = f.`FirstItem`
LEFT JOIN `marketplace_products` p ON p.`ProductId` = fi.`ProductId`
LEFT JOIN `marketplace_workspaces` w
  ON w.`WorkspaceType` = 'BusinessHub' AND w.`Name` = 'CDM BusinessHub'
SET o.`WorkspaceId` = COALESCE(p.`WorkspaceId`, w.`WorkspaceId`)
WHERE o.`WorkspaceId` IS NULL;

-- ---------------------------------------------------------
-- 9. BusinessHub site: the hub lives at the campus canteen.
--    (A BusinessHub MAY link a location for display; a Stall MUST.)
-- ---------------------------------------------------------

INSERT INTO `marketplace_stall_locations` (`Name`, `Description`, `IsActive`, `SortOrder`)
SELECT 'Canteen', 'BusinessHub site at the campus canteen.', 1, 1
FROM DUAL
WHERE NOT EXISTS (
    SELECT 1 FROM `marketplace_stall_locations` WHERE `Name` = 'Canteen'
);

UPDATE `marketplace_workspaces` w
JOIN `marketplace_stall_locations` l ON l.`Name` = 'Canteen'
SET w.`StallLocationId` = l.`StallLocationId`
WHERE w.`WorkspaceType` = 'BusinessHub'
  AND w.`Name` = 'CDM BusinessHub'
  AND w.`StallLocationId` IS NULL;

-- ---------------------------------------------------------
-- 10. Designated operator per stall account + ID suffix audit
--
-- The Head names the designated operator (name + last 3 ID digits) when
-- the stall login is created. The setup screen shows it with a reminder;
-- the session carries it into every audit row.
-- ---------------------------------------------------------

SET @has_sa_op := (
    SELECT COUNT(*) FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'marketplace_stall_accounts'
      AND COLUMN_NAME = 'OperatorName'
);
SET @ddl := IF(@has_sa_op = 0,
    'ALTER TABLE `marketplace_stall_accounts` ADD COLUMN `OperatorName` varchar(120) NULL AFTER `IsActive`, ADD COLUMN `IdLast3` char(3) NULL AFTER `OperatorName`',
    'SELECT "stall_accounts operator columns already present" AS note');
PREPARE stmt FROM @ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @has_ses_id := (
    SELECT COUNT(*) FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'marketplace_operator_sessions'
      AND COLUMN_NAME = 'IdLast3'
);
SET @ddl := IF(@has_ses_id = 0,
    'ALTER TABLE `marketplace_operator_sessions` ADD COLUMN `IdLast3` char(3) NULL AFTER `OperatorName`',
    'SELECT "sessions IdLast3 already present" AS note');
PREPARE stmt FROM @ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @has_tx_id := (
    SELECT COUNT(*) FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'marketplace_inventory_transactions'
      AND COLUMN_NAME = 'IdLast3'
);
SET @ddl := IF(@has_tx_id = 0,
    'ALTER TABLE `marketplace_inventory_transactions` ADD COLUMN `IdLast3` char(3) NULL AFTER `OperatorName`',
    'SELECT "transactions IdLast3 already present" AS note');
PREPARE stmt FROM @ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
