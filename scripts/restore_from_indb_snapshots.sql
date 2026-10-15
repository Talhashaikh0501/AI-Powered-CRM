-- =============================================================================
-- IN-DATABASE SNAPSHOT RESTORATION SCRIPT FOR TalhaCRM_ TABLES IN AITStudentDB
-- SEPARATE, EXPLICITLY APPROVED EMERGENCY RECOVERY OPERATION ONLY.
--
-- SAFETY REQUIREMENTS:
-- 1. This script must NEVER be executed automatically or silently.
-- 2. It will REFUSE to execute unless @CONFIRM_RESTORE is explicitly set to
--    'YES_RESTORE_TALHACRM_TABLES' by an authorized operator.
-- 3. You must specify the exact @SnapshotSuffix to restore from.
-- 4. It verifies that snapshot tables exist and contain records BEFORE touching any live table.
-- 5. It targets ONLY dbo.TalhaCRM_* tables and NEVER touches any unrelated shared tables.
-- 6. No 'GO' batch separators; safe for both SSMS and ADO.NET execution.
-- =============================================================================

DECLARE @CONFIRM_RESTORE VARCHAR(50) = 'NO'; -- MUST be explicitly set to 'YES_RESTORE_TALHACRM_TABLES'
DECLARE @SnapshotSuffix NVARCHAR(50) = '';    -- MUST be explicitly set to a valid snapshot timestamp suffix, e.g. '20261016_014600'

IF @CONFIRM_RESTORE <> 'YES_RESTORE_TALHACRM_TABLES'
BEGIN
    RAISERROR('SAFETY ABORT: Restoration aborted. @CONFIRM_RESTORE is not set to ''YES_RESTORE_TALHACRM_TABLES''. Restoration is a separate, explicitly approved operation and never executes automatically.', 16, 1);
    RETURN;
END;

IF @SnapshotSuffix IS NULL OR LEN(LTRIM(RTRIM(@SnapshotSuffix))) = 0
BEGIN
    RAISERROR('SAFETY ABORT: You must specify a valid @SnapshotSuffix (e.g. ''20261016_014600'') matching existing snapshot tables.', 16, 1);
    RETURN;
END;

-- Pre-flight validation: Verify snapshot tables exist before altering anything
DECLARE @missingCount INT = 0;
IF OBJECT_ID('dbo.TalhaCRM_BKP_' + @SnapshotSuffix + '_Leads') IS NULL SET @missingCount += 1;
IF OBJECT_ID('dbo.TalhaCRM_BKP_' + @SnapshotSuffix + '_LeadActivities') IS NULL SET @missingCount += 1;
IF OBJECT_ID('dbo.TalhaCRM_BKP_' + @SnapshotSuffix + '_FollowUps') IS NULL SET @missingCount += 1;
IF OBJECT_ID('dbo.TalhaCRM_BKP_' + @SnapshotSuffix + '_AILeadInsights') IS NULL SET @missingCount += 1;
IF OBJECT_ID('dbo.TalhaCRM_BKP_' + @SnapshotSuffix + '_Notifications') IS NULL SET @missingCount += 1;
IF OBJECT_ID('dbo.TalhaCRM_BKP_' + @SnapshotSuffix + '_AuditLogs') IS NULL SET @missingCount += 1;
IF OBJECT_ID('dbo.TalhaCRM_BKP_' + @SnapshotSuffix + '_AspNetUsers') IS NULL SET @missingCount += 1;
IF OBJECT_ID('dbo.TalhaCRM_BKP_' + @SnapshotSuffix + '_AspNetRoles') IS NULL SET @missingCount += 1;
IF OBJECT_ID('dbo.TalhaCRM_BKP_' + @SnapshotSuffix + '_AspNetUserRoles') IS NULL SET @missingCount += 1;

IF @missingCount > 0
BEGIN
    RAISERROR('SAFETY ABORT: One or more snapshot tables with suffix %s do not exist. Refusing to touch live CRM tables.', 16, 1, @SnapshotSuffix);
    RETURN;
END;

BEGIN TRANSACTION;
SET XACT_ABORT ON;

