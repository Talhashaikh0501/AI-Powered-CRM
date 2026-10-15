-- =============================================================================
-- IN-DATABASE SNAPSHOT BACKUP FOR TalhaCRM_ TABLES IN AITStudentDB
-- Run before applying Phase2MultiTenantCore migration
-- Creates timestamped, immutable backup tables: dbo.TalhaCRM_BKP_<timestamp>_<table_name>
-- Existing backups are NEVER dropped or overwritten.
-- Compatible with SqlCommand.ExecuteNonQuery() (NO 'GO' batch separators).
--
-- NOTE: Snapshots are initialized with Status = 'PendingVerification'.
-- They are only marked as 'Verified' after row-count verification succeeds.
-- =============================================================================

BEGIN TRANSACTION;
SET XACT_ABORT ON;

-- 1. Generate unique timestamp suffix: YYYYMMDD_HHMMSS
DECLARE @snapshotSuffix NVARCHAR(30) = FORMAT(GETUTCDATE(), 'yyyyMMdd_HHmmss');

-- 2. Verify that no table with this snapshot suffix already exists (fail-closed protection)
DECLARE @conflictCount INT = 0;
SELECT @conflictCount = COUNT(*) 
FROM sys.tables 
WHERE name LIKE 'TalhaCRM_BKP_' + @snapshotSuffix + '_%';

IF @conflictCount > 0
BEGIN
    RAISERROR('Snapshot tables with suffix %s already exist! Accidental overwriting is blocked.', 16, 1, @snapshotSuffix);
    ROLLBACK TRANSACTION;
    RETURN;
END;

-- 3. Execute byte-accurate in-database snapshots without dropping previous backups
DECLARE @sql NVARCHAR(MAX) = N'
SELECT * INTO dbo.TalhaCRM_BKP_' + @snapshotSuffix + N'_Leads FROM dbo.TalhaCRM_Leads;
SELECT * INTO dbo.TalhaCRM_BKP_' + @snapshotSuffix + N'_LeadActivities FROM dbo.TalhaCRM_LeadActivities;
SELECT * INTO dbo.TalhaCRM_BKP_' + @snapshotSuffix + N'_FollowUps FROM dbo.TalhaCRM_FollowUps;
SELECT * INTO dbo.TalhaCRM_BKP_' + @snapshotSuffix + N'_LeadStatusHistory FROM dbo.TalhaCRM_LeadStatusHistory;
SELECT * INTO dbo.TalhaCRM_BKP_' + @snapshotSuffix + N'_AILeadInsights FROM dbo.TalhaCRM_AILeadInsights;
SELECT * INTO dbo.TalhaCRM_BKP_' + @snapshotSuffix + N'_Notifications FROM dbo.TalhaCRM_Notifications;
SELECT * INTO dbo.TalhaCRM_BKP_' + @snapshotSuffix + N'_AuditLogs FROM dbo.TalhaCRM_AuditLogs;
SELECT * INTO dbo.TalhaCRM_BKP_' + @snapshotSuffix + N'_AspNetUsers FROM dbo.TalhaCRM_AspNetUsers;
SELECT * INTO dbo.TalhaCRM_BKP_' + @snapshotSuffix + N'_AspNetRoles FROM dbo.TalhaCRM_AspNetRoles;
SELECT * INTO dbo.TalhaCRM_BKP_' + @snapshotSuffix + N'_AspNetUserRoles FROM dbo.TalhaCRM_AspNetUserRoles;
';

IF OBJECT_ID('dbo.TalhaCRM_EFMigrationsHistory') IS NOT NULL
BEGIN
    SET @sql += N'SELECT * INTO dbo.TalhaCRM_BKP_' + @snapshotSuffix + N'_EFMigrationsHistory FROM dbo.TalhaCRM_EFMigrationsHistory; ';
END
ELSE IF OBJECT_ID('dbo.__EFMigrationsHistory') IS NOT NULL
BEGIN
    SET @sql += N'SELECT * INTO dbo.TalhaCRM_BKP_' + @snapshotSuffix + N'_EFMigrationsHistory FROM dbo.__EFMigrationsHistory; ';
END;

EXEC sp_executesql @sql;

-- 4. Record snapshot creation in a dedicated metadata audit log table with Status = 'PendingVerification'
IF OBJECT_ID('dbo.TalhaCRM_BKP_SnapshotMetadata') IS NULL
BEGIN
    CREATE TABLE dbo.TalhaCRM_BKP_SnapshotMetadata (
        SnapshotId INT IDENTITY(1,1) PRIMARY KEY,
        SnapshotSuffix NVARCHAR(50) NOT NULL,
        CreatedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        Status NVARCHAR(50) NOT NULL DEFAULT 'PendingVerification'
    );
END;

INSERT INTO dbo.TalhaCRM_BKP_SnapshotMetadata (SnapshotSuffix, CreatedAtUtc, Status)
VALUES (@snapshotSuffix, SYSUTCDATETIME(), 'PendingVerification');

COMMIT TRANSACTION;

-- Return created snapshot suffix to caller
SELECT @snapshotSuffix AS CreatedSnapshotSuffix;
