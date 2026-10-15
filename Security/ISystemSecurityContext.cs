using System;

namespace AI_Powered_CRM.Security
{
    public interface ISystemSecurityContext
    {
        bool IsSystemExecution { get; }
        string? SystemExecutionReason { get; }
        IDisposable BeginSystemScope(string reason);
    }
}
