using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AI_Powered_CRM.AI;
using AI_Powered_CRM.Data;
using AI_Powered_CRM.DTOs;
using AI_Powered_CRM.Models;
using AI_Powered_CRM.Models.Entities;
using AI_Powered_CRM.Models.Enums;
using AI_Powered_CRM.Security;
using AI_Powered_CRM.Services.Implementations;
using AI_Powered_CRM.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AIPoweredCRM.Tests
{
    public class SqlServerLeadAssignmentConcurrencyIntegrationTests : IAsyncLifetime
    {
        private readonly string _testDbName;
        private readonly string _connectionString;
        private DbContextOptions<ApplicationDbContext> _dbOptions = null!;

        public SqlServerLeadAssignmentConcurrencyIntegrationTests()
        {
            _testDbName = $"TalhaCRM_LockTest_{Guid.NewGuid():N}";
            _connectionString = $"Server=(localdb)\\MSSQLLocalDB;Database={_testDbName};Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True;Connect Timeout=30;";
        }

        public async Task InitializeAsync()
        {
            _dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlServer(_connectionString)
                .Options;

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

        [Fact]
        public async Task TC_SQLSERVER_LOCK_001_IndependentLockingPerTenant_ParallelExecution()
        {
            var tenantA = Guid.NewGuid();
            var tenantB = Guid.NewGuid();

            using var context1 = CreateDbContext();
            using var context2 = CreateDbContext();

            var service1 = new LeadAssignmentService(context1, new MockAuditService(), NullLogger<LeadAssignmentService>.Instance);
            var service2 = new LeadAssignmentService(context2, new MockAuditService(), NullLogger<LeadAssignmentService>.Instance);

            // Connection 1 acquires lock on Tenant A
            await using var lockA = await service1.AcquireTenantAssignmentLockAsync(tenantA);

            // Connection 2 simultaneously acquires lock on Tenant B without blocking
            var acquireTaskB = service2.AcquireTenantAssignmentLockAsync(tenantB, TimeSpan.FromSeconds(5));
            var completedTask = await Task.WhenAny(acquireTaskB, Task.Delay(2000));

            Assert.Equal(acquireTaskB, completedTask); // Must complete immediately without waiting for Tenant A
            await using var lockB = await acquireTaskB;

            Assert.NotNull(lockA);
            Assert.NotNull(lockB);
        }

        [Fact]
        public async Task TC_SQLSERVER_LOCK_002_SameTenantBlocksUntilReleased_AcrossSeparateConnections()
        {
            var tenantId = Guid.NewGuid();

            using var context1 = CreateDbContext();
            using var context2 = CreateDbContext();

            var service1 = new LeadAssignmentService(context1, new MockAuditService(), NullLogger<LeadAssignmentService>.Instance);
            var service2 = new LeadAssignmentService(context2, new MockAuditService(), NullLogger<LeadAssignmentService>.Instance);

            // Connection 1 acquires lock
            var lock1 = await service1.AcquireTenantAssignmentLockAsync(tenantId);

            // Connection 2 attempts to acquire lock on the same tenant with 500ms timeout -> must time out
            await Assert.ThrowsAsync<TimeoutException>(async () =>
            {
                await service2.AcquireTenantAssignmentLockAsync(tenantId, TimeSpan.FromMilliseconds(500));
            });

            // Connection 1 releases the lock
            await lock1.DisposeAsync();

            // Connection 2 can now acquire the lock immediately
            await using var lock2 = await service2.AcquireTenantAssignmentLockAsync(tenantId, TimeSpan.FromSeconds(5));
            Assert.NotNull(lock2);
        }

        [Fact]
        public async Task TC_SQLSERVER_CONCURRENCY_003_MultiConnectionParallelLeadCreation_DistributesFairly()
        {
            var tenantId = Guid.NewGuid();

            // Seed tenant, settings, and 2 active sales reps in the database
            using (var seedContext = CreateDbContext())
            {
                seedContext.IgnoreTenantFilter = true;
                seedContext.Tenants.Add(new Tenant
                {
                    TenantId = tenantId,
                    CompanyName = "Concurrency Test Corp",
                    Slug = "conc-" + Guid.NewGuid().ToString("N")[..6],
                    ContactEmail = "conc@corp.in",
                    DefaultCurrency = "INR",
                    SubscriptionTier = SubscriptionTier.Professional,
                    SubscriptionStatus = SubscriptionStatus.Active,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                });

                seedContext.TenantSettings.Add(new TenantSetting
                {
                    TenantId = tenantId,
                    LeadAssignmentMode = "RoundRobin",
                    CurrencyCode = "INR",
                    CurrencySymbol = "₹"
                });

                var rep1 = new ApplicationUser
                {
                    Id = "sqllock_rep1_" + Guid.NewGuid().ToString("N"),
                    UserName = "rep1@conc.in",
                    Email = "rep1@conc.in",
                    FullName = "Sales Agent One",
                    IsActive = true,
                    EmailConfirmed = true,
                    SecurityStamp = Guid.NewGuid().ToString()
                };
                var rep2 = new ApplicationUser
                {
                    Id = "sqllock_rep2_" + Guid.NewGuid().ToString("N"),
                    UserName = "rep2@conc.in",
                    Email = "rep2@conc.in",
                    FullName = "Sales Agent Two",
                    IsActive = true,
                    EmailConfirmed = true,
                    SecurityStamp = Guid.NewGuid().ToString()
                };

                var adminUser = new ApplicationUser
                {
                    Id = "admin_user",
                    UserName = "admin@conc.in",
                    Email = "admin@conc.in",
                    FullName = "Admin User",
                    IsActive = true,
                    EmailConfirmed = true,
                    SecurityStamp = Guid.NewGuid().ToString()
                };

                seedContext.Users.AddRange(adminUser, rep1, rep2);
                seedContext.TenantMemberships.AddRange(
                    new TenantMembership
                    {
                        MembershipId = Guid.NewGuid(),
                        TenantId = tenantId,
                        UserId = adminUser.Id,
                        CompanyRole = CompanyRole.CompanyAdmin,
                        IsActive = true,
                        JoinedAt = DateTime.UtcNow
                    },
                    new TenantMembership
                    {
                        MembershipId = Guid.NewGuid(),
                        TenantId = tenantId,
                        UserId = rep1.Id,
                        CompanyRole = CompanyRole.SalesExecutive,
                        IsActive = true,
                        JoinedAt = DateTime.UtcNow
                    },
                    new TenantMembership
                    {
                        MembershipId = Guid.NewGuid(),
                        TenantId = tenantId,
                        UserId = rep2.Id,
                        CompanyRole = CompanyRole.SalesExecutive,
                        IsActive = true,
                        JoinedAt = DateTime.UtcNow
                    }
                );

                await seedContext.SaveChangesAsync();
            }

            // Launch 4 simultaneous lead creations each using a completely separate DbContext instance
            // simulating 4 concurrent web requests arriving across multiple server instances
            var tasks = Enumerable.Range(1, 4).Select(async i =>
            {
                var tenantContext = new TenantContext();
                tenantContext.SetTenant(tenantId, "conc");

                using var reqContext = CreateDbContext(tenantContext);
                var auditService = new MockAuditService();
                var assignmentService = new LeadAssignmentService(reqContext, auditService, NullLogger<LeadAssignmentService>.Instance);
                var aiService = new MockAIService();
                var notificationService = new MockNotificationService();

                var leadService = new LeadService(
                    reqContext,
                    aiService,
                    auditService,
                    notificationService,
                    assignmentService,
                    tenantContext,
                    NullLogger<LeadService>.Instance);

                var dto = new CreateLeadDto
                {
                    Name = $"Parallel Lead {i}",
                    Phone = $"+91 99000000{i:D2}",
                    Email = $"lead{i}@client.com",
                    Source = "Website",
                    InterestedProduct = "ERP",
                    Budget = 50000m,
                    Status = LeadStatus.New,
                    Priority = LeadPriority.Medium
                };

                return await leadService.CreateLeadAsync(dto, "admin_user", "Admin");
            });

            var createdLeads = await Task.WhenAll(tasks);

            Assert.Equal(4, createdLeads.Length);

            // Query database to verify distribution
            using (var verifyContext = CreateDbContext())
            {
                verifyContext.IgnoreTenantFilter = true;
                var dbLeads = await verifyContext.Leads.Where(l => l.TenantId == tenantId).ToListAsync();
                Assert.Equal(4, dbLeads.Count);

                var repGroups = dbLeads.GroupBy(l => l.OwnerUserId).ToDictionary(g => g.Key!, g => g.Count());
                Assert.Equal(2, repGroups.Count);

                // Both sales reps must have received exactly 2 leads each
                foreach (var count in repGroups.Values)
                {
                    Assert.Equal(2, count);
                }
            }
        }

        [Fact]
        public async Task TC_SQLSERVER_LOCK_004_LockTimeout_FailsClosed_AndReleasesLocalSemaphore()
        {
            var tenantId = Guid.NewGuid();

            using var context1 = CreateDbContext();
            using var context2 = CreateDbContext();

            // Simulate another instance acquiring the distributed SQL lock on Connection 1 directly
            var conn1 = context1.Database.GetDbConnection();
            if (conn1.State != System.Data.ConnectionState.Open)
            {
                await conn1.OpenAsync();
            }

            using var cmd1 = conn1.CreateCommand();
            cmd1.CommandText = @"
                DECLARE @res int;
                EXEC @res = sp_getapplock 
                    @Resource = @Resource, 
                    @LockMode = 'Exclusive', 
                    @LockOwner = 'Session', 
                    @LockTimeout = 15000;
                SELECT @res;";
            var pRes = cmd1.CreateParameter();
            pRes.ParameterName = "@Resource";
            pRes.Value = $"TalhaCRM_Assign_{tenantId:N}";
            cmd1.Parameters.Add(pRes);
            var lockGranted = Convert.ToInt32(await cmd1.ExecuteScalarAsync());
            Assert.True(lockGranted >= 0, "External instance must successfully acquire initial SQL applock.");

            // Instance 2 attempts acquisition with 300ms timeout -> MUST time out on SQL Server sp_getapplock (return code -1)
            var service2 = new LeadAssignmentService(context2, new MockAuditService(), NullLogger<LeadAssignmentService>.Instance);
            var ex = await Assert.ThrowsAsync<TimeoutException>(async () =>
            {
                await service2.AcquireTenantAssignmentLockAsync(tenantId, TimeSpan.FromMilliseconds(300));
            });
            Assert.Contains("sp_getapplock return code: -1", ex.Message);

            // Release lock on Connection 1
            using var releaseCmd = conn1.CreateCommand();
            releaseCmd.CommandText = "EXEC sp_releaseapplock @Resource = @Resource, @LockOwner = 'Session';";
            var pRel = releaseCmd.CreateParameter();
            pRel.ParameterName = "@Resource";
            pRel.Value = $"TalhaCRM_Assign_{tenantId:N}";
            releaseCmd.Parameters.Add(pRel);
            await releaseCmd.ExecuteNonQueryAsync();

            // Crucial check: Instance 2's local semaphore MUST have been released when SQL timeout occurred.
            // If local semaphore was leaked, service2 would deadlock here.
            await using var lock2 = await service2.AcquireTenantAssignmentLockAsync(tenantId, TimeSpan.FromSeconds(5));
            Assert.NotNull(lock2);
        }

        [Fact]
        public async Task TC_SQLSERVER_LOCK_005_ExceptionDuringPersistence_RollsBackInFlightReservation_AndReleasesLock()
        {
            var tenantId = Guid.NewGuid();
            var repId = "sqllock_rollback_rep_" + Guid.NewGuid().ToString("N");

            // Seed tenant and rep
            using (var seedContext = CreateDbContext())
            {
                seedContext.IgnoreTenantFilter = true;
                seedContext.Tenants.Add(new Tenant
                {
                    TenantId = tenantId,
                    CompanyName = "Rollback Test Corp",
                    Slug = "roll-" + Guid.NewGuid().ToString("N")[..6],
                    ContactEmail = "roll@corp.in",
                    DefaultCurrency = "INR",
                    SubscriptionTier = SubscriptionTier.Starter,
                    SubscriptionStatus = SubscriptionStatus.Active,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                });
                seedContext.TenantSettings.Add(new TenantSetting
                {
                    TenantId = tenantId,
                    LeadAssignmentMode = "RoundRobin",
                    CurrencyCode = "INR",
                    CurrencySymbol = "₹"
                });
                var rep = new ApplicationUser
                {
                    Id = repId,
                    UserName = "rollback_rep@test.in",
                    Email = "rollback_rep@test.in",
                    FullName = "Rollback Rep",
                    IsActive = true,
                    EmailConfirmed = true,
                    SecurityStamp = Guid.NewGuid().ToString()
                };
                seedContext.Users.Add(rep);
                seedContext.TenantMemberships.Add(new TenantMembership
                {
                    MembershipId = Guid.NewGuid(),
                    TenantId = tenantId,
                    UserId = rep.Id,
                    CompanyRole = CompanyRole.SalesExecutive,
                    IsActive = true,
                    JoinedAt = DateTime.UtcNow
                });
                await seedContext.SaveChangesAsync();
            }

            var tenantContext = new TenantContext();
            tenantContext.SetTenant(tenantId, "roll");

            using var reqContext = CreateDbContext(tenantContext);
            var assignmentService = new LeadAssignmentService(reqContext, new MockAuditService(), NullLogger<LeadAssignmentService>.Instance);

            // 1. Acquire lock and determine owner (increments in-flight reservation counter)
            string assignedOwner;
            await using (await assignmentService.AcquireTenantAssignmentLockAsync(tenantId))
            {
                assignedOwner = await assignmentService.DetermineLeadOwnerAsync(tenantId, null, "admin_user", acquireLock: false);
                Assert.Equal(repId, assignedOwner);

                // Simulate persistence failure or exception
                assignmentService.RollbackInFlightReservation(tenantId, assignedOwner);
            }

            // 2. Verify that after lock release, a subsequent lock acquisition succeeds without deadlock
            await using var secondLock = await assignmentService.AcquireTenantAssignmentLockAsync(tenantId);
            Assert.NotNull(secondLock);
        }

        [Fact]
        public async Task TC_SQLSERVER_LOCK_006_DisposedLockScope_ReleasesSqlApplockAndSemaphore()
        {
            var tenantId = Guid.NewGuid();

            using var context1 = CreateDbContext();
            using var context2 = CreateDbContext();

            var service1 = new LeadAssignmentService(context1, new MockAuditService(), NullLogger<LeadAssignmentService>.Instance);
            var service2 = new LeadAssignmentService(context2, new MockAuditService(), NullLogger<LeadAssignmentService>.Instance);

            // Scope block
            await using (await service1.AcquireTenantAssignmentLockAsync(tenantId, TimeSpan.FromSeconds(5)))
            {
                // Held
            }

            // Immediately after disposal, connection 2 acquires lock without waiting
            var acquireTask = service2.AcquireTenantAssignmentLockAsync(tenantId, TimeSpan.FromSeconds(5));
            var completedTask = await Task.WhenAny(acquireTask, Task.Delay(2000));
            Assert.Equal(acquireTask, completedTask);

            await using var lock2 = await acquireTask;
            Assert.NotNull(lock2);
        }

        private class MockAuditService : IAuditService
        {
            public Task LogAsync(string? userId, string entityType, string entityId, string action, object? details = null, bool isSecurityCritical = false) => Task.CompletedTask;
            public Task<List<AuditLog>> GetRecentLogsAsync(int count = 50) => Task.FromResult(new List<AuditLog>());
        }

        private class MockAIService : IAIService
        {
            public Task<AILeadAnalysisResult> AnalyzeLeadAsync(Lead lead, List<LeadActivity> activities, List<FollowUp> followUps)
            {
                return Task.FromResult(new AILeadAnalysisResult
                {
                    Score = 75,
                    Classification = "Warm",
                    Provider = "DeterministicRuleEngine",
                    Model = "RuleEngine",
                    Reasons = new List<string> { "Rule match" },
                    SuggestedResponse = "Hello"
                });
            }

            public Task<string> GenerateDraftResponseAsync(Lead lead, List<LeadActivity> activities, string? userTone, string? customInstructions) => Task.FromResult("Email body");
            public Task<ManagerPipelineSummaryDto> GeneratePipelineSummaryAsync(DashboardSummaryDto dashboardData) => Task.FromResult(new ManagerPipelineSummaryDto());
        }

        private class MockNotificationService : INotificationService
        {
            public Task CreateNotificationAsync(string userId, string title, string message, int? leadId = null) => Task.CompletedTask;
            public Task<List<Notification>> GetUserNotificationsAsync(string userId, int count = 20, bool unreadOnly = false) => Task.FromResult(new List<Notification>());
            public Task<int> GetUnreadCountAsync(string userId) => Task.FromResult(0);
            public Task<bool> MarkAsReadAsync(int notificationId, string userId) => Task.FromResult(true);
            public Task MarkAllAsReadAsync(string userId) => Task.CompletedTask;
        }
    }
}
