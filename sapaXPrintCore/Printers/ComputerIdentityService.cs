using System;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace sapaXPrintCore.Printers
{
    public static class ComputerIdentityService
    {
        public static string GetComputerId()
        {
            string raw = TryGetMachineGuid() ?? Environment.MachineName;
            return GenerateId(raw);
        }

        private static string? TryGetMachineGuid()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Cryptography");
                return key?.GetValue("MachineGuid") as string;
            }
            catch { return null; }
        }

        private static string GenerateId(string input)
        {
            using SHA1 sha1 = SHA1.Create();
            byte[] hash = sha1.ComputeHash(Encoding.UTF8.GetBytes(input));
            uint id = BitConverter.ToUInt32(hash, 0);
            return (id % 100_000_000).ToString("D8");
        }
    }
}