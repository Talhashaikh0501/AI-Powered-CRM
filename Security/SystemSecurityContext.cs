using System;
using System.Security;
using System.Threading;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace AI_Powered_CRM.Security
{
    public class SystemSecurityContext : ISystemSecurityContext
    {
        public const string DefaultSystemInfrastructureToken = "CRM_SYS_JOB_SECURE_TOKEN_5f8a9e2d_2026";
        private static readonly AsyncLocal<string?> _currentSystemReason = new AsyncLocal<string?>();
        private readonly IHttpContextAccessor? _httpContextAccessor;
        private readonly string _configuredToken;

        public SystemSecurityContext(
            IHttpContextAccessor? httpContextAccessor = null,
            IConfiguration? configuration = null)
        {
            _httpContextAccessor = httpContextAccessor;
            _configuredToken = configuration?["Security:SystemJobToken"] ?? DefaultSystemInfrastructureToken;
        }

        public bool IsSystemExecution => !string.IsNullOrWhiteSpace(_currentSystemReason.Value);

        public string? SystemExecutionReason => _currentSystemReason.Value;

        public IDisposable BeginSystemScope(string reason, string? authorizationToken = null)
        {
            if (string.IsNullOrWhiteSpace(reason))
                throw new ArgumentException("A valid justification reason is required for system execution scope.", nameof(reason));

            // Rule 1: Cannot elevate from an active interactive HTTP request context
            if (_httpContextAccessor?.HttpContext != null)
            {
                throw new SecurityException("Elevation to system execution scope is strictly prohibited within interactive HTTP request contexts.");
            }

            // Rule 2: Caller must present the authorized background-job infrastructure token
            if (string.IsNullOrWhiteSpace(authorizationToken) || !CryptographicTokenMatches(authorizationToken, _configuredToken))
            {
                throw new SecurityException("Unauthorized attempt to establish system execution scope: invalid or missing infrastructure token.");
            }

            var previous = _currentSystemReason.Value;
            _currentSystemReason.Value = reason;
            return new SystemScope(() => _currentSystemReason.Value = previous);
        }

        private static bool CryptographicTokenMatches(string provided, string expected)
        {
            if (provided.Length != expected.Length) return false;
            int diff = 0;
            for (int i = 0; i < provided.Length; i++)
            {
                diff |= provided[i] ^ expected[i];
            }
            return diff == 0;
        }

        private sealed class SystemScope : IDisposable
        {
            private readonly Action _onDispose;
            private bool _disposed;

            public SystemScope(Action onDispose)
            {
                _onDispose = onDispose;
            }

            public void Dispose()
            {
                if (!_disposed)
                {
                    _disposed = true;
                    _onDispose();
                }
            }
        }
    }
}
