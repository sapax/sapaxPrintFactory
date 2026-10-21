using sapaXPrintCore.Printers;
using sapaXPrintCore.Jobs;
using System;

namespace sapaXPrintMaster.Services
{
    // Values come from the Administrator tab, stored encrypted by AgentCredentialStore.
    // Cached in memory because the poller reads them on every loop (every few seconds).
    public static class AppSettings
    {
        public const int DefaultPollSeconds = 5;

        private static readonly object _lock = new();
        private static AgentCredentials? _cache;

        private static AgentCredentials Current
        {
            get
            {
                lock (_lock)
                    return _cache ??= AgentCredentialStore.Load()
                                      ?? new AgentCredentials { ComputerId = ComputerIdentityService.GetComputerId() };
            }
        }

        public static string AgentId =>
            string.IsNullOrWhiteSpace(Current.ComputerId) ? ComputerIdentityService.GetComputerId() : Current.ComputerId;

        public static int PollIntervalSeconds
        {
            get
            {
                int s = Current.PollIntervalSeconds;
                if (s <= 0) s = DefaultPollSeconds;
                return Math.Clamp(s, DbJobPoller.MinIntervalSeconds, DbJobPoller.MaxIntervalSeconds);
            }
        }

        /// <summary>Changes and saves the local settings. The poller picks the new values up on its next loop.</summary>
        public static void Update(Action<AgentCredentials> change)
        {
            lock (_lock)
            {
                var c = _cache ?? AgentCredentialStore.Load()
                        ?? new AgentCredentials { ComputerId = ComputerIdentityService.GetComputerId() };
                change(c);
                if (string.IsNullOrWhiteSpace(c.ComputerId))
                    c.ComputerId = ComputerIdentityService.GetComputerId();

                AgentCredentialStore.Save(c);
                _cache = c;
            }
        }
    }
}