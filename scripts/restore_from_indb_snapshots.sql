-- =============================================================================
-- IN-DATABASE SNAPSHOT RESTORATION SCRIPT FOR TalhaCRM_ TABLES IN AITStudentDB
-- SEPARATE, EXPLICITLY APPROVED EMERGENCY RECOVERY OPERATION ONLY.
--
-- SAFETY & COMPLIANCE REQUIREMENTS:
-- 1. This script must NEVER be executed automatically or silently.
-- 2. It will REFUSE to execute unless @CONFIRM_RESTORE is explicitly set to
--    'YES_RESTORE_TALHACRM_TABLES' by an authorized operator.
-- 3. You must specify the exact @SnapshotSuffix to restore from.
-- 4. Works for BOTH failed/partially-applied and completed Phase 2 schema states:
--    - Detects whether TenantId column exists on domain tables.
--    - If TenantId exists, assigns records to Primary Tenant (11111111-1111-1111-1111-111111111111).
--    - If TenantId does not exist (Phase 1 schema), restores Phase 1 columns cleanly.
-- 5. Preserves Identity Users and Roles without deleting user records, avoiding
--    foreign-key violations or cascade deletion of memberships/claims/logins.
-- 6. Preserves and verifies TenantMemberships and TenantSettings dependencies.
-- 7. Targets ONLY TalhaCRM_* tables and NEVER touches any unrelated shared tables.
-- 8. Dynamically resolves user schema (AITStudent / dbo).
-- 9. No 'GO' batch separators; safe for both SSMS and ADO.NET execution.
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

DECLARE @schema NVARCHAR(128) = SCHEMA_NAME();

-- Pre-flight validation: Verify snapshot tables exist before altering anything
DECLARE @missingCount INT = 0;
IF OBJECT_ID(QUOTENAME(@schema) + '.[TalhaCRM_BKP_' + @SnapshotSuffix + '_Leads]') IS NULL SET @missingCount += 1;
IF OBJECT_ID(QUOTENAME(@schema) + '.[TalhaCRM_BKP_' + @SnapshotSuffix + '_LeadActivities]') IS NULL SET @missingCount += 1;
IF OBJECT_ID(QUOTENAME(@schema) + '.[TalhaCRM_BKP_' + @SnapshotSuffix + '_FollowUps]') IS NULL SET @missingCount += 1;
IF OBJECT_ID(QUOTENAME(@schema) + '.[TalhaCRM_BKP_' + @SnapshotSuffix + '_AILeadInsights]') IS NULL SET @missingCount += 1;
IF OBJECT_ID(QUOTENAME(@schema) + '.[TalhaCRM_BKP_' + @SnapshotSuffix + '_Notifications]') IS NULL SET @missingCount += 1;
IF OBJECT_ID(QUOTENAME(@schema) + '.[TalhaCRM_BKP_' + @SnapshotSuffix + '_AuditLogs]') IS NULL SET @missingCount += 1;
IF OBJECT_ID(QUOTENAME(@schema) + '.[TalhaCRM_BKP_' + @SnapshotSuffix + '_AspNetUsers]') IS NULL SET @missingCount += 1;
IF OBJECT_ID(QUOTENAME(@schema) + '.[TalhaCRM_BKP_' + @SnapshotSuffix + '_AspNetRoles]') IS NULL SET @missingCount += 1;
IF OBJECT_ID(QUOTENAME(@schema) + '.[TalhaCRM_BKP_' + @SnapshotSuffix + '_AspNetUserRoles]') IS NULL SET @missingCount += 1;

IF @missingCount > 0
BEGIN
    RAISERROR('SAFETY ABORT: One or more snapshot tables with suffix %s do not exist. Refusing to touch live CRM tables.', 16, 1, @SnapshotSuffix);
    RETURN;
END;

BEGIN TRANSACTION;
SET XACT_ABORT ON;

-- =============================================================================
-- STEP 1: PRESERVE & SYNCHRONIZE IDENTITY USERS AND ROLES (NO DELETION)
-- =============================================================================
DECLARE @syncIdentitySql NVARCHAR(MAX) = N'
-- 1. Ensure Roles from snapshot exist
INSERT INTO [' + @schema + N'].[TalhaCRM_AspNetRoles] (Id, Name, NormalizedName, ConcurrencyStamp)
SELECT b.Id, b.Name, b.NormalizedName, b.ConcurrencyStamp
FROM [' + @schema + N'].[TalhaCRM_BKP_' + @SnapshotSuffix + N'_AspNetRoles] b
WHERE NOT EXISTS (SELECT 1 FROM [' + @schema + N'].[TalhaCRM_AspNetRoles] r WHERE r.Id = b.Id);

