using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace sapaXPrintMaster.Services
{
    /// <summary>
    /// Local (DPAPI-encrypted) settings of this desktop agent.
    /// Old agent.dat files that still contain ApiKey / CloudUrl load fine: unknown fields are ignored.
    /// </summary>
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