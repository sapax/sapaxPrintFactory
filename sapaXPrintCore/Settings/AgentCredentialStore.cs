using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace sapaXPrintCore.Settings
{
    /// <summary>Machine-wide agent settings (computer ID, agent name, poll interval).</summary>
    public class AgentCredentials
    {
        public string ComputerId { get; set; } = "";
        public string AgentName { get; set; } = "";

        /// <summary>How often the agent asks the database for new print jobs.</summary>
        public int PollIntervalSeconds { get; set; } = 5;
    }

    public static class AgentCredentialStore
    {
        private const string FileName = "agent.dat";

        public static AgentCredentials? Load() => SecureStore.Read<AgentCredentials>(FileName);

        public static void Save(AgentCredentials c) => SecureStore.Write(FileName, c);
    }
}