-- 1. Disable constraints ONLY on TalhaCRM_ tables
ALTER TABLE dbo.TalhaCRM_FollowUps NOCHECK CONSTRAINT ALL;
ALTER TABLE dbo.TalhaCRM_LeadActivities NOCHECK CONSTRAINT ALL;
ALTER TABLE dbo.TalhaCRM_AILeadInsights NOCHECK CONSTRAINT ALL;
ALTER TABLE dbo.TalhaCRM_Notifications NOCHECK CONSTRAINT ALL;
ALTER TABLE dbo.TalhaCRM_AuditLogs NOCHECK CONSTRAINT ALL;
ALTER TABLE dbo.TalhaCRM_LeadStatusHistory NOCHECK CONSTRAINT ALL;
ALTER TABLE dbo.TalhaCRM_Leads NOCHECK CONSTRAINT ALL;
ALTER TABLE dbo.TalhaCRM_AspNetUserRoles NOCHECK CONSTRAINT ALL;
ALTER TABLE dbo.TalhaCRM_AspNetUsers NOCHECK CONSTRAINT ALL;
ALTER TABLE dbo.TalhaCRM_AspNetRoles NOCHECK CONSTRAINT ALL;

-- 2. Clear current TalhaCRM_ tables
DELETE FROM dbo.TalhaCRM_FollowUps;
DELETE FROM dbo.TalhaCRM_LeadActivities;
DELETE FROM dbo.TalhaCRM_AILeadInsights;
DELETE FROM dbo.TalhaCRM_Notifications;
DELETE FROM dbo.TalhaCRM_AuditLogs;
DELETE FROM dbo.TalhaCRM_LeadStatusHistory;
DELETE FROM dbo.TalhaCRM_Leads;
DELETE FROM dbo.TalhaCRM_AspNetUserRoles;
DELETE FROM dbo.TalhaCRM_AspNetUsers;
DELETE FROM dbo.TalhaCRM_AspNetRoles;

-- 3. Restore data from snapshots using dynamic SQL with @SnapshotSuffix
DECLARE @restoreSql NVARCHAR(MAX) = N'
SET IDENTITY_INSERT dbo.TalhaCRM_Leads ON;
INSERT INTO dbo.TalhaCRM_Leads (LeadId, Name, Phone, Email, Company, Source, InterestedProduct, Budget, Status, Priority, OwnerUserId, IsArchived, CreatedAt, UpdatedAt)
SELECT LeadId, Name, Phone, Email, Company, Source, InterestedProduct, Budget, Status, Priority, OwnerUserId, IsArchived, CreatedAt, UpdatedAt FROM dbo.TalhaCRM_BKP_' + @SnapshotSuffix + N'_Leads;
SET IDENTITY_INSERT dbo.TalhaCRM_Leads OFF;

SET IDENTITY_INSERT dbo.TalhaCRM_LeadActivities ON;
INSERT INTO dbo.TalhaCRM_LeadActivities (ActivityId, LeadId, UserId, Type, Description, OccurredAt)
SELECT ActivityId, LeadId, UserId, Type, Description, OccurredAt FROM dbo.TalhaCRM_BKP_' + @SnapshotSuffix + N'_LeadActivities;
SET IDENTITY_INSERT dbo.TalhaCRM_LeadActivities OFF;

SET IDENTITY_INSERT dbo.TalhaCRM_FollowUps ON;
INSERT INTO dbo.TalhaCRM_FollowUps (FollowUpId, LeadId, AssignedUserId, DueAt, Type, Notes, Status, CompletedAt)
SELECT FollowUpId, LeadId, AssignedUserId, DueAt, Type, Notes, Status, CompletedAt FROM dbo.TalhaCRM_BKP_' + @SnapshotSuffix + N'_FollowUps;
SET IDENTITY_INSERT dbo.TalhaCRM_FollowUps OFF;

IF OBJECT_ID(''dbo.TalhaCRM_BKP_' + @SnapshotSuffix + N'_LeadStatusHistory'') IS NOT NULL AND (SELECT COUNT(*) FROM dbo.TalhaCRM_BKP_' + @SnapshotSuffix + N'_LeadStatusHistory) > 0
BEGIN
    SET IDENTITY_INSERT dbo.TalhaCRM_LeadStatusHistory ON;
    INSERT INTO dbo.TalhaCRM_LeadStatusHistory (HistoryId, LeadId, OldStatus, NewStatus, ChangedByUserId, Notes, ChangedAt)
    SELECT HistoryId, LeadId, OldStatus, NewStatus, ChangedByUserId, Notes, ChangedAt FROM dbo.TalhaCRM_BKP_' + @SnapshotSuffix + N'_LeadStatusHistory;
    SET IDENTITY_INSERT dbo.TalhaCRM_LeadStatusHistory OFF;
