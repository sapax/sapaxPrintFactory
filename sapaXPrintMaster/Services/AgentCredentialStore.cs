using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace sapaXPrintMaster.Services
{
    public class AgentCredentials
    {
        public string ComputerId { get; set; } = "";
        public string AgentName { get; set; } = "";
        public string ApiKey { get; set; } = "";
        public string CloudUrl { get; set; } = "";
    }

    public static class AgentCredentialStore
    {
        private const string FileName = "agent.dat";
        public static AgentCredentials? Load() => SecureStore.Read<AgentCredentials>(FileName);
        public static void Save(AgentCredentials c) => SecureStore.Write(FileName, c);
    }
}