-- 2. Ensure Users from snapshot exist (Insert missing)
INSERT INTO [' + @schema + N'].[TalhaCRM_AspNetUsers] (Id, FullName, CreatedAt, IsActive, UserName, NormalizedUserName, Email, NormalizedEmail, EmailConfirmed, PasswordHash, SecurityStamp, ConcurrencyStamp, PhoneNumber, PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnd, LockoutEnabled, AccessFailedCount)
SELECT b.Id, b.FullName, b.CreatedAt, b.IsActive, b.UserName, b.NormalizedUserName, b.Email, b.NormalizedEmail, b.EmailConfirmed, b.PasswordHash, b.SecurityStamp, b.ConcurrencyStamp, b.PhoneNumber, b.PhoneNumberConfirmed, b.TwoFactorEnabled, b.LockoutEnd, b.LockoutEnabled, b.AccessFailedCount
FROM [' + @schema + N'].[TalhaCRM_BKP_' + @SnapshotSuffix + N'_AspNetUsers] b
WHERE NOT EXISTS (SELECT 1 FROM [' + @schema + N'].[TalhaCRM_AspNetUsers] u WHERE u.Id = b.Id);

-- 3. Synchronize User core attributes from snapshot without deleting user rows
UPDATE u
SET u.FullName = b.FullName,
    u.IsActive = b.IsActive,
    u.UserName = b.UserName,
    u.NormalizedUserName = b.NormalizedUserName,
    u.Email = b.Email,
    u.NormalizedEmail = b.NormalizedEmail,
    u.EmailConfirmed = b.EmailConfirmed,
    u.PasswordHash = b.PasswordHash,
    u.SecurityStamp = b.SecurityStamp,
    u.ConcurrencyStamp = b.ConcurrencyStamp
FROM [' + @schema + N'].[TalhaCRM_AspNetUsers] u
JOIN [' + @schema + N'].[TalhaCRM_BKP_' + @SnapshotSuffix + N'_AspNetUsers] b ON u.Id = b.Id;

-- 4. Ensure User-Role mappings exist
INSERT INTO [' + @schema + N'].[TalhaCRM_AspNetUserRoles] (UserId, RoleId)
SELECT b.UserId, b.RoleId
FROM [' + @schema + N'].[TalhaCRM_BKP_' + @SnapshotSuffix + N'_AspNetUserRoles] b
WHERE NOT EXISTS (SELECT 1 FROM [' + @schema + N'].[TalhaCRM_AspNetUserRoles] ur WHERE ur.UserId = b.UserId AND ur.RoleId = b.RoleId);
';
EXEC sp_executesql @syncIdentitySql;

-- =============================================================================
-- STEP 2: ENSURE PHASE 2 TENANT INFRASTRUCTURE IF SCHEMA REQUIRES IT
-- =============================================================================
DECLARE @primaryTenantId UNIQUEIDENTIFIER = '11111111-1111-1111-1111-111111111111';

IF OBJECT_ID(QUOTENAME(@schema) + '.[TalhaCRM_Tenants]') IS NOT NULL
BEGIN
    DECLARE @tenantSql NVARCHAR(MAX) = N'
    IF NOT EXISTS (SELECT 1 FROM [' + @schema + N'].[TalhaCRM_Tenants] WHERE TenantId = @pTenantId)
    BEGIN
        INSERT INTO [' + @schema + N'].[TalhaCRM_Tenants] (
            TenantId, CompanyName, Slug, ContactEmail, Phone, 
            DefaultCurrency, SubscriptionTier, SubscriptionStatus, 
            MaxUserSeats, MaxLeadsQuota, MonthlyAiRequestLimit, CurrentMonthAiUsageCount, 
            IsActive, CreatedAt, UpdatedAt
        ) VALUES (
            @pTenantId, ''AI Powered CRM Enterprise'', ''primary-tenant'', ''compliance@aipoweredcrm.com'', ''+91-22-61234567'',
            ''INR'', ''Enterprise'', ''Active'',
            100, 50000, 5000, 0,
            1, SYSUTCDATETIME(), SYSUTCDATETIME()
        );
    END;';
    EXEC sp_executesql @tenantSql, N'@pTenantId UNIQUEIDENTIFIER', @pTenantId = @primaryTenantId;
