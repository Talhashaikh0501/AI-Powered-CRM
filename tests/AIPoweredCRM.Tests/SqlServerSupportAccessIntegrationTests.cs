using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AI_Powered_CRM.Data;
using AI_Powered_CRM.Models;
using AI_Powered_CRM.Models.Entities;
using AI_Powered_CRM.Models.Enums;
using AI_Powered_CRM.Security;
using AI_Powered_CRM.Services.Implementations;
using AI_Powered_CRM.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AIPoweredCRM.Tests
{
    public class SqlServerSupportAccessIntegrationTests : IAsyncLifetime
    {
        private readonly string _testDbName;
        private readonly string _connectionString;
        private DbContextOptions<ApplicationDbContext> _dbOptions = null!;

        public SqlServerSupportAccessIntegrationTests()
        {
            _testDbName = $"TalhaCRM_Test_{Guid.NewGuid():N}";
            _connectionString = $"Server=(localdb)\\MSSQLLocalDB;Database={_testDbName};Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True;Connect Timeout=30;";
        }

        public async Task InitializeAsync()
        {
            _dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlServer(_connectionString)
                .Options;

            // Apply EF Core migrations to the isolated test database
            using var context = new ApplicationDbContext(_dbOptions);
            await context.Database.MigrateAsync();
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
                // Best-effort cleanup of test database
            }
        }

        private ApplicationDbContext CreateDbContext(ITenantContext? tenantContext = null)
        {
            return new ApplicationDbContext(_dbOptions, tenantContext);
        }

        private static ApplicationUser CreateUser(string id, string username)
        {
            return new ApplicationUser
            {
                Id = id,
                UserName = username,
                NormalizedUserName = username.ToUpperInvariant(),
                Email = $"{username}@test.local",
                NormalizedEmail = $"{username.ToUpperInvariant()}@TEST.LOCAL",
                SecurityStamp = Guid.NewGuid().ToString("D"),
                ConcurrencyStamp = Guid.NewGuid().ToString("D"),
                IsActive = true
            };
        }

        [Fact]
        public async Task TC_SQL_001_Phase2_Migration_Applies_Cleanly_To_Real_SqlServer()
        {
            using var context = CreateDbContext();

            // Verify migration history contains Phase2MultiTenantCore
            var appliedMigrations = (await context.Database.GetAppliedMigrationsAsync()).ToList();
            Assert.Contains(appliedMigrations, m => m.Contains("Phase2MultiTenantCore"));

            // Verify schema tables exist in real SQL Server
            var conn = (SqlConnection)context.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open) await conn.OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES 
                WHERE TABLE_NAME IN (
                    'TalhaCRM_Tenants', 
                    'TalhaCRM_TenantMemberships', 
                    'TalhaCRM_TenantSettings', 
                    'TalhaCRM_SupportAccessTickets', 
                    'TalhaCRM_Leads'
                );";

            var foundTables = new List<string>();
            using (var reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    foundTables.Add(reader.GetString(0));
                }
            }

            Assert.Contains("TalhaCRM_Tenants", foundTables);
            Assert.Contains("TalhaCRM_TenantMemberships", foundTables);
            Assert.Contains("TalhaCRM_TenantSettings", foundTables);
            Assert.Contains("TalhaCRM_SupportAccessTickets", foundTables);
            Assert.Contains("TalhaCRM_Leads", foundTables);
        }

        [Fact]
        public async Task TC_SQL_002_Real_SqlServer_Transactional_Audit_Rollback_On_Failure()
        {
            using var context = CreateDbContext();
            context.IgnoreTenantFilter = true;

            var tenantId = Guid.NewGuid();
            var tenant = new Tenant
            {
                TenantId = tenantId,
                CompanyName = "SQL Rollback Test Tenant",
                Slug = $"sql-test-{Guid.NewGuid():N}"[..20],
                ContactEmail = "test@rollback.local",
                DefaultCurrency = "INR",
                IsActive = true
            };
            context.Tenants.Add(tenant);

            context.Users.AddRange(
                CreateUser("superadmin-sql-user", "superadmin_sql"),
                CreateUser("requester-sql-user", "requester_sql"),
                CreateUser("approver-sql-user", "approver_sql")
            );

            var ticket = new SupportAccessTicket
            {
                TicketId = Guid.NewGuid(),
                TicketNumber = $"TKT-{Guid.NewGuid():N}"[..12].ToUpperInvariant(),
                TenantId = tenantId,
                SuperAdminUserId = "superadmin-sql-user",
                CreatedByUserId = "requester-sql-user",
                Reason = "Testing SQL Server transaction rollback",
                Status = "Pending",
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddHours(4)
            };
            context.SupportAccessTickets.Add(ticket);
            await context.SaveChangesAsync();

            // Simulate atomic approval with failed audit in a real SQL Server transaction
            using (var tx = await context.Database.BeginTransactionAsync())
            {
                ticket.Status = "Approved";
                ticket.ApprovedByUserId = "approver-sql-user";
                ticket.ApprovedAt = DateTime.UtcNow;

                await context.SaveChangesAsync();

                // Rollback transaction to simulate audit failure
                await tx.RollbackAsync();

                // Reset tracked entity state
                ticket.Status = "Pending";
                ticket.ApprovedByUserId = null;
                ticket.ApprovedAt = null;
                context.Entry(ticket).State = EntityState.Unchanged;
            }

            // Verify in a completely separate DbContext instance directly against SQL Server
            using var verifyContext = CreateDbContext();
            verifyContext.IgnoreTenantFilter = true;
            var persisted = await verifyContext.SupportAccessTickets
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.TicketId == ticket.TicketId);

            Assert.NotNull(persisted);
            Assert.Equal("Pending", persisted.Status);
            Assert.Null(persisted.ApprovedByUserId);
        }

        [Fact]
        public async Task TC_SQL_003_Real_SqlServer_Concurrent_Approval_Race_Condition()
        {
            using var setupContext = CreateDbContext();
            setupContext.IgnoreTenantFilter = true;

            var tenantId = Guid.NewGuid();
            var tenant = new Tenant
            {
                TenantId = tenantId,
                CompanyName = "SQL Concurrency Test Tenant",
                Slug = $"concurrency-{Guid.NewGuid():N}"[..20],
                ContactEmail = "concurrency@test.local",
                DefaultCurrency = "INR",
                IsActive = true
            };
            setupContext.Tenants.Add(tenant);

            setupContext.Users.AddRange(
                CreateUser("superadmin-race-user", "superadmin_race"),
                CreateUser("requester-race-user", "requester_race"),
                CreateUser("admin-1", "admin1_race"),
                CreateUser("admin-2", "admin2_race")
            );

            var ticketId = Guid.NewGuid();
            var ticket = new SupportAccessTicket
            {
                TicketId = ticketId,
                TicketNumber = $"TKT-{Guid.NewGuid():N}"[..12].ToUpperInvariant(),
                TenantId = tenantId,
                SuperAdminUserId = "superadmin-race-user",
                CreatedByUserId = "requester-race-user",
                Reason = "Testing real SQL Server concurrency check",
                Status = "Pending",
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddHours(4)
            };
            setupContext.SupportAccessTickets.Add(ticket);
            await setupContext.SaveChangesAsync();

            // Launch two concurrent approval tasks against real SQL Server
            var task1 = Task.Run(async () =>
            {
                using var ctx1 = CreateDbContext();
                ctx1.IgnoreTenantFilter = true;
                var t1 = await ctx1.SupportAccessTickets.FirstAsync(t => t.TicketId == ticketId);
                t1.Status = "Approved";
                t1.ApprovedByUserId = "admin-1";
                t1.ApprovedAt = DateTime.UtcNow;
                await ctx1.SaveChangesAsync();
                return "admin-1";
            });

            var task2 = Task.Run(async () =>
            {
                using var ctx2 = CreateDbContext();
                ctx2.IgnoreTenantFilter = true;
                var t2 = await ctx2.SupportAccessTickets.FirstAsync(t => t.TicketId == ticketId);
                t2.Status = "Approved";
                t2.ApprovedByUserId = "admin-2";
                t2.ApprovedAt = DateTime.UtcNow;
                await ctx2.SaveChangesAsync();
                return "admin-2";
            });

            var results = await Task.WhenAll(
                task1.ContinueWith(t => t.IsCompletedSuccessfully),
                task2.ContinueWith(t => t.IsCompletedSuccessfully));

            // Assert: Exactly one concurrent approval succeeds against real SQL Server
            var successCount = (results[0] ? 1 : 0) + (results[1] ? 1 : 0);
            Assert.Equal(1, successCount);

            // Assert: The losing task threw a DbUpdateConcurrencyException from real SQL Server
            Exception? failureEx = results[0] ? task2.Exception?.InnerException ?? task2.Exception : task1.Exception?.InnerException ?? task1.Exception;
            Assert.NotNull(failureEx);
            Assert.IsType<DbUpdateConcurrencyException>(failureEx);

            // Verify final state in real SQL Server
            using var verifyContext = CreateDbContext();
            verifyContext.IgnoreTenantFilter = true;
            var finalTicket = await verifyContext.SupportAccessTickets.AsNoTracking().FirstAsync(t => t.TicketId == ticketId);
            Assert.Equal("Approved", finalTicket.Status);
            Assert.True(finalTicket.ApprovedByUserId == "admin-1" || finalTicket.ApprovedByUserId == "admin-2");
        }

        [Fact]
        public async Task TC_SQL_004_Real_SqlServer_CrossTenant_QueryFilter_And_Write_Isolation()
        {
            var tenantA = Guid.NewGuid();
            var tenantB = Guid.NewGuid();

            using (var seedContext = CreateDbContext())
            {
                seedContext.IgnoreTenantFilter = true;
                seedContext.Tenants.Add(new Tenant { TenantId = tenantA, CompanyName = "Tenant Alpha", Slug = $"alpha-{Guid.NewGuid():N}"[..18], ContactEmail = "a@test.com", DefaultCurrency = "INR", IsActive = true });
                seedContext.Tenants.Add(new Tenant { TenantId = tenantB, CompanyName = "Tenant Beta", Slug = $"beta-{Guid.NewGuid():N}"[..18], ContactEmail = "b@test.com", DefaultCurrency = "INR", IsActive = true });

                seedContext.Leads.Add(new Lead { TenantId = tenantA, Name = "Alpha Lead", Phone = "+91 99999 11111", Status = LeadStatus.New });
                seedContext.Leads.Add(new Lead { TenantId = tenantB, Name = "Beta Lead", Phone = "+91 99999 22222", Status = LeadStatus.New });
                await seedContext.SaveChangesAsync();
            }

            // Tenant A session context
            var tenantAContext = new TenantContext();
            tenantAContext.SetTenant(tenantA, "alpha-tenant");

            using (var queryContext = CreateDbContext(tenantAContext))
            {
                var leads = await queryContext.Leads.ToListAsync();
                Assert.Single(leads);
                Assert.Equal("Alpha Lead", leads[0].Name);
                Assert.Equal(tenantA, leads[0].TenantId);

                // Attempt cross-tenant write into Tenant B
                var crossLead = new Lead
                {
                    TenantId = tenantB,
                    Name = "Tampered Lead for Beta",
                    Phone = "+91 99999 33333",
                    Status = LeadStatus.New
                };
                queryContext.Leads.Add(crossLead);

                var ex = await Assert.ThrowsAsync<System.Security.SecurityException>(() => queryContext.SaveChangesAsync());
                Assert.Contains("Cross-tenant write rejected", ex.Message);
            }
        }
    }
}
