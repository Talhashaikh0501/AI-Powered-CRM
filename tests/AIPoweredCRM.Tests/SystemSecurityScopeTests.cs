using System;
using System.Security;
using AI_Powered_CRM.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace AIPoweredCRM.Tests
{
    public class SystemSecurityScopeTests
    {
        private readonly IConfiguration _configuration;
        private const string TestSystemToken = "CRM_TEST_SECURE_TOKEN_XYZ_123";

        public SystemSecurityScopeTests()
        {
            var inMemorySettings = new System.Collections.Generic.Dictionary<string, string?>
            {
                { "Security:SystemJobToken", TestSystemToken }
            };
            _configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(inMemorySettings)
                .Build();
        }

        [Fact]
        public void TC_SYS_001_InteractiveHttpContext_CannotCreateSystemScope_ThrowsSecurityException()
        {
            var httpContextAccessor = new HttpContextAccessor
            {
                HttpContext = new DefaultHttpContext()
            };
            var securityContext = new SystemSecurityContext(httpContextAccessor, _configuration);

            var ex = Assert.Throws<SecurityException>(() =>
                securityContext.BeginSystemScope("UnauthorizedHttpScope", TestSystemToken));

            Assert.Contains("Elevation to system execution scope is strictly prohibited within interactive HTTP request contexts", ex.Message);
            Assert.False(securityContext.IsSystemExecution);
        }

        [Fact]
        public void TC_SYS_002_MissingInfrastructureToken_OutsideHttpContext_ThrowsSecurityException()
        {
            var httpContextAccessor = new HttpContextAccessor { HttpContext = null };
            var securityContext = new SystemSecurityContext(httpContextAccessor, _configuration);

            var ex = Assert.Throws<SecurityException>(() =>
                securityContext.BeginSystemScope("NoTokenAttempt", null));

            Assert.Contains("invalid or missing infrastructure token", ex.Message);
            Assert.False(securityContext.IsSystemExecution);
        }

        [Fact]
        public void TC_SYS_003_InvalidInfrastructureToken_OutsideHttpContext_ThrowsSecurityException()
        {
            var httpContextAccessor = new HttpContextAccessor { HttpContext = null };
            var securityContext = new SystemSecurityContext(httpContextAccessor, _configuration);

            var ex = Assert.Throws<SecurityException>(() =>
                securityContext.BeginSystemScope("WrongTokenAttempt", "malicious-forged-token"));

            Assert.Contains("invalid or missing infrastructure token", ex.Message);
            Assert.False(securityContext.IsSystemExecution);
        }

        [Fact]
        public void TC_SYS_004_BlankReason_ThrowsArgumentException()
        {
            var httpContextAccessor = new HttpContextAccessor { HttpContext = null };
            var securityContext = new SystemSecurityContext(httpContextAccessor, _configuration);

            Assert.Throws<ArgumentException>(() =>
                securityContext.BeginSystemScope("   ", TestSystemToken));
        }

        [Fact]
        public void TC_SYS_005_AuthorizedBackgroundJobScopeFactory_CreatesScopeSuccessfully()
        {
            var httpContextAccessor = new HttpContextAccessor { HttpContext = null };
            var securityContext = new SystemSecurityContext(httpContextAccessor, _configuration);
            var jobScopeFactory = new BackgroundJobScopeFactory(securityContext, _configuration);

            Assert.False(securityContext.IsSystemExecution);
            Assert.Null(securityContext.SystemExecutionReason);

            using (jobScopeFactory.CreateJobScope("NightlyCRMMaintenanceJob"))
            {
                Assert.True(securityContext.IsSystemExecution);
                Assert.Equal("NightlyCRMMaintenanceJob", securityContext.SystemExecutionReason);
            }

            Assert.False(securityContext.IsSystemExecution);
            Assert.Null(securityContext.SystemExecutionReason);
        }

        [Fact]
        public void TC_SYS_006_SystemScope_RevertsToPreviousState_OnDispose()
        {
            var httpContextAccessor = new HttpContextAccessor { HttpContext = null };
            var securityContext = new SystemSecurityContext(httpContextAccessor, _configuration);
            var jobScopeFactory = new BackgroundJobScopeFactory(securityContext, _configuration);

            using (jobScopeFactory.CreateJobScope("OuterJob"))
            {
                Assert.Equal("OuterJob", securityContext.SystemExecutionReason);

                using (jobScopeFactory.CreateJobScope("InnerJob"))
                {
                    Assert.Equal("InnerJob", securityContext.SystemExecutionReason);
                }

                Assert.Equal("OuterJob", securityContext.SystemExecutionReason);
            }

            Assert.False(securityContext.IsSystemExecution);
            Assert.Null(securityContext.SystemExecutionReason);
        }
    }
}