END;

IF OBJECT_ID(QUOTENAME(@schema) + '.[TalhaCRM_TenantSettings]') IS NOT NULL
BEGIN
    DECLARE @settingSql NVARCHAR(MAX) = N'
    IF NOT EXISTS (SELECT 1 FROM [' + @schema + N'].[TalhaCRM_TenantSettings] WHERE TenantId = @pTenantId)
    BEGIN
        INSERT INTO [' + @schema + N'].[TalhaCRM_TenantSettings] (
            TenantId, CurrencyCode, CurrencySymbol, DateFormat, TimeZoneId, 
            FiscalYearStartMonth, LeadAssignmentMode, RestrictLeadExport, UpdatedAt
        ) VALUES (
            @pTenantId, ''INR'', N''₹'', ''dd/MM/yyyy'', ''India Standard Time'', 
            4, ''Manual'', 1, SYSUTCDATETIME()
        );
    END;';
    EXEC sp_executesql @settingSql, N'@pTenantId UNIQUEIDENTIFIER', @pTenantId = @primaryTenantId;
END;

IF OBJECT_ID(QUOTENAME(@schema) + '.[TalhaCRM_TenantMemberships]') IS NOT NULL
BEGIN
    DECLARE @membershipSql NVARCHAR(MAX) = N'
    INSERT INTO [' + @schema + N'].[TalhaCRM_TenantMemberships] (MembershipId, TenantId, UserId, CompanyRole, IsActive, JoinedAt)
    SELECT NEWID(), @pTenantId, u.Id,
        CASE 
            WHEN r.Name IN (''Admin'', ''CompanyAdmin'') THEN ''CompanyAdmin''
            WHEN r.Name IN (''Manager'', ''SalesManager'') THEN ''Manager''
            ELSE ''SalesExecutive''
        END,
        1, SYSUTCDATETIME()
    FROM [' + @schema + N'].[TalhaCRM_AspNetUsers] u
    LEFT JOIN [' + @schema + N'].[TalhaCRM_AspNetUserRoles] ur ON u.Id = ur.UserId
    LEFT JOIN [' + @schema + N'].[TalhaCRM_AspNetRoles] r ON ur.RoleId = r.Id
    WHERE r.Name NOT IN (''PlatformSuperAdmin'', ''SuperAdmin'')
      AND NOT EXISTS (
          SELECT 1 FROM [' + @schema + N'].[TalhaCRM_TenantMemberships] tm 
          WHERE tm.TenantId = @pTenantId AND tm.UserId = u.Id
      );';
    EXEC sp_executesql @membershipSql, N'@pTenantId UNIQUEIDENTIFIER', @pTenantId = @primaryTenantId;
END;

