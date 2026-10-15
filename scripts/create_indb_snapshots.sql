-- =============================================================================
-- IN-DATABASE SNAPSHOT BACKUP FOR TalhaCRM_ TABLES IN AITStudentDB
-- Run before applying Phase2MultiTenantCore migration
-- =============================================================================

BEGIN TRANSACTION;
SET XACT_ABORT ON;

-- 1. Drop existing snapshot tables if previously created
IF OBJECT_ID('dbo.TalhaCRM_BKP_Leads') IS NOT NULL DROP TABLE dbo.TalhaCRM_BKP_Leads;
IF OBJECT_ID('dbo.TalhaCRM_BKP_LeadActivities') IS NOT NULL DROP TABLE dbo.TalhaCRM_BKP_LeadActivities;
IF OBJECT_ID('dbo.TalhaCRM_BKP_FollowUps') IS NOT NULL DROP TABLE dbo.TalhaCRM_BKP_FollowUps;
IF OBJECT_ID('dbo.TalhaCRM_BKP_LeadStatusHistory') IS NOT NULL DROP TABLE dbo.TalhaCRM_BKP_LeadStatusHistory;
IF OBJECT_ID('dbo.TalhaCRM_BKP_AILeadInsights') IS NOT NULL DROP TABLE dbo.TalhaCRM_BKP_AILeadInsights;
IF OBJECT_ID('dbo.TalhaCRM_BKP_Notifications') IS NOT NULL DROP TABLE dbo.TalhaCRM_BKP_Notifications;
IF OBJECT_ID('dbo.TalhaCRM_BKP_AuditLogs') IS NOT NULL DROP TABLE dbo.TalhaCRM_BKP_AuditLogs;
IF OBJECT_ID('dbo.TalhaCRM_BKP_AspNetUsers') IS NOT NULL DROP TABLE dbo.TalhaCRM_BKP_AspNetUsers;
IF OBJECT_ID('dbo.TalhaCRM_BKP_AspNetRoles') IS NOT NULL DROP TABLE dbo.TalhaCRM_BKP_AspNetRoles;
IF OBJECT_ID('dbo.TalhaCRM_BKP_AspNetUserRoles') IS NOT NULL DROP TABLE dbo.TalhaCRM_BKP_AspNetUserRoles;
IF OBJECT_ID('dbo.TalhaCRM_BKP_EFMigrationsHistory') IS NOT NULL DROP TABLE dbo.TalhaCRM_BKP_EFMigrationsHistory;

-- 2. Create raw byte-accurate in-database snapshots
SELECT * INTO dbo.TalhaCRM_BKP_Leads FROM dbo.TalhaCRM_Leads;
SELECT * INTO dbo.TalhaCRM_BKP_LeadActivities FROM dbo.TalhaCRM_LeadActivities;
SELECT * INTO dbo.TalhaCRM_BKP_FollowUps FROM dbo.TalhaCRM_FollowUps;
SELECT * INTO dbo.TalhaCRM_BKP_LeadStatusHistory FROM dbo.TalhaCRM_LeadStatusHistory;
SELECT * INTO dbo.TalhaCRM_BKP_AILeadInsights FROM dbo.TalhaCRM_AILeadInsights;
SELECT * INTO dbo.TalhaCRM_BKP_Notifications FROM dbo.TalhaCRM_Notifications;
SELECT * INTO dbo.TalhaCRM_BKP_AuditLogs FROM dbo.TalhaCRM_AuditLogs;
SELECT * INTO dbo.TalhaCRM_BKP_AspNetUsers FROM dbo.TalhaCRM_AspNetUsers;
SELECT * INTO dbo.TalhaCRM_BKP_AspNetRoles FROM dbo.TalhaCRM_AspNetRoles;
SELECT * INTO dbo.TalhaCRM_BKP_AspNetUserRoles FROM dbo.TalhaCRM_AspNetUserRoles;
SELECT * INTO dbo.TalhaCRM_BKP_EFMigrationsHistory FROM dbo.TalhaCRM_EFMigrationsHistory;

COMMIT TRANSACTION;
GO
