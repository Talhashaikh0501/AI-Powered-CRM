BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [TalhaCRM_EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015164702_Phase2MultiTenantCore'
)
BEGIN
    DROP INDEX [IX_TalhaCRM_Notifications_UserId_IsRead] ON [TalhaCRM_Notifications];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [TalhaCRM_EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015164702_Phase2MultiTenantCore'
)
BEGIN
    DROP INDEX [IX_TalhaCRM_Leads_OwnerUserId_Status] ON [TalhaCRM_Leads];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [TalhaCRM_EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015164702_Phase2MultiTenantCore'
)
BEGIN
    DROP INDEX [IX_TalhaCRM_LeadActivities_LeadId_OccurredAt] ON [TalhaCRM_LeadActivities];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [TalhaCRM_EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015164702_Phase2MultiTenantCore'
)
BEGIN
    DROP INDEX [IX_TalhaCRM_FollowUps_AssignedUserId_DueAt_Status] ON [TalhaCRM_FollowUps];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [TalhaCRM_EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015164702_Phase2MultiTenantCore'
)
BEGIN
    DROP INDEX [IX_TalhaCRM_AuditLogs_CreatedAt] ON [TalhaCRM_AuditLogs];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [TalhaCRM_EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015164702_Phase2MultiTenantCore'
)
BEGIN
    DROP INDEX [IX_TalhaCRM_AILeadInsights_LeadId_GeneratedAt] ON [TalhaCRM_AILeadInsights];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [TalhaCRM_EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015164702_Phase2MultiTenantCore'
)
BEGIN
    ALTER TABLE [TalhaCRM_Notifications] ADD [TenantId] uniqueidentifier NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [TalhaCRM_EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015164702_Phase2MultiTenantCore'
)
BEGIN
    ALTER TABLE [TalhaCRM_LeadStatusHistory] ADD [TenantId] uniqueidentifier NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [TalhaCRM_EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015164702_Phase2MultiTenantCore'
)
BEGIN
    ALTER TABLE [TalhaCRM_Leads] ADD [TenantId] uniqueidentifier NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [TalhaCRM_EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015164702_Phase2MultiTenantCore'
)
BEGIN
    ALTER TABLE [TalhaCRM_LeadActivities] ADD [TenantId] uniqueidentifier NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [TalhaCRM_EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015164702_Phase2MultiTenantCore'
)
BEGIN
    ALTER TABLE [TalhaCRM_FollowUps] ADD [TenantId] uniqueidentifier NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [TalhaCRM_EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015164702_Phase2MultiTenantCore'
)
BEGIN
    ALTER TABLE [TalhaCRM_AuditLogs] ADD [TenantId] uniqueidentifier NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [TalhaCRM_EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015164702_Phase2MultiTenantCore'
)
BEGIN
    ALTER TABLE [TalhaCRM_AILeadInsights] ADD [TenantId] uniqueidentifier NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [TalhaCRM_EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015164702_Phase2MultiTenantCore'
)
BEGIN
    CREATE TABLE [TalhaCRM_Tenants] (
        [TenantId] uniqueidentifier NOT NULL,
        [CompanyName] nvarchar(150) NOT NULL,
        [Slug] nvarchar(60) NOT NULL,
        [ContactEmail] nvarchar(180) NOT NULL,
        [Phone] nvarchar(30) NULL,
        [DefaultCurrency] nvarchar(10) NOT NULL,
        [SubscriptionTier] nvarchar(30) NOT NULL,
        [SubscriptionStatus] nvarchar(30) NOT NULL,
        [TrialEndsAt] datetime2 NULL,
        [SubscriptionRenewsAt] datetime2 NULL,
        [MaxUserSeats] int NOT NULL,
        [MaxLeadsQuota] int NOT NULL,
        [MonthlyAiRequestLimit] int NOT NULL,
        [CurrentMonthAiUsageCount] int NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_TalhaCRM_Tenants] PRIMARY KEY ([TenantId])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [TalhaCRM_EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015164702_Phase2MultiTenantCore'
)
BEGIN
    CREATE TABLE [TalhaCRM_TenantInvitations] (
        [InvitationId] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [Email] nvarchar(180) NOT NULL,
        [CompanyRole] nvarchar(30) NOT NULL,
        [TokenHash] nvarchar(256) NOT NULL,
        [ExpiresAt] datetime2 NOT NULL,
        [IsAccepted] bit NOT NULL,
        [InvitedByUserId] nvarchar(450) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_TalhaCRM_TenantInvitations] PRIMARY KEY ([InvitationId]),
        CONSTRAINT [FK_TalhaCRM_TenantInvitations_TalhaCRM_AspNetUsers_InvitedByUserId] FOREIGN KEY ([InvitedByUserId]) REFERENCES [TalhaCRM_AspNetUsers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_TalhaCRM_TenantInvitations_TalhaCRM_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [TalhaCRM_Tenants] ([TenantId]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [TalhaCRM_EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015164702_Phase2MultiTenantCore'
)
BEGIN
    CREATE TABLE [TalhaCRM_TenantMemberships] (
        [MembershipId] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [UserId] nvarchar(450) NOT NULL,
        [CompanyRole] nvarchar(30) NOT NULL,
        [IsActive] bit NOT NULL,
        [JoinedAt] datetime2 NOT NULL,
        [InvitedByUserId] nvarchar(450) NULL,
        CONSTRAINT [PK_TalhaCRM_TenantMemberships] PRIMARY KEY ([MembershipId]),
        CONSTRAINT [FK_TalhaCRM_TenantMemberships_TalhaCRM_AspNetUsers_InvitedByUserId] FOREIGN KEY ([InvitedByUserId]) REFERENCES [TalhaCRM_AspNetUsers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_TalhaCRM_TenantMemberships_TalhaCRM_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [TalhaCRM_AspNetUsers] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_TalhaCRM_TenantMemberships_TalhaCRM_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [TalhaCRM_Tenants] ([TenantId]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [TalhaCRM_EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015164702_Phase2MultiTenantCore'
)
BEGIN
    CREATE TABLE [TalhaCRM_TenantSettings] (
        [TenantId] uniqueidentifier NOT NULL,
        [CurrencyCode] nvarchar(10) NOT NULL,
        [CurrencySymbol] nvarchar(10) NOT NULL,
        [DateFormat] nvarchar(30) NOT NULL,
        [TimeZoneId] nvarchar(60) NOT NULL,
        [FiscalYearStartMonth] int NOT NULL,
        [LeadAssignmentMode] nvarchar(30) NOT NULL,
        [RestrictLeadExport] bit NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_TalhaCRM_TenantSettings] PRIMARY KEY ([TenantId]),
        CONSTRAINT [FK_TalhaCRM_TenantSettings_TalhaCRM_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [TalhaCRM_Tenants] ([TenantId]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [TalhaCRM_EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015164702_Phase2MultiTenantCore'
)
BEGIN
    CREATE TABLE [TalhaCRM_SupportAccessTickets] (
        [TicketId] uniqueidentifier NOT NULL,
        [TicketNumber] nvarchar(50) NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [SuperAdminUserId] nvarchar(450) NOT NULL,
        [CreatedByUserId] nvarchar(450) NOT NULL,
        [Reason] nvarchar(500) NOT NULL,
        [Status] nvarchar(30) NOT NULL,
        [ApprovedAt] datetime2 NULL,
        [ApprovedByUserId] nvarchar(450) NULL,
        [ExpiresAt] datetime2 NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_TalhaCRM_SupportAccessTickets] PRIMARY KEY ([TicketId]),
        CONSTRAINT [FK_TalhaCRM_SupportAccessTickets_TalhaCRM_AspNetUsers_SuperAdminUserId] FOREIGN KEY ([SuperAdminUserId]) REFERENCES [TalhaCRM_AspNetUsers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_TalhaCRM_SupportAccessTickets_TalhaCRM_AspNetUsers_CreatedByUserId] FOREIGN KEY ([CreatedByUserId]) REFERENCES [TalhaCRM_AspNetUsers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_TalhaCRM_SupportAccessTickets_TalhaCRM_AspNetUsers_ApprovedByUserId] FOREIGN KEY ([ApprovedByUserId]) REFERENCES [TalhaCRM_AspNetUsers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_TalhaCRM_SupportAccessTickets_TalhaCRM_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [TalhaCRM_Tenants] ([TenantId]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [TalhaCRM_EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015164702_Phase2MultiTenantCore'
)
BEGIN

                    -- 1. Seed Primary Default Tenant if not already present
                    IF NOT EXISTS (SELECT 1 FROM [TalhaCRM_Tenants] WHERE [TenantId] = '11111111-1111-1111-1111-111111111111')
                    BEGIN
                        INSERT INTO [TalhaCRM_Tenants] (
                            [TenantId], [CompanyName], [Slug], [ContactEmail], [Phone], 
                            [DefaultCurrency], [SubscriptionTier], [SubscriptionStatus], 
                            [MaxUserSeats], [MaxLeadsQuota], [MonthlyAiRequestLimit], [CurrentMonthAiUsageCount], 
                            [IsActive], [CreatedAt], [UpdatedAt]
                        ) VALUES (
                            '11111111-1111-1111-1111-111111111111', 'AI Powered CRM Enterprise', 'primary-tenant', 'compliance@aipoweredcrm.com', '+91-22-61234567',
                            'INR', 'Enterprise', 'Active',
                            100, 50000, 5000, 0,
                            1, SYSUTCDATETIME(), SYSUTCDATETIME()
                        );
                    END

                    -- 2. Seed Primary Tenant Settings if not already present
                    IF NOT EXISTS (SELECT 1 FROM [TalhaCRM_TenantSettings] WHERE [TenantId] = '11111111-1111-1111-1111-111111111111')
                    BEGIN
                        INSERT INTO [TalhaCRM_TenantSettings] (
                            [TenantId], [CurrencyCode], [CurrencySymbol], [DateFormat], [TimeZoneId], 
                            [FiscalYearStartMonth], [LeadAssignmentMode], [RestrictLeadExport], [UpdatedAt]
                        ) VALUES (
                            '11111111-1111-1111-1111-111111111111', 'INR', N'₹', 'dd/MM/yyyy', 'India Standard Time', 
                            4, 'Manual', 1, SYSUTCDATETIME()
                        );
                    END

                    -- 3. Backfill existing legacy records with the primary tenant
                    UPDATE [TalhaCRM_Leads] SET [TenantId] = '11111111-1111-1111-1111-111111111111' WHERE [TenantId] = '00000000-0000-0000-0000-000000000000';
                    UPDATE [TalhaCRM_LeadActivities] SET [TenantId] = '11111111-1111-1111-1111-111111111111' WHERE [TenantId] = '00000000-0000-0000-0000-000000000000';
                    UPDATE [TalhaCRM_FollowUps] SET [TenantId] = '11111111-1111-1111-1111-111111111111' WHERE [TenantId] = '00000000-0000-0000-0000-000000000000';
                    UPDATE [TalhaCRM_LeadStatusHistory] SET [TenantId] = '11111111-1111-1111-1111-111111111111' WHERE [TenantId] = '00000000-0000-0000-0000-000000000000';
                    UPDATE [TalhaCRM_AILeadInsights] SET [TenantId] = '11111111-1111-1111-1111-111111111111' WHERE [TenantId] = '00000000-0000-0000-0000-000000000000';
                    UPDATE [TalhaCRM_Notifications] SET [TenantId] = '11111111-1111-1111-1111-111111111111' WHERE [TenantId] = '00000000-0000-0000-0000-000000000000';
                    UPDATE [TalhaCRM_AuditLogs] SET [TenantId] = '11111111-1111-1111-1111-111111111111' WHERE [TenantId] IS NULL;

                    -- 4. Automatically map existing users into primary company membership,
                    -- HANDLING USERS WITH MULTIPLE ROLES (aggregate to highest role: Admin > Manager > SalesExecutive)
                    -- and STRICTLY EXCLUDING Platform SuperAdmins who must remain platform-scoped without company membership.
                    WITH UserMaxRole AS (
                        SELECT 
                            u.[Id] AS [UserId],
                            CASE 
                                WHEN SUM(CASE WHEN r.[Name] IN ('Admin', 'CompanyAdmin') THEN 1 ELSE 0 END) > 0 THEN 'CompanyAdmin'
                                WHEN SUM(CASE WHEN r.[Name] IN ('Manager', 'SalesManager') THEN 1 ELSE 0 END) > 0 THEN 'Manager'
                                ELSE 'SalesExecutive'
                            END AS [CompanyRole]
                        FROM [TalhaCRM_AspNetUsers] u
                        LEFT JOIN [TalhaCRM_AspNetUserRoles] ur ON u.[Id] = ur.[UserId]
                        LEFT JOIN [TalhaCRM_AspNetRoles] r ON ur.[RoleId] = r.[Id]
                        WHERE NOT EXISTS (
                            -- Exclude users who hold PlatformSuperAdmin or SuperAdmin role
                            SELECT 1 FROM [TalhaCRM_AspNetUserRoles] ur_sa 
                            JOIN [TalhaCRM_AspNetRoles] r_sa ON ur_sa.[RoleId] = r_sa.[Id] 
                            WHERE ur_sa.[UserId] = u.[Id] AND r_sa.[Name] IN ('PlatformSuperAdmin', 'SuperAdmin')
                        )
                        GROUP BY u.[Id]
                    )
                    INSERT INTO [TalhaCRM_TenantMemberships] ([MembershipId], [TenantId], [UserId], [CompanyRole], [IsActive], [JoinedAt])
                    SELECT 
                        NEWID(), 
                        '11111111-1111-1111-1111-111111111111', 
                        umr.[UserId], 
                        umr.[CompanyRole], 
                        1, 
                        SYSUTCDATETIME()
                    FROM UserMaxRole umr
                    WHERE NOT EXISTS (
                        -- Do not insert duplicates
                        SELECT 1 FROM [TalhaCRM_TenantMemberships] tm 
                        WHERE tm.[TenantId] = '11111111-1111-1111-1111-111111111111' AND tm.[UserId] = umr.[UserId]
                    );
                
END;
GO

IF NOT EXISTS (
    SELECT * FROM [TalhaCRM_EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015164702_Phase2MultiTenantCore'
)
BEGIN
    CREATE INDEX [IX_TalhaCRM_SupportAccessTickets_TenantId_TicketNumber_Status] ON [TalhaCRM_SupportAccessTickets] ([TenantId], [TicketNumber], [Status]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [TalhaCRM_EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015164702_Phase2MultiTenantCore'
)
BEGIN
    CREATE INDEX [IX_TalhaCRM_SupportAccessTickets_SuperAdminUserId] ON [TalhaCRM_SupportAccessTickets] ([SuperAdminUserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [TalhaCRM_EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015164702_Phase2MultiTenantCore'
)
BEGIN
    CREATE INDEX [IX_TalhaCRM_Notifications_TenantId_UserId_IsRead] ON [TalhaCRM_Notifications] ([TenantId], [UserId], [IsRead]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [TalhaCRM_EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015164702_Phase2MultiTenantCore'
)
BEGIN
    CREATE INDEX [IX_TalhaCRM_Notifications_UserId] ON [TalhaCRM_Notifications] ([UserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [TalhaCRM_EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015164702_Phase2MultiTenantCore'
)
BEGIN
    CREATE INDEX [IX_TalhaCRM_LeadStatusHistory_TenantId] ON [TalhaCRM_LeadStatusHistory] ([TenantId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [TalhaCRM_EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015164702_Phase2MultiTenantCore'
)
BEGIN
    CREATE INDEX [IX_TalhaCRM_Leads_OwnerUserId] ON [TalhaCRM_Leads] ([OwnerUserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [TalhaCRM_EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015164702_Phase2MultiTenantCore'
)
BEGIN
    CREATE INDEX [IX_TalhaCRM_Leads_TenantId_OwnerUserId_Status] ON [TalhaCRM_Leads] ([TenantId], [OwnerUserId], [Status]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [TalhaCRM_EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015164702_Phase2MultiTenantCore'
)
BEGIN
    CREATE INDEX [IX_TalhaCRM_Leads_TenantId_Status] ON [TalhaCRM_Leads] ([TenantId], [Status]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [TalhaCRM_EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015164702_Phase2MultiTenantCore'
)
BEGIN
    CREATE INDEX [IX_TalhaCRM_LeadActivities_LeadId] ON [TalhaCRM_LeadActivities] ([LeadId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [TalhaCRM_EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015164702_Phase2MultiTenantCore'
)
BEGIN
    CREATE INDEX [IX_TalhaCRM_LeadActivities_TenantId_LeadId_OccurredAt] ON [TalhaCRM_LeadActivities] ([TenantId], [LeadId], [OccurredAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [TalhaCRM_EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015164702_Phase2MultiTenantCore'
)
BEGIN
    CREATE INDEX [IX_TalhaCRM_FollowUps_AssignedUserId] ON [TalhaCRM_FollowUps] ([AssignedUserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [TalhaCRM_EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015164702_Phase2MultiTenantCore'
)
BEGIN
    CREATE INDEX [IX_TalhaCRM_FollowUps_TenantId_AssignedUserId_DueAt_Status] ON [TalhaCRM_FollowUps] ([TenantId], [AssignedUserId], [DueAt], [Status]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [TalhaCRM_EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015164702_Phase2MultiTenantCore'
)
BEGIN
    CREATE INDEX [IX_TalhaCRM_AuditLogs_TenantId_CreatedAt] ON [TalhaCRM_AuditLogs] ([TenantId], [CreatedAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [TalhaCRM_EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015164702_Phase2MultiTenantCore'
)
BEGIN
    CREATE INDEX [IX_TalhaCRM_AILeadInsights_LeadId] ON [TalhaCRM_AILeadInsights] ([LeadId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [TalhaCRM_EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015164702_Phase2MultiTenantCore'
)
BEGIN
    CREATE INDEX [IX_TalhaCRM_AILeadInsights_TenantId_LeadId_GeneratedAt] ON [TalhaCRM_AILeadInsights] ([TenantId], [LeadId], [GeneratedAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [TalhaCRM_EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015164702_Phase2MultiTenantCore'
)
BEGIN
    CREATE INDEX [IX_TalhaCRM_TenantInvitations_InvitedByUserId] ON [TalhaCRM_TenantInvitations] ([InvitedByUserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [TalhaCRM_EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015164702_Phase2MultiTenantCore'
)
BEGIN
    CREATE INDEX [IX_TalhaCRM_TenantInvitations_TenantId_Email] ON [TalhaCRM_TenantInvitations] ([TenantId], [Email]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [TalhaCRM_EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015164702_Phase2MultiTenantCore'
)
BEGIN
    CREATE INDEX [IX_TalhaCRM_TenantMemberships_InvitedByUserId] ON [TalhaCRM_TenantMemberships] ([InvitedByUserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [TalhaCRM_EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015164702_Phase2MultiTenantCore'
)
BEGIN
    CREATE UNIQUE INDEX [IX_TalhaCRM_TenantMemberships_TenantId_UserId] ON [TalhaCRM_TenantMemberships] ([TenantId], [UserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [TalhaCRM_EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015164702_Phase2MultiTenantCore'
)
BEGIN
    CREATE INDEX [IX_TalhaCRM_TenantMemberships_UserId] ON [TalhaCRM_TenantMemberships] ([UserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [TalhaCRM_EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015164702_Phase2MultiTenantCore'
)
BEGIN
    CREATE UNIQUE INDEX [IX_TalhaCRM_Tenants_Slug] ON [TalhaCRM_Tenants] ([Slug]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [TalhaCRM_EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015164702_Phase2MultiTenantCore'
)
BEGIN
    ALTER TABLE [TalhaCRM_AILeadInsights] ADD CONSTRAINT [FK_TalhaCRM_AILeadInsights_TalhaCRM_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [TalhaCRM_Tenants] ([TenantId]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [TalhaCRM_EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015164702_Phase2MultiTenantCore'
)
BEGIN
    ALTER TABLE [TalhaCRM_AuditLogs] ADD CONSTRAINT [FK_TalhaCRM_AuditLogs_TalhaCRM_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [TalhaCRM_Tenants] ([TenantId]) ON DELETE SET NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [TalhaCRM_EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015164702_Phase2MultiTenantCore'
)
BEGIN
    ALTER TABLE [TalhaCRM_FollowUps] ADD CONSTRAINT [FK_TalhaCRM_FollowUps_TalhaCRM_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [TalhaCRM_Tenants] ([TenantId]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [TalhaCRM_EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015164702_Phase2MultiTenantCore'
)
BEGIN
    ALTER TABLE [TalhaCRM_LeadActivities] ADD CONSTRAINT [FK_TalhaCRM_LeadActivities_TalhaCRM_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [TalhaCRM_Tenants] ([TenantId]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [TalhaCRM_EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015164702_Phase2MultiTenantCore'
)
BEGIN
    ALTER TABLE [TalhaCRM_Leads] ADD CONSTRAINT [FK_TalhaCRM_Leads_TalhaCRM_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [TalhaCRM_Tenants] ([TenantId]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [TalhaCRM_EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015164702_Phase2MultiTenantCore'
)
BEGIN
    ALTER TABLE [TalhaCRM_LeadStatusHistory] ADD CONSTRAINT [FK_TalhaCRM_LeadStatusHistory_TalhaCRM_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [TalhaCRM_Tenants] ([TenantId]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [TalhaCRM_EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015164702_Phase2MultiTenantCore'
)
BEGIN
    ALTER TABLE [TalhaCRM_Notifications] ADD CONSTRAINT [FK_TalhaCRM_Notifications_TalhaCRM_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [TalhaCRM_Tenants] ([TenantId]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [TalhaCRM_EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015164702_Phase2MultiTenantCore'
)
BEGIN
    INSERT INTO [TalhaCRM_EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261015164702_Phase2MultiTenantCore', N'8.0.29');
END;
GO

COMMIT;
GO

