using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Security;
using System.Threading.Tasks;
using AI_Powered_CRM.Data;
using AI_Powered_CRM.Models;
using AI_Powered_CRM.Models.Entities;
using AI_Powered_CRM.Models.Enums;
using AI_Powered_CRM.Security;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace AIPoweredCRM.Tests
{
    public class SqlServerLegacyMigrationIntegrationTests : IAsyncLifetime
    {
        private readonly string _testDbName;
        private readonly string _connectionString;
        private DbContextOptions<ApplicationDbContext> _dbOptions = null!;

        public SqlServerLegacyMigrationIntegrationTests()
        {
            _testDbName = $"TalhaCRM_LegacyMigrate_{Guid.NewGuid():N}";
            _connectionString = $"Server=(localdb)\\MSSQLLocalDB;Database={_testDbName};Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True;Connect Timeout=30;";
        }

        public Task InitializeAsync()
        {
            _dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlServer(_connectionString, x => x.MigrationsHistoryTable("TalhaCRM_EFMigrationsHistory"))
                .Options;

            return Task.CompletedTask;
        }

        public async Task DisposeAsync()
        {
            try
            {
                using var context = new ApplicationDbContext(_dbOptions);
                await context.Database.EnsureDeletedAsync();
            }
            catch
            {
                // Best-effort cleanup
            }
        }

        private ApplicationDbContext CreateDbContext(ITenantContext? tenantContext = null)
        {
            return new ApplicationDbContext(_dbOptions, tenantContext);
        }

        [Fact]
        public async Task TC_MIG_001_Legacy_Database_Restoration_And_Phase2_Migration_Integrity()
        {
            using (var baselineContext = CreateDbContext())
            {
                // Step 1: Migrate only up to the Phase 1 Initial Baseline schema
                var migrator = baselineContext.Database.GetService<IMigrator>();
                await migrator.MigrateAsync("20261013032739_InitialCrmBaseline");
            }

            // Step 2: Seed realistic Phase 1 legacy data directly into SQL Server
            // (At baseline, tables do NOT have TenantId)
            using (var sqlConn = new SqlConnection(_connectionString))
            {
                await sqlConn.OpenAsync();

                using var cmd = sqlConn.CreateCommand();
                cmd.CommandText = @"
                    -- 1. Seed Roles
                    INSERT INTO TalhaCRM_AspNetRoles (Id, Name, NormalizedName, ConcurrencyStamp) VALUES
                    ('role-admin', 'Admin', 'ADMIN', NEWID()),
                    ('role-manager', 'Manager', 'MANAGER', NEWID()),
                    ('role-sales', 'SalesExecutive', 'SALESEXECUTIVE', NEWID()),
                    ('role-superadmin', 'PlatformSuperAdmin', 'PLATFORMSUPERADMIN', NEWID());

                    -- 2. Seed Users
                    INSERT INTO TalhaCRM_AspNetUsers (Id, FullName, CreatedAt, IsActive, UserName, NormalizedUserName, Email, NormalizedEmail, EmailConfirmed, PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnabled, AccessFailedCount) VALUES
                    ('user-admin', 'Legacy Admin', SYSUTCDATETIME(), 1, 'admin@legacy.local', 'ADMIN@LEGACY.LOCAL', 'admin@legacy.local', 'ADMIN@LEGACY.LOCAL', 1, 0, 0, 0, 0),
                    ('user-dual', 'Legacy Dual Role User', SYSUTCDATETIME(), 1, 'dual@legacy.local', 'DUAL@LEGACY.LOCAL', 'dual@legacy.local', 'DUAL@LEGACY.LOCAL', 1, 0, 0, 0, 0),
                    ('user-sales', 'Legacy Sales Rep', SYSUTCDATETIME(), 1, 'sales@legacy.local', 'SALES@LEGACY.LOCAL', 'sales@legacy.local', 'SALES@LEGACY.LOCAL', 1, 0, 0, 0, 0),
                    ('user-superadmin', 'Global Platform SuperAdmin', SYSUTCDATETIME(), 1, 'superadmin@saas.local', 'SUPERADMIN@SAAS.LOCAL', 'superadmin@saas.local', 'SUPERADMIN@SAAS.LOCAL', 1, 0, 0, 0, 0);

                    -- 3. Seed User Roles (including dual role for user-dual: Manager + SalesExecutive)
                    INSERT INTO TalhaCRM_AspNetUserRoles (UserId, RoleId) VALUES
                    ('user-admin', 'role-admin'),
                    ('user-dual', 'role-manager'),
                    ('user-dual', 'role-sales'),
                    ('user-sales', 'role-sales'),
                    ('user-superadmin', 'role-superadmin');

                    -- 4. Seed Legacy Business Records (NO TenantId column exists yet)
                    INSERT INTO TalhaCRM_Leads (Name, Email, Phone, Company, Source, InterestedProduct, Budget, Status, Priority, OwnerUserId, IsArchived, CreatedAt, UpdatedAt) VALUES
                    ('Legacy Acme Lead', 'acme@client.com', '+91 98000 11111', 'Acme Corp', 'Website', 'CRM Software', 50000.00, 'New', 'High', 'user-admin', 0, SYSUTCDATETIME(), SYSUTCDATETIME()),
                    ('Legacy Global Lead', 'global@client.com', '+91 98000 22222', 'Global Tech', 'Referral', 'Cloud Service', 75000.00, 'Contacted', 'Medium', 'user-dual', 0, SYSUTCDATETIME(), SYSUTCDATETIME()),
                    ('Legacy Retail Lead', 'retail@client.com', '+91 98000 33333', 'Retail Ltd', 'LinkedIn', 'Support Package', 120000.00, 'Converted', 'High', 'user-sales', 0, SYSUTCDATETIME(), SYSUTCDATETIME());

                    -- Retrieve one LeadId for related child records
                    DECLARE @FirstLeadId int = (SELECT TOP 1 LeadId FROM TalhaCRM_Leads WHERE Name = 'Legacy Acme Lead');

                    INSERT INTO TalhaCRM_LeadActivities (LeadId, UserId, Type, Description, OccurredAt) VALUES
                    (@FirstLeadId, 'user-admin', 'Call', 'Initial discovery call with legacy client', SYSUTCDATETIME());

                    INSERT INTO TalhaCRM_FollowUps (LeadId, AssignedUserId, Type, Notes, Status, DueAt) VALUES
                    (@FirstLeadId, 'user-admin', 'Call', 'Call back client', 'Pending', DATEADD(day, 2, SYSUTCDATETIME()));

                    INSERT INTO TalhaCRM_LeadStatusHistory (LeadId, OldStatus, NewStatus, ChangedByUserId, Notes, ChangedAt) VALUES
                    (@FirstLeadId, 'New', 'Contacted', 'user-admin', 'Called prospect', SYSUTCDATETIME());

                    INSERT INTO TalhaCRM_AILeadInsights (LeadId, Classification, Score, Summary, NextBestAction, RiskLevel, Urgency, ReasonJson, Provider, Model, GeneratedAt) VALUES
                    (@FirstLeadId, 'Hot', 85, 'High budget lead', 'Send commercial quote', 'Low', 'High', '{""need"":""immediate""}', 'Groq', 'llama-3.3-70b-versatile', SYSUTCDATETIME());

                    INSERT INTO TalhaCRM_Notifications (UserId, LeadId, Title, Message, IsRead, CreatedAt) VALUES
                    ('user-admin', @FirstLeadId, 'Legacy Notification', 'Follow-up due soon', 0, SYSUTCDATETIME());

                    INSERT INTO TalhaCRM_AuditLogs (UserId, Action, EntityType, EntityId, DetailsJson, CreatedAt) VALUES
                    ('user-admin', 'LeadCreated', 'Lead', CAST(@FirstLeadId AS nvarchar(50)), 'Legacy audit entry', SYSUTCDATETIME());
                ";
                await cmd.ExecuteNonQueryAsync();

                // Step 3: Record baseline counts before Phase 2 migration
                cmd.CommandText = @"
                    SELECT 
                        (SELECT COUNT(*) FROM TalhaCRM_AspNetUsers) AS UserCount,
                        (SELECT COUNT(*) FROM TalhaCRM_Leads) AS LeadCount,
                        (SELECT COUNT(*) FROM TalhaCRM_LeadActivities) AS ActivityCount,
                        (SELECT COUNT(*) FROM TalhaCRM_FollowUps) AS FollowUpCount,
                        (SELECT COUNT(*) FROM TalhaCRM_LeadStatusHistory) AS HistoryCount,
                        (SELECT COUNT(*) FROM TalhaCRM_AILeadInsights) AS InsightCount,
                        (SELECT COUNT(*) FROM TalhaCRM_Notifications) AS NotificationCount,
                        (SELECT COUNT(*) FROM TalhaCRM_AuditLogs) AS AuditCount;
                ";

                using var reader = await cmd.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                var preUserCount = reader.GetInt32(0);
                var preLeadCount = reader.GetInt32(1);
                var preActivityCount = reader.GetInt32(2);
                var preFollowUpCount = reader.GetInt32(3);
                var preHistoryCount = reader.GetInt32(4);
                var preInsightCount = reader.GetInt32(5);
                var preNotificationCount = reader.GetInt32(6);
                var preAuditCount = reader.GetInt32(7);

                Assert.Equal(4, preUserCount);
                Assert.Equal(3, preLeadCount);
                Assert.Equal(1, preActivityCount);
                Assert.Equal(1, preFollowUpCount);
                Assert.Equal(1, preHistoryCount);
                Assert.Equal(1, preInsightCount);
                Assert.Equal(1, preNotificationCount);
                Assert.Equal(1, preAuditCount);
            }

            // Step 4: Execute Phase 2 Migration onto the existing populated database
            using (var migrationContext = CreateDbContext())
            {
                var migrator = migrationContext.Database.GetService<IMigrator>();
                await migrator.MigrateAsync("20261015164702_Phase2MultiTenantCore");
            }

            // Step 5: Post-migration Verification
            var primaryTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

            using (var verifyContext = CreateDbContext())
            {
                verifyContext.IgnoreTenantFilter = true;

                // 5.1 Verify Zero Record Loss
                var postUsers = await verifyContext.Users.CountAsync();
                var postLeads = await verifyContext.Leads.CountAsync();
                var postActivities = await verifyContext.LeadActivities.CountAsync();
                var postFollowUps = await verifyContext.FollowUps.CountAsync();
                var postHistory = await verifyContext.LeadStatusHistories.CountAsync();
                var postInsights = await verifyContext.AILeadInsights.CountAsync();
                var postNotifications = await verifyContext.Notifications.CountAsync();
                var postAudits = await verifyContext.AuditLogs.CountAsync();

                Assert.Equal(4, postUsers);
                Assert.Equal(3, postLeads);
                Assert.Equal(1, postActivities);
                Assert.Equal(1, postFollowUps);
                Assert.Equal(1, postHistory);
                Assert.Equal(1, postInsights);
                Assert.Equal(1, postNotifications);
                Assert.Equal(1, postAudits);

                // 5.2 Verify TenantId Backfilled to Primary Tenant for ALL records
                Assert.True(await verifyContext.Leads.AllAsync(l => l.TenantId == primaryTenantId));
                Assert.True(await verifyContext.LeadActivities.AllAsync(a => a.TenantId == primaryTenantId));
                Assert.True(await verifyContext.FollowUps.AllAsync(f => f.TenantId == primaryTenantId));
                Assert.True(await verifyContext.LeadStatusHistories.AllAsync(h => h.TenantId == primaryTenantId));
                Assert.True(await verifyContext.AILeadInsights.AllAsync(i => i.TenantId == primaryTenantId));
                Assert.True(await verifyContext.Notifications.AllAsync(n => n.TenantId == primaryTenantId));
                Assert.True(await verifyContext.AuditLogs.AllAsync(a => a.TenantId == primaryTenantId));

                // 5.3 Verify Primary Tenant & Settings created with INR default
                var tenant = await verifyContext.Tenants.FirstOrDefaultAsync(t => t.TenantId == primaryTenantId);
                Assert.NotNull(tenant);
                Assert.Equal("INR", tenant.DefaultCurrency);
                Assert.True(tenant.IsActive);

                var settings = await verifyContext.TenantSettings.FirstOrDefaultAsync(s => s.TenantId == primaryTenantId);
                Assert.NotNull(settings);
                Assert.Equal("INR", settings.CurrencyCode);
                Assert.Equal("₹", settings.CurrencySymbol);

                // 5.4 Verify Identity Role Mappings & Tenant Memberships
                var memberships = await verifyContext.TenantMemberships.ToListAsync();

                // Assert Admin -> CompanyAdmin
                var adminMember = memberships.FirstOrDefault(m => m.UserId == "user-admin");
                Assert.NotNull(adminMember);
                Assert.Equal(primaryTenantId, adminMember.TenantId);
                Assert.Equal(CompanyRole.CompanyAdmin, adminMember.CompanyRole);
                Assert.True(adminMember.IsActive);

                // Assert Dual Role (Manager + SalesExecutive) -> Highest role Manager (no duplicate membership!)
                var dualMember = memberships.FirstOrDefault(m => m.UserId == "user-dual");
                Assert.NotNull(dualMember);
                Assert.Equal(primaryTenantId, dualMember.TenantId);
                Assert.Equal(CompanyRole.Manager, dualMember.CompanyRole);
                Assert.True(dualMember.IsActive);
                Assert.Single(memberships.Where(m => m.UserId == "user-dual"));

                // Assert SalesExecutive -> SalesExecutive
                var salesMember = memberships.FirstOrDefault(m => m.UserId == "user-sales");
                Assert.NotNull(salesMember);
                Assert.Equal(primaryTenantId, salesMember.TenantId);
                Assert.Equal(CompanyRole.SalesExecutive, salesMember.CompanyRole);
                Assert.True(salesMember.IsActive);

                // Assert PlatformSuperAdmin was NOT given company membership
                var superAdminMember = memberships.FirstOrDefault(m => m.UserId == "user-superadmin");
                Assert.Null(superAdminMember);
                Assert.Equal(3, memberships.Count); // Exactly 3 company memberships, zero for SuperAdmin
            }

            // Step 6: Verify Foreign Keys Exist in SQL Server Schema
            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT fk.name AS ForeignKeyName, 
                           tp.name AS ParentTable, 
                           tr.name AS ReferencedTable
                    FROM sys.foreign_keys fk
                    INNER JOIN sys.tables tp ON fk.parent_object_id = tp.object_id
                    INNER JOIN sys.tables tr ON fk.referenced_object_id = tr.object_id
                    WHERE tp.name IN ('TalhaCRM_Leads', 'TalhaCRM_TenantMemberships', 'TalhaCRM_TenantSettings');
                ";

                var foreignKeys = new List<string>();
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        foreignKeys.Add($"{reader.GetString(1)}->{reader.GetString(2)}");
                    }
                }

                Assert.Contains("TalhaCRM_Leads->TalhaCRM_Tenants", foreignKeys);
                Assert.Contains("TalhaCRM_TenantMemberships->TalhaCRM_Tenants", foreignKeys);
                Assert.Contains("TalhaCRM_TenantSettings->TalhaCRM_Tenants", foreignKeys);
            }

            // Step 7: Verify Tenant Isolation against the migrated data
            var primaryContext = new TenantContext();
            primaryContext.SetTenant(primaryTenantId, "primary-tenant");

            var secondTenantId = Guid.NewGuid();
            var secondContext = new TenantContext();
            secondContext.SetTenant(secondTenantId, "second-tenant");

            // Querying with primary tenant context sees all 3 migrated leads
            using (var primaryDb = CreateDbContext(primaryContext))
            {
                var leads = await primaryDb.Leads.ToListAsync();
                Assert.Equal(3, leads.Count);
                Assert.All(leads, l => Assert.Equal(primaryTenantId, l.TenantId));

                // Cross-tenant write rejection check
                var illegalCrossLead = new Lead
                {
                    TenantId = secondTenantId,
                    Name = "Illegal Cross-Tenant Lead",
                    Phone = "+91 99999 88888",
                    Status = LeadStatus.New
                };
                primaryDb.Leads.Add(illegalCrossLead);

                var ex = await Assert.ThrowsAsync<SecurityException>(() => primaryDb.SaveChangesAsync());
                Assert.Contains("Cross-tenant write rejected", ex.Message);
            }

            // Querying with second tenant context sees 0 leads
            using (var secondDb = CreateDbContext(secondContext))
            {
                var leads = await secondDb.Leads.ToListAsync();
                Assert.Empty(leads);
            }
        }

        [Fact]
        public async Task TC_MIG_002_Snapshot_Verification_And_Dual_Phase_Recovery_Integrity()
        {
            var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "AI_Powered_CRM.sln")))
            {
                dir = dir.Parent;
            }
            Assert.NotNull(dir);
            var projectRoot = dir.FullName;
            var createSnapshotPath = System.IO.Path.Combine(projectRoot, "scripts", "create_indb_snapshots.sql");
            var restoreSnapshotPath = System.IO.Path.Combine(projectRoot, "scripts", "restore_from_indb_snapshots.sql");

            Assert.True(System.IO.File.Exists(createSnapshotPath), $"Missing {createSnapshotPath}");
            Assert.True(System.IO.File.Exists(restoreSnapshotPath), $"Missing {restoreSnapshotPath}");

            // Step 1: Migrate up to Phase 1 Baseline
            using (var baselineContext = CreateDbContext())
            {
                var migrator = baselineContext.Database.GetService<IMigrator>();
                await migrator.MigrateAsync("20261013032739_InitialCrmBaseline");
            }

            // Step 2: Seed Phase 1 baseline data
            using (var sqlConn = new SqlConnection(_connectionString))
            {
                await sqlConn.OpenAsync();

                using var seedCmd = sqlConn.CreateCommand();
                seedCmd.CommandText = @"
                    INSERT INTO TalhaCRM_AspNetRoles (Id, Name, NormalizedName, ConcurrencyStamp) VALUES
                    ('role-admin', 'Admin', 'ADMIN', NEWID()),
                    ('role-manager', 'Manager', 'MANAGER', NEWID()),
                    ('role-sales', 'SalesExecutive', 'SALESEXECUTIVE', NEWID());

                    INSERT INTO TalhaCRM_AspNetUsers (Id, FullName, CreatedAt, IsActive, UserName, NormalizedUserName, Email, NormalizedEmail, EmailConfirmed, PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnabled, AccessFailedCount) VALUES
                    ('user-admin', 'Admin User', SYSUTCDATETIME(), 1, 'admin@test.local', 'ADMIN@TEST.LOCAL', 'admin@test.local', 'ADMIN@TEST.LOCAL', 1, 0, 0, 0, 0),
                    ('user-sales', 'Sales User', SYSUTCDATETIME(), 1, 'sales@test.local', 'SALES@TEST.LOCAL', 'sales@test.local', 'SALES@TEST.LOCAL', 1, 0, 0, 0, 0);

                    INSERT INTO TalhaCRM_AspNetUserRoles (UserId, RoleId) VALUES
                    ('user-admin', 'role-admin'),
                    ('user-sales', 'role-sales');

                    INSERT INTO TalhaCRM_Leads (Name, Email, Phone, Company, Source, InterestedProduct, Budget, Status, Priority, OwnerUserId, IsArchived, CreatedAt, UpdatedAt) VALUES
                    ('Alpha Corp', 'alpha@corp.local', '+91 91111 22222', 'Alpha Corp', 'Web', 'CRM', 40000.00, 'New', 'High', 'user-admin', 0, SYSUTCDATETIME(), SYSUTCDATETIME()),
                    ('Beta Ltd', 'beta@ltd.local', '+91 93333 44444', 'Beta Ltd', 'Referral', 'Cloud', 60000.00, 'Contacted', 'Medium', 'user-sales', 0, SYSUTCDATETIME(), SYSUTCDATETIME());

                    DECLARE @AlphaLeadId int = (SELECT TOP 1 LeadId FROM TalhaCRM_Leads WHERE Name = 'Alpha Corp');

                    INSERT INTO TalhaCRM_LeadActivities (LeadId, UserId, Type, Description, OccurredAt) VALUES
                    (@AlphaLeadId, 'user-admin', 'Call', 'Alpha kickoff call', SYSUTCDATETIME());

                    INSERT INTO TalhaCRM_FollowUps (LeadId, AssignedUserId, Type, Notes, Status, DueAt) VALUES
                    (@AlphaLeadId, 'user-admin', 'Call', 'Alpha follow up', 'Pending', DATEADD(day, 1, SYSUTCDATETIME()));

                    INSERT INTO TalhaCRM_AILeadInsights (LeadId, Classification, Score, Summary, NextBestAction, RiskLevel, Urgency, ReasonJson, Provider, Model, GeneratedAt) VALUES
                    (@AlphaLeadId, 'Warm', 75, 'Solid opportunity', 'Send quote', 'Low', 'Medium', '{}', 'Groq', 'llama-3.3-70b-versatile', SYSUTCDATETIME());

                    INSERT INTO TalhaCRM_Notifications (UserId, LeadId, Title, Message, IsRead, CreatedAt) VALUES
                    ('user-admin', @AlphaLeadId, 'Kickoff alert', 'Kickoff completed', 0, SYSUTCDATETIME());

                    INSERT INTO TalhaCRM_AuditLogs (UserId, Action, EntityType, EntityId, DetailsJson, CreatedAt) VALUES
                    ('user-admin', 'Create', 'Lead', CAST(@AlphaLeadId AS nvarchar(50)), '{}', SYSUTCDATETIME());
                ";
                await seedCmd.ExecuteNonQueryAsync();

                // Step 3: Run create_indb_snapshots.sql
                var snapshotSql = await System.IO.File.ReadAllTextAsync(createSnapshotPath);
                using var snapCmd = sqlConn.CreateCommand();
                snapCmd.CommandText = snapshotSql;
                var snapshotSuffixObj = await snapCmd.ExecuteScalarAsync();
                Assert.NotNull(snapshotSuffixObj);
                var snapshotSuffix = snapshotSuffixObj.ToString()!;
                Assert.False(string.IsNullOrWhiteSpace(snapshotSuffix));

                // 3.1 Verify status is PendingVerification initially
                using var checkCmd = sqlConn.CreateCommand();
                checkCmd.CommandText = "SELECT Status FROM dbo.TalhaCRM_BKP_SnapshotMetadata WHERE SnapshotSuffix = @suffix";
                checkCmd.Parameters.AddWithValue("@suffix", snapshotSuffix);
                var status = (string)(await checkCmd.ExecuteScalarAsync())!;
                Assert.Equal("PendingVerification", status);

                // 3.2 Verify row counts match before marking Verified
                checkCmd.CommandText = $"SELECT COUNT(*) FROM dbo.TalhaCRM_BKP_{snapshotSuffix}_Leads";
                var bkpLeadCount = (int)(await checkCmd.ExecuteScalarAsync())!;
                Assert.Equal(2, bkpLeadCount);

                checkCmd.CommandText = $"SELECT COUNT(*) FROM dbo.TalhaCRM_BKP_{snapshotSuffix}_AspNetUsers";
                var bkpUserCount = (int)(await checkCmd.ExecuteScalarAsync())!;
                Assert.Equal(2, bkpUserCount);

                // Update to Verified (matching apply_phase2_migration.ps1 Step 2 behavior)
                checkCmd.CommandText = "UPDATE dbo.TalhaCRM_BKP_SnapshotMetadata SET Status = 'Verified' WHERE SnapshotSuffix = @suffix";
                await checkCmd.ExecuteNonQueryAsync();
            }

            // Step 4: Apply Phase 2 Migration
            using (var migrationContext = CreateDbContext())
            {
                var migrator = migrationContext.Database.GetService<IMigrator>();
                await migrator.MigrateAsync("20261015164702_Phase2MultiTenantCore");
            }

            var primaryTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

            // Verify Phase 2 schema is active
            using (var verifyContext = CreateDbContext())
            {
                verifyContext.IgnoreTenantFilter = true;
                Assert.Equal(2, await verifyContext.Leads.CountAsync());
                Assert.Equal(2, await verifyContext.Users.CountAsync());
                Assert.Equal(2, await verifyContext.TenantMemberships.CountAsync());
            }

            // Step 5: Test Recovery Process on Phase 2 Database
            using (var sqlConn = new SqlConnection(_connectionString))
            {
                await sqlConn.OpenAsync();

                // Read snapshot suffix
                using var getSuffixCmd = sqlConn.CreateCommand();
                getSuffixCmd.CommandText = "SELECT TOP 1 SnapshotSuffix FROM dbo.TalhaCRM_BKP_SnapshotMetadata ORDER BY SnapshotId DESC";
                var snapshotSuffix = (string)(await getSuffixCmd.ExecuteScalarAsync())!;

                // Mutate/add records in Phase 2 to simulate partial or failed state
                using var mutateCmd = sqlConn.CreateCommand();
                mutateCmd.CommandText = @"
                    INSERT INTO TalhaCRM_Leads (Name, Email, Phone, Company, Source, InterestedProduct, Budget, Status, Priority, OwnerUserId, IsArchived, CreatedAt, UpdatedAt, TenantId)
                    VALUES ('Corrupted Unwanted Lead', 'corrupt@bad.local', '+91 99999 00000', 'Bad Corp', 'Web', 'None', 100.00, 'New', 'Low', 'user-admin', 0, SYSUTCDATETIME(), SYSUTCDATETIME(), '11111111-1111-1111-1111-111111111111');
                ";
                await mutateCmd.ExecuteNonQueryAsync();

                // Test Safety Abort without @CONFIRM_RESTORE
                var restoreScriptRaw = await System.IO.File.ReadAllTextAsync(restoreSnapshotPath);
                using var failCmd = sqlConn.CreateCommand();
                failCmd.CommandText = restoreScriptRaw.Replace("@SnapshotSuffix NVARCHAR(50) = '';", $"@SnapshotSuffix NVARCHAR(50) = '{snapshotSuffix}';");
                var ex = await Assert.ThrowsAsync<SqlException>(() => failCmd.ExecuteNonQueryAsync());
                Assert.Contains("SAFETY ABORT", ex.Message);

                // Execute authorized recovery on Phase 2 schema
                using var restoreCmd = sqlConn.CreateCommand();
                var authorizedRestoreScript = restoreScriptRaw
                    .Replace("@CONFIRM_RESTORE VARCHAR(50) = 'NO';", "@CONFIRM_RESTORE VARCHAR(50) = 'YES_RESTORE_TALHACRM_TABLES';")
                    .Replace("@SnapshotSuffix NVARCHAR(50) = '';", $"@SnapshotSuffix NVARCHAR(50) = '{snapshotSuffix}';");

                restoreCmd.CommandText = authorizedRestoreScript;
                await restoreCmd.ExecuteNonQueryAsync();

                // Verify DBCC CHECKCONSTRAINTS passes with zero violations
                using var checkFkCmd = sqlConn.CreateCommand();
                checkFkCmd.CommandText = "DBCC CHECKCONSTRAINTS WITH ALL_CONSTRAINTS;";
                using var fkReader = await checkFkCmd.ExecuteReaderAsync();
                Assert.False(await fkReader.ReadAsync(), "Foreign key check detected integrity violations!");
            }

            // Step 6: Verify Data and Schema Integrity after Phase 2 Recovery
            using (var postRecoveryContext = CreateDbContext())
            {
                postRecoveryContext.IgnoreTenantFilter = true;

                // 6.1 Users are preserved without ID or hash destruction
                var users = await postRecoveryContext.Users.OrderBy(u => u.Id).ToListAsync();
                Assert.Equal(2, users.Count);
                Assert.Equal("user-admin", users[0].Id);
                Assert.Equal("user-sales", users[1].Id);

                // 6.2 Memberships are intact
                var memberships = await postRecoveryContext.TenantMemberships.ToListAsync();
                Assert.Equal(2, memberships.Count);
                Assert.All(memberships, m => Assert.Equal(primaryTenantId, m.TenantId));

                // 6.3 Leads restored exactly to 2 snapshot leads (the corrupted lead is gone!)
                var leads = await postRecoveryContext.Leads.OrderBy(l => l.Name).ToListAsync();
                Assert.Equal(2, leads.Count);
                Assert.Equal("Alpha Corp", leads[0].Name);
                Assert.Equal("Beta Ltd", leads[1].Name);
                Assert.All(leads, l => Assert.Equal(primaryTenantId, l.TenantId));

                // 6.4 Activities, Followups, Insights, Notifications, Audits restored
                Assert.Equal(1, await postRecoveryContext.LeadActivities.CountAsync());
                Assert.Equal(1, await postRecoveryContext.FollowUps.CountAsync());
                Assert.Equal(1, await postRecoveryContext.AILeadInsights.CountAsync());
                Assert.Equal(1, await postRecoveryContext.Notifications.CountAsync());
                Assert.Equal(1, await postRecoveryContext.AuditLogs.CountAsync());
            }

            // Step 7: Test Recovery on Phase 1 Rolled-back Schema
            using (var rollbackContext = CreateDbContext())
            {
                var migrator = rollbackContext.Database.GetService<IMigrator>();
                await migrator.MigrateAsync("20261013032739_InitialCrmBaseline");
            }

            using (var sqlConn = new SqlConnection(_connectionString))
            {
                await sqlConn.OpenAsync();

                using var getSuffixCmd = sqlConn.CreateCommand();
                getSuffixCmd.CommandText = "SELECT TOP 1 SnapshotSuffix FROM dbo.TalhaCRM_BKP_SnapshotMetadata ORDER BY SnapshotId DESC";
                var snapshotSuffix = (string)(await getSuffixCmd.ExecuteScalarAsync())!;

                var restoreScriptRaw = await System.IO.File.ReadAllTextAsync(restoreSnapshotPath);
                var authorizedRestoreScript = restoreScriptRaw
                    .Replace("@CONFIRM_RESTORE VARCHAR(50) = 'NO';", "@CONFIRM_RESTORE VARCHAR(50) = 'YES_RESTORE_TALHACRM_TABLES';")
                    .Replace("@SnapshotSuffix NVARCHAR(50) = '';", $"@SnapshotSuffix NVARCHAR(50) = '{snapshotSuffix}';");

                using var restoreCmd = sqlConn.CreateCommand();
                restoreCmd.CommandText = authorizedRestoreScript;
                await restoreCmd.ExecuteNonQueryAsync();

                // Verify row counts in Phase 1 baseline schema
                using var countCmd = sqlConn.CreateCommand();
                countCmd.CommandText = "SELECT (SELECT COUNT(*) FROM TalhaCRM_Leads), (SELECT COUNT(*) FROM TalhaCRM_AspNetUsers);";
                using var reader = await countCmd.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal(2, reader.GetInt32(0));
                Assert.Equal(2, reader.GetInt32(1));
            }
        }
    }
}
