using System;
using System.Threading;

namespace AI_Powered_CRM.Security
{
    public class SystemSecurityContext : ISystemSecurityContext
    {
        private static readonly AsyncLocal<string?> _currentSystemReason = new AsyncLocal<string?>();

        public bool IsSystemExecution => !string.IsNullOrWhiteSpace(_currentSystemReason.Value);

        public string? SystemExecutionReason => _currentSystemReason.Value;

        public IDisposable BeginSystemScope(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
                throw new ArgumentException("A valid justification reason is required for system execution scope.", nameof(reason));

            var previous = _currentSystemReason.Value;
            _currentSystemReason.Value = reason;
            return new SystemScope(() => _currentSystemReason.Value = previous);
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
