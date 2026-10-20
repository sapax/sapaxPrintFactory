using sapaXPrintCore.Printers;

namespace sapaXPrintMaster.Services
{
    // Nothing secret is hardcoded any more. Values come from the Administrator tab
    // ("Register this computer"), stored encrypted by AgentCredentialStore.
    public static class AppSettings
    {
        public static string CloudUrl =>
            AgentCredentialStore.Load() is { CloudUrl.Length: > 0 } c ? c.CloudUrl : "http://localhost:49592";

        public static string AgentId =>
            AgentCredentialStore.Load() is { ComputerId.Length: > 0 } c
                ? c.ComputerId
                : ComputerIdentityService.GetComputerId();

        public static string AgentApiKey => AgentCredentialStore.Load()?.ApiKey ?? "";
    }
}