-- =============================================================================
-- STEP 3: CLEAR DOMAIN TABLES IN CHILD-TO-PARENT ORDER
-- =============================================================================
DECLARE @clearSql NVARCHAR(MAX) = N'
DELETE FROM [' + @schema + N'].[TalhaCRM_FollowUps];
DELETE FROM [' + @schema + N'].[TalhaCRM_LeadActivities];
DELETE FROM [' + @schema + N'].[TalhaCRM_AILeadInsights];
DELETE FROM [' + @schema + N'].[TalhaCRM_Notifications];
DELETE FROM [' + @schema + N'].[TalhaCRM_AuditLogs];
IF OBJECT_ID(''' + QUOTENAME(@schema) + N'.[TalhaCRM_LeadStatusHistory]'') IS NOT NULL DELETE FROM [' + @schema + N'].[TalhaCRM_LeadStatusHistory];
DELETE FROM [' + @schema + N'].[TalhaCRM_Leads];
';
EXEC sp_executesql @clearSql;

-- =============================================================================
-- STEP 4: DYNAMIC RESTORATION OF DOMAIN RECORDS (ADAPTING TO TENANTID)
-- =============================================================================
DECLARE @hasTenantCol BIT = CASE WHEN COL_LENGTH(QUOTENAME(@schema) + '.[TalhaCRM_Leads]', 'TenantId') IS NOT NULL THEN 1 ELSE 0 END;
DECLARE @restoreDomainSql NVARCHAR(MAX);

IF @hasTenantCol = 1
BEGIN
    -- PHASE 2 OR PARTIALLY-APPLIED SCHEMA: Map TenantId to Primary Tenant
    SET @restoreDomainSql = N'
    SET IDENTITY_INSERT [' + @schema + N'].[TalhaCRM_Leads] ON;
    INSERT INTO [' + @schema + N'].[TalhaCRM_Leads] (LeadId, Name, Phone, Email, Company, Source, InterestedProduct, Budget, Status, Priority, OwnerUserId, IsArchived, CreatedAt, UpdatedAt, TenantId)
    SELECT LeadId, Name, Phone, Email, Company, Source, InterestedProduct, Budget, Status, Priority, OwnerUserId, IsArchived, CreatedAt, UpdatedAt, CAST(''11111111-1111-1111-1111-111111111111'' AS uniqueidentifier)
    FROM [' + @schema + N'].[TalhaCRM_BKP_' + @SnapshotSuffix + N'_Leads];
    SET IDENTITY_INSERT [' + @schema + N'].[TalhaCRM_Leads] OFF;

    SET IDENTITY_INSERT [' + @schema + N'].[TalhaCRM_LeadActivities] ON;
    INSERT INTO [' + @schema + N'].[TalhaCRM_LeadActivities] (ActivityId, LeadId, UserId, Type, Description, OccurredAt, TenantId)
    SELECT ActivityId, LeadId, UserId, Type, Description, OccurredAt, CAST(''11111111-1111-1111-1111-111111111111'' AS uniqueidentifier)
    FROM [' + @schema + N'].[TalhaCRM_BKP_' + @SnapshotSuffix + N'_LeadActivities];
    SET IDENTITY_INSERT [' + @schema + N'].[TalhaCRM_LeadActivities] OFF;

    SET IDENTITY_INSERT [' + @schema + N'].[TalhaCRM_FollowUps] ON;
    INSERT INTO [' + @schema + N'].[TalhaCRM_FollowUps] (FollowUpId, LeadId, AssignedUserId, DueAt, Type, Notes, Status, CompletedAt, TenantId)
    SELECT FollowUpId, LeadId, AssignedUserId, DueAt, Type, Notes, Status, CompletedAt, CAST(''11111111-1111-1111-1111-111111111111'' AS uniqueidentifier)
    FROM [' + @schema + N'].[TalhaCRM_BKP_' + @SnapshotSuffix + N'_FollowUps];
    SET IDENTITY_INSERT [' + @schema + N'].[TalhaCRM_FollowUps] OFF;

    IF OBJECT_ID(''' + QUOTENAME(@schema) + N'.[TalhaCRM_BKP_' + @SnapshotSuffix + N'_LeadStatusHistory]'') IS NOT NULL AND (SELECT COUNT(*) FROM [' + @schema + N'].[TalhaCRM_BKP_' + @SnapshotSuffix + N'_LeadStatusHistory]) > 0
    BEGIN
        SET IDENTITY_INSERT [' + @schema + N'].[TalhaCRM_LeadStatusHistory] ON;
        INSERT INTO [' + @schema + N'].[TalhaCRM_LeadStatusHistory] (HistoryId, LeadId, OldStatus, NewStatus, ChangedByUserId, Notes, ChangedAt, TenantId)
        SELECT HistoryId, LeadId, OldStatus, NewStatus, ChangedByUserId, Notes, ChangedAt, CAST(''11111111-1111-1111-1111-111111111111'' AS uniqueidentifier)
        FROM [' + @schema + N'].[TalhaCRM_BKP_' + @SnapshotSuffix + N'_LeadStatusHistory];
        SET IDENTITY_INSERT [' + @schema + N'].[TalhaCRM_LeadStatusHistory] OFF;
    END;

    SET IDENTITY_INSERT [' + @schema + N'].[TalhaCRM_AILeadInsights] ON;
    INSERT INTO [' + @schema + N'].[TalhaCRM_AILeadInsights] (InsightId, LeadId, Classification, Score, Summary, NextBestAction, RiskLevel, Urgency, ReasonJson, SuggestedResponse, Provider, Model, GeneratedAt, TenantId)
    SELECT InsightId, LeadId, Classification, Score, Summary, NextBestAction, RiskLevel, Urgency, ReasonJson, SuggestedResponse, Provider, Model, GeneratedAt, CAST(''11111111-1111-1111-1111-111111111111'' AS uniqueidentifier)
    FROM [' + @schema + N'].[TalhaCRM_BKP_' + @SnapshotSuffix + N'_AILeadInsights];
    SET IDENTITY_INSERT [' + @schema + N'].[TalhaCRM_AILeadInsights] OFF;

    SET IDENTITY_INSERT [' + @schema + N'].[TalhaCRM_Notifications] ON;
    INSERT INTO [' + @schema + N'].[TalhaCRM_Notifications] (NotificationId, UserId, LeadId, Title, Message, IsRead, CreatedAt, TenantId)
    SELECT NotificationId, UserId, LeadId, Title, Message, IsRead, CreatedAt, CAST(''11111111-1111-1111-1111-111111111111'' AS uniqueidentifier)
    FROM [' + @schema + N'].[TalhaCRM_BKP_' + @SnapshotSuffix + N'_Notifications];
    SET IDENTITY_INSERT [' + @schema + N'].[TalhaCRM_Notifications] OFF;

    SET IDENTITY_INSERT [' + @schema + N'].[TalhaCRM_AuditLogs] ON;
    INSERT INTO [' + @schema + N'].[TalhaCRM_AuditLogs] (AuditLogId, UserId, EntityType, EntityId, Action, DetailsJson, CreatedAt, TenantId)
    SELECT AuditLogId, UserId, EntityType, EntityId, Action, DetailsJson, CreatedAt, CAST(''11111111-1111-1111-1111-111111111111'' AS uniqueidentifier)
    FROM [' + @schema + N'].[TalhaCRM_BKP_' + @SnapshotSuffix + N'_AuditLogs];
    SET IDENTITY_INSERT [' + @schema + N'].[TalhaCRM_AuditLogs] OFF;
    ';
END
ELSE
BEGIN
    -- PHASE 1 SCHEMA (No TenantId Column): Clean baseline restoration
    SET @restoreDomainSql = N'
    SET IDENTITY_INSERT [' + @schema + N'].[TalhaCRM_Leads] ON;
    INSERT INTO [' + @schema + N'].[TalhaCRM_Leads] (LeadId, Name, Phone, Email, Company, Source, InterestedProduct, Budget, Status, Priority, OwnerUserId, IsArchived, CreatedAt, UpdatedAt)
    SELECT LeadId, Name, Phone, Email, Company, Source, InterestedProduct, Budget, Status, Priority, OwnerUserId, IsArchived, CreatedAt, UpdatedAt
    FROM [' + @schema + N'].[TalhaCRM_BKP_' + @SnapshotSuffix + N'_Leads];
    SET IDENTITY_INSERT [' + @schema + N'].[TalhaCRM_Leads] OFF;

    SET IDENTITY_INSERT [' + @schema + N'].[TalhaCRM_LeadActivities] ON;
    INSERT INTO [' + @schema + N'].[TalhaCRM_LeadActivities] (ActivityId, LeadId, UserId, Type, Description, OccurredAt)
    SELECT ActivityId, LeadId, UserId, Type, Description, OccurredAt
    FROM [' + @schema + N'].[TalhaCRM_BKP_' + @SnapshotSuffix + N'_LeadActivities];
    SET IDENTITY_INSERT [' + @schema + N'].[TalhaCRM_LeadActivities] OFF;

    SET IDENTITY_INSERT [' + @schema + N'].[TalhaCRM_FollowUps] ON;
    INSERT INTO [' + @schema + N'].[TalhaCRM_FollowUps] (FollowUpId, LeadId, AssignedUserId, DueAt, Type, Notes, Status, CompletedAt)
    SELECT FollowUpId, LeadId, AssignedUserId, DueAt, Type, Notes, Status, CompletedAt
    FROM [' + @schema + N'].[TalhaCRM_BKP_' + @SnapshotSuffix + N'_FollowUps];
    SET IDENTITY_INSERT [' + @schema + N'].[TalhaCRM_FollowUps] OFF;

    IF OBJECT_ID(''' + QUOTENAME(@schema) + N'.[TalhaCRM_BKP_' + @SnapshotSuffix + N'_LeadStatusHistory]'') IS NOT NULL AND (SELECT COUNT(*) FROM [' + @schema + N'].[TalhaCRM_BKP_' + @SnapshotSuffix + N'_LeadStatusHistory]) > 0
    BEGIN
        SET IDENTITY_INSERT [' + @schema + N'].[TalhaCRM_LeadStatusHistory] ON;
        INSERT INTO [' + @schema + N'].[TalhaCRM_LeadStatusHistory] (HistoryId, LeadId, OldStatus, NewStatus, ChangedByUserId, Notes, ChangedAt)
        SELECT HistoryId, LeadId, OldStatus, NewStatus, ChangedByUserId, Notes, ChangedAt
        FROM [' + @schema + N'].[TalhaCRM_BKP_' + @SnapshotSuffix + N'_LeadStatusHistory];
        SET IDENTITY_INSERT [' + @schema + N'].[TalhaCRM_LeadStatusHistory] OFF;
    END;

    SET IDENTITY_INSERT [' + @schema + N'].[TalhaCRM_AILeadInsights] ON;
    INSERT INTO [' + @schema + N'].[TalhaCRM_AILeadInsights] (InsightId, LeadId, Classification, Score, Summary, NextBestAction, RiskLevel, Urgency, ReasonJson, SuggestedResponse, Provider, Model, GeneratedAt)
    SELECT InsightId, LeadId, Classification, Score, Summary, NextBestAction, RiskLevel, Urgency, ReasonJson, SuggestedResponse, Provider, Model, GeneratedAt
    FROM [' + @schema + N'].[TalhaCRM_BKP_' + @SnapshotSuffix + N'_AILeadInsights];
    SET IDENTITY_INSERT [' + @schema + N'].[TalhaCRM_AILeadInsights] OFF;

    SET IDENTITY_INSERT [' + @schema + N'].[TalhaCRM_Notifications] ON;
    INSERT INTO [' + @schema + N'].[TalhaCRM_Notifications] (NotificationId, UserId, LeadId, Title, Message, IsRead, CreatedAt)
    SELECT NotificationId, UserId, LeadId, Title, Message, IsRead, CreatedAt
    FROM [' + @schema + N'].[TalhaCRM_BKP_' + @SnapshotSuffix + N'_Notifications];
    SET IDENTITY_INSERT [' + @schema + N'].[TalhaCRM_Notifications] OFF;

    SET IDENTITY_INSERT [' + @schema + N'].[TalhaCRM_AuditLogs] ON;
    INSERT INTO [' + @schema + N'].[TalhaCRM_AuditLogs] (AuditLogId, UserId, EntityType, EntityId, Action, DetailsJson, CreatedAt)
    SELECT AuditLogId, UserId, EntityType, EntityId, Action, DetailsJson, CreatedAt
    FROM [' + @schema + N'].[TalhaCRM_BKP_' + @SnapshotSuffix + N'_AuditLogs];
    SET IDENTITY_INSERT [' + @schema + N'].[TalhaCRM_AuditLogs] OFF;
    ';
END;

EXEC sp_executesql @restoreDomainSql;

-- =============================================================================
-- STEP 5: RE-CHECK & ENFORCE CONSTRAINTS
-- =============================================================================
DECLARE @enforceSql NVARCHAR(MAX) = N'
ALTER TABLE [' + @schema + N'].[TalhaCRM_FollowUps] WITH CHECK CHECK CONSTRAINT ALL;
ALTER TABLE [' + @schema + N'].[TalhaCRM_LeadActivities] WITH CHECK CHECK CONSTRAINT ALL;
ALTER TABLE [' + @schema + N'].[TalhaCRM_AILeadInsights] WITH CHECK CHECK CONSTRAINT ALL;
ALTER TABLE [' + @schema + N'].[TalhaCRM_Notifications] WITH CHECK CHECK CONSTRAINT ALL;
ALTER TABLE [' + @schema + N'].[TalhaCRM_AuditLogs] WITH CHECK CHECK CONSTRAINT ALL;
IF OBJECT_ID(''' + QUOTENAME(@schema) + N'.[TalhaCRM_LeadStatusHistory]'') IS NOT NULL ALTER TABLE [' + @schema + N'].[TalhaCRM_LeadStatusHistory] WITH CHECK CHECK CONSTRAINT ALL;
ALTER TABLE [' + @schema + N'].[TalhaCRM_Leads] WITH CHECK CHECK CONSTRAINT ALL;
IF OBJECT_ID(''' + QUOTENAME(@schema) + N'.[TalhaCRM_TenantMemberships]'') IS NOT NULL ALTER TABLE [' + @schema + N'].[TalhaCRM_TenantMemberships] WITH CHECK CHECK CONSTRAINT ALL;
IF OBJECT_ID(''' + QUOTENAME(@schema) + N'.[TalhaCRM_TenantSettings]'') IS NOT NULL ALTER TABLE [' + @schema + N'].[TalhaCRM_TenantSettings] WITH CHECK CHECK CONSTRAINT ALL;
';
EXEC sp_executesql @enforceSql;

COMMIT TRANSACTION;
