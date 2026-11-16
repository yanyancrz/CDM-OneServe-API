-- Apply before deploying the API/frontend changes. Existing appointments/history are untouched.
CREATE TABLE IF NOT EXISTS `guidance_reschedule_proposals` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `AppointmentId` int NOT NULL,
    `ProposedBy` int NOT NULL,
    `ProposedDate` date NOT NULL,
    `ProposedTimeSlot` varchar(20) NOT NULL,
    `OriginalDate` date NOT NULL,
    `OriginalTimeSlot` varchar(20) NOT NULL,
    `Reason` varchar(500) NOT NULL,
    `Instructions` varchar(1000) NULL,
    `Status` varchar(20) NOT NULL DEFAULT 'Pending',
    `RespondedBy` int NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `RespondedAt` datetime(6) NULL,
    `PendingAppointmentId` int GENERATED ALWAYS AS
        (CASE WHEN `Status` = 'Pending' THEN `AppointmentId` ELSE NULL END) STORED,
    PRIMARY KEY (`Id`),
    UNIQUE KEY `uq_guidance_pending_reschedule` (`PendingAppointmentId`),
    KEY `ix_guidance_proposal_appointment` (`AppointmentId`, `CreatedAt`),
    CONSTRAINT `fk_guidance_proposal_appointment` FOREIGN KEY (`AppointmentId`)
        REFERENCES `guidance_appointments` (`Id`) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
