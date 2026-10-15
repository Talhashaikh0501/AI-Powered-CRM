-- =============================================================================
-- IN-DATABASE SNAPSHOT BACKUP FOR TalhaCRM_ TABLES IN AITStudentDB
-- Run before applying Phase2MultiTenantCore migration
-- Creates timestamped, immutable backup tables: [<schema>].[TalhaCRM_BKP_<timestamp>_<table_name>]
-- Existing backups are NEVER dropped or overwritten.
-- Schema-agnostic: Dynamically resolves current user schema (AITStudent / dbo).
-- Compatible with SqlCommand.ExecuteNonQuery() (NO 'GO' batch separators).
--
-- NOTE: Snapshots are initialized with Status = 'PendingVerification'.
-- They are only marked as 'Verified' after row-count verification succeeds.
-- =============================================================================

BEGIN TRANSACTION;
SET XACT_ABORT ON;

-- 1. Determine current schema and generate unique timestamp suffix: YYYYMMDD_HHMMSS
DECLARE @schema NVARCHAR(128) = SCHEMA_NAME();
DECLARE @snapshotSuffix NVARCHAR(30) = FORMAT(GETUTCDATE(), 'yyyyMMdd_HHmmss');

-- 2. Verify that no table with this snapshot suffix already exists (fail-closed protection)
DECLARE @conflictCount INT = 0;
SELECT @conflictCount = COUNT(*) 
FROM sys.tables 
WHERE schema_id = SCHEMA_ID(@schema) AND name LIKE 'TalhaCRM_BKP_' + @snapshotSuffix + '_%';

IF @conflictCount > 0
BEGIN
    RAISERROR('Snapshot tables with suffix %s already exist! Accidental overwriting is blocked.', 16, 1, @snapshotSuffix);
    ROLLBACK TRANSACTION;
    RETURN;
END;

-- 3. Execute byte-accurate in-database snapshots without dropping previous backups
DECLARE @sql NVARCHAR(MAX) = N'
SELECT * INTO [' + @schema + N'].[TalhaCRM_BKP_' + @snapshotSuffix + N'_Leads] FROM [' + @schema + N'].[TalhaCRM_Leads];
SELECT * INTO [' + @schema + N'].[TalhaCRM_BKP_' + @snapshotSuffix + N'_LeadActivities] FROM [' + @schema + N'].[TalhaCRM_LeadActivities];
SELECT * INTO [' + @schema + N'].[TalhaCRM_BKP_' + @snapshotSuffix + N'_FollowUps] FROM [' + @schema + N'].[TalhaCRM_FollowUps];
SELECT * INTO [' + @schema + N'].[TalhaCRM_BKP_' + @snapshotSuffix + N'_LeadStatusHistory] FROM [' + @schema + N'].[TalhaCRM_LeadStatusHistory];
SELECT * INTO [' + @schema + N'].[TalhaCRM_BKP_' + @snapshotSuffix + N'_AILeadInsights] FROM [' + @schema + N'].[TalhaCRM_AILeadInsights];
SELECT * INTO [' + @schema + N'].[TalhaCRM_BKP_' + @snapshotSuffix + N'_Notifications] FROM [' + @schema + N'].[TalhaCRM_Notifications];
SELECT * INTO [' + @schema + N'].[TalhaCRM_BKP_' + @snapshotSuffix + N'_AuditLogs] FROM [' + @schema + N'].[TalhaCRM_AuditLogs];
SELECT * INTO [' + @schema + N'].[TalhaCRM_BKP_' + @snapshotSuffix + N'_AspNetUsers] FROM [' + @schema + N'].[TalhaCRM_AspNetUsers];
SELECT * INTO [' + @schema + N'].[TalhaCRM_BKP_' + @snapshotSuffix + N'_AspNetRoles] FROM [' + @schema + N'].[TalhaCRM_AspNetRoles];
SELECT * INTO [' + @schema + N'].[TalhaCRM_BKP_' + @snapshotSuffix + N'_AspNetUserRoles] FROM [' + @schema + N'].[TalhaCRM_AspNetUserRoles];
';

IF OBJECT_ID(QUOTENAME(@schema) + '.[TalhaCRM_EFMigrationsHistory]') IS NOT NULL
BEGIN
    SET @sql += N'SELECT * INTO [' + @schema + N'].[TalhaCRM_BKP_' + @snapshotSuffix + N'_EFMigrationsHistory] FROM [' + @schema + N'].[TalhaCRM_EFMigrationsHistory]; ';
END
ELSE IF OBJECT_ID(QUOTENAME(@schema) + '.[__EFMigrationsHistory]') IS NOT NULL
BEGIN
    SET @sql += N'SELECT * INTO [' + @schema + N'].[TalhaCRM_BKP_' + @snapshotSuffix + N'_EFMigrationsHistory] FROM [' + @schema + N'].[__EFMigrationsHistory]; ';
END;

EXEC sp_executesql @sql;

-- 4. Record snapshot creation in a dedicated metadata audit log table with Status = 'PendingVerification'
IF OBJECT_ID(QUOTENAME(@schema) + '.[TalhaCRM_BKP_SnapshotMetadata]') IS NULL
BEGIN
    DECLARE @createMetaSql NVARCHAR(MAX) = N'
    CREATE TABLE [' + @schema + N'].[TalhaCRM_BKP_SnapshotMetadata] (
        SnapshotId INT IDENTITY(1,1) PRIMARY KEY,
        SnapshotSuffix NVARCHAR(50) NOT NULL,
        CreatedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        Status NVARCHAR(50) NOT NULL DEFAULT ''PendingVerification''
    );';
    EXEC sp_executesql @createMetaSql;
END;

DECLARE @insertMetaSql NVARCHAR(MAX) = N'
INSERT INTO [' + @schema + N'].[TalhaCRM_BKP_SnapshotMetadata] (SnapshotSuffix, CreatedAtUtc, Status)
VALUES (@suffix, SYSUTCDATETIME(), ''PendingVerification'');';

EXEC sp_executesql @insertMetaSql, N'@suffix NVARCHAR(50)', @suffix = @snapshotSuffix;

COMMIT TRANSACTION;

-- Return created snapshot suffix to caller
SELECT @snapshotSuffix AS CreatedSnapshotSuffix;
