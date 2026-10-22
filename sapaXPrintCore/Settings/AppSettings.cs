using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using sapaXPrintCore.Jobs;
using sapaXPrintCore.Printers;

namespace sapaXPrintCore.Settings
{
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