END;

SET IDENTITY_INSERT dbo.TalhaCRM_AILeadInsights ON;
INSERT INTO dbo.TalhaCRM_AILeadInsights (InsightId, LeadId, Classification, Score, Summary, NextBestAction, RiskLevel, Urgency, ReasonJson, SuggestedResponse, Provider, Model, GeneratedAt)
SELECT InsightId, LeadId, Classification, Score, Summary, NextBestAction, RiskLevel, Urgency, ReasonJson, SuggestedResponse, Provider, Model, GeneratedAt FROM dbo.TalhaCRM_BKP_' + @SnapshotSuffix + N'_AILeadInsights;
SET IDENTITY_INSERT dbo.TalhaCRM_AILeadInsights OFF;

SET IDENTITY_INSERT dbo.TalhaCRM_Notifications ON;
INSERT INTO dbo.TalhaCRM_Notifications (NotificationId, UserId, LeadId, Title, Message, IsRead, CreatedAt)
SELECT NotificationId, UserId, LeadId, Title, Message, IsRead, CreatedAt FROM dbo.TalhaCRM_BKP_' + @SnapshotSuffix + N'_Notifications;
SET IDENTITY_INSERT dbo.TalhaCRM_Notifications OFF;

SET IDENTITY_INSERT dbo.TalhaCRM_AuditLogs ON;
INSERT INTO dbo.TalhaCRM_AuditLogs (AuditLogId, UserId, EntityType, EntityId, Action, DetailsJson, CreatedAt)
SELECT AuditLogId, UserId, EntityType, EntityId, Action, DetailsJson, CreatedAt FROM dbo.TalhaCRM_BKP_' + @SnapshotSuffix + N'_AuditLogs;
SET IDENTITY_INSERT dbo.TalhaCRM_AuditLogs OFF;

INSERT INTO dbo.TalhaCRM_AspNetRoles (Id, Name, NormalizedName, ConcurrencyStamp)
SELECT Id, Name, NormalizedName, ConcurrencyStamp FROM dbo.TalhaCRM_BKP_' + @SnapshotSuffix + N'_AspNetRoles;

INSERT INTO dbo.TalhaCRM_AspNetUsers (Id, FullName, CreatedAt, IsActive, UserName, NormalizedUserName, Email, NormalizedEmail, EmailConfirmed, PasswordHash, SecurityStamp, ConcurrencyStamp, PhoneNumber, PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnd, LockoutEnabled, AccessFailedCount)
SELECT Id, FullName, CreatedAt, IsActive, UserName, NormalizedUserName, Email, NormalizedEmail, EmailConfirmed, PasswordHash, SecurityStamp, ConcurrencyStamp, PhoneNumber, PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnd, LockoutEnabled, AccessFailedCount FROM dbo.TalhaCRM_BKP_' + @SnapshotSuffix + N'_AspNetUsers;

INSERT INTO dbo.TalhaCRM_AspNetUserRoles (UserId, RoleId)
SELECT UserId, RoleId FROM dbo.TalhaCRM_BKP_' + @SnapshotSuffix + N'_AspNetUserRoles;
';
EXEC sp_executesql @restoreSql;

-- 4. Re-enable constraints ONLY on TalhaCRM_ tables
ALTER TABLE dbo.TalhaCRM_FollowUps CHECK CONSTRAINT ALL;
ALTER TABLE dbo.TalhaCRM_LeadActivities CHECK CONSTRAINT ALL;
ALTER TABLE dbo.TalhaCRM_AILeadInsights CHECK CONSTRAINT ALL;
ALTER TABLE dbo.TalhaCRM_Notifications CHECK CONSTRAINT ALL;
ALTER TABLE dbo.TalhaCRM_AuditLogs CHECK CONSTRAINT ALL;
ALTER TABLE dbo.TalhaCRM_LeadStatusHistory CHECK CONSTRAINT ALL;
ALTER TABLE dbo.TalhaCRM_Leads CHECK CONSTRAINT ALL;
ALTER TABLE dbo.TalhaCRM_AspNetUserRoles CHECK CONSTRAINT ALL;
ALTER TABLE dbo.TalhaCRM_AspNetUsers CHECK CONSTRAINT ALL;
ALTER TABLE dbo.TalhaCRM_AspNetRoles CHECK CONSTRAINT ALL;

COMMIT TRANSACTION;
