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
                .UseSqlServer(_connectionString)
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
    }
}
