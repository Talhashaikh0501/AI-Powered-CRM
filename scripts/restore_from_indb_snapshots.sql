-- =============================================================================
-- IN-DATABASE SNAPSHOT RESTORATION SCRIPT FOR TalhaCRM_ TABLES IN AITStudentDB
-- Run to completely restore TalhaCRM_ tables from pre-migration snapshots
-- =============================================================================

BEGIN TRANSACTION;
SET XACT_ABORT ON;

-- 1. Disable constraints
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

-- 2. Clear current tables
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

-- 3. Restore data from snapshots
SET IDENTITY_INSERT dbo.TalhaCRM_Leads ON;
INSERT INTO dbo.TalhaCRM_Leads (LeadId, Name, Phone, Email, Company, Source, InterestedProduct, Budget, Status, Priority, OwnerUserId, IsArchived, CreatedAt, UpdatedAt)
SELECT LeadId, Name, Phone, Email, Company, Source, InterestedProduct, Budget, Status, Priority, OwnerUserId, IsArchived, CreatedAt, UpdatedAt FROM dbo.TalhaCRM_BKP_Leads;
SET IDENTITY_INSERT dbo.TalhaCRM_Leads OFF;

SET IDENTITY_INSERT dbo.TalhaCRM_LeadActivities ON;
INSERT INTO dbo.TalhaCRM_LeadActivities (ActivityId, LeadId, UserId, Type, Description, OccurredAt)
SELECT ActivityId, LeadId, UserId, Type, Description, OccurredAt FROM dbo.TalhaCRM_BKP_LeadActivities;
SET IDENTITY_INSERT dbo.TalhaCRM_LeadActivities OFF;

SET IDENTITY_INSERT dbo.TalhaCRM_FollowUps ON;
INSERT INTO dbo.TalhaCRM_FollowUps (FollowUpId, LeadId, AssignedUserId, DueAt, Type, Notes, Status, CompletedAt)
SELECT FollowUpId, LeadId, AssignedUserId, DueAt, Type, Notes, Status, CompletedAt FROM dbo.TalhaCRM_BKP_FollowUps;
SET IDENTITY_INSERT dbo.TalhaCRM_FollowUps OFF;

IF OBJECT_ID('dbo.TalhaCRM_BKP_LeadStatusHistory') IS NOT NULL AND (SELECT COUNT(*) FROM dbo.TalhaCRM_BKP_LeadStatusHistory) > 0
BEGIN
    SET IDENTITY_INSERT dbo.TalhaCRM_LeadStatusHistory ON;
    INSERT INTO dbo.TalhaCRM_LeadStatusHistory (HistoryId, LeadId, OldStatus, NewStatus, ChangedByUserId, Notes, ChangedAt)
    SELECT HistoryId, LeadId, OldStatus, NewStatus, ChangedByUserId, Notes, ChangedAt FROM dbo.TalhaCRM_BKP_LeadStatusHistory;
    SET IDENTITY_INSERT dbo.TalhaCRM_LeadStatusHistory OFF;
END;

SET IDENTITY_INSERT dbo.TalhaCRM_AILeadInsights ON;
INSERT INTO dbo.TalhaCRM_AILeadInsights (InsightId, LeadId, Classification, Score, Summary, NextBestAction, RiskLevel, Urgency, ReasonJson, SuggestedResponse, Provider, Model, GeneratedAt)
SELECT InsightId, LeadId, Classification, Score, Summary, NextBestAction, RiskLevel, Urgency, ReasonJson, SuggestedResponse, Provider, Model, GeneratedAt FROM dbo.TalhaCRM_BKP_AILeadInsights;
SET IDENTITY_INSERT dbo.TalhaCRM_AILeadInsights OFF;

SET IDENTITY_INSERT dbo.TalhaCRM_Notifications ON;
INSERT INTO dbo.TalhaCRM_Notifications (NotificationId, UserId, LeadId, Title, Message, IsRead, CreatedAt)
SELECT NotificationId, UserId, LeadId, Title, Message, IsRead, CreatedAt FROM dbo.TalhaCRM_BKP_Notifications;
SET IDENTITY_INSERT dbo.TalhaCRM_Notifications OFF;

SET IDENTITY_INSERT dbo.TalhaCRM_AuditLogs ON;
INSERT INTO dbo.TalhaCRM_AuditLogs (AuditLogId, UserId, EntityType, EntityId, Action, DetailsJson, CreatedAt)
SELECT AuditLogId, UserId, EntityType, EntityId, Action, DetailsJson, CreatedAt FROM dbo.TalhaCRM_BKP_AuditLogs;
SET IDENTITY_INSERT dbo.TalhaCRM_AuditLogs OFF;

INSERT INTO dbo.TalhaCRM_AspNetRoles (Id, Name, NormalizedName, ConcurrencyStamp)
SELECT Id, Name, NormalizedName, ConcurrencyStamp FROM dbo.TalhaCRM_BKP_AspNetRoles;

INSERT INTO dbo.TalhaCRM_AspNetUsers (Id, FullName, CreatedAt, IsActive, UserName, NormalizedUserName, Email, NormalizedEmail, EmailConfirmed, PasswordHash, SecurityStamp, ConcurrencyStamp, PhoneNumber, PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnd, LockoutEnabled, AccessFailedCount)
SELECT Id, FullName, CreatedAt, IsActive, UserName, NormalizedUserName, Email, NormalizedEmail, EmailConfirmed, PasswordHash, SecurityStamp, ConcurrencyStamp, PhoneNumber, PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnd, LockoutEnabled, AccessFailedCount FROM dbo.TalhaCRM_BKP_AspNetUsers;

INSERT INTO dbo.TalhaCRM_AspNetUserRoles (UserId, RoleId)
SELECT UserId, RoleId FROM dbo.TalhaCRM_BKP_AspNetUserRoles;

-- 4. Re-enable constraints
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
GO
