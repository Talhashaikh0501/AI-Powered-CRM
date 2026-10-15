using System;
using Microsoft.Extensions.Configuration;

namespace AI_Powered_CRM.Security
{
    public interface IBackgroundJobScopeFactory
    {
        IDisposable CreateJobScope(string jobReason);
    }

    public class BackgroundJobScopeFactory : IBackgroundJobScopeFactory
    {
        private readonly ISystemSecurityContext _systemSecurityContext;
        private readonly string _systemJobToken;

        public BackgroundJobScopeFactory(
            ISystemSecurityContext systemSecurityContext,
            IConfiguration? configuration = null)
        {
            _systemSecurityContext = systemSecurityContext;
            _systemJobToken = configuration?["Security:SystemJobToken"] 
                ?? SystemSecurityContext.DefaultSystemInfrastructureToken;
        }

        public IDisposable CreateJobScope(string jobReason)
        {
            return _systemSecurityContext.BeginSystemScope(jobReason, _systemJobToken);
        }
    }
}
