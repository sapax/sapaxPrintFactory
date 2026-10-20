using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace sapaXPrintMaster.Services
{
    /// <summary>
    /// Encrypts small JSON settings with Windows DPAPI (current Windows user).
    /// Needs NuGet: System.Security.Cryptography.ProtectedData
    /// </summary>
    public static class SecureStore
    {
        public static readonly string Folder =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "sapaXPrintMaster");

        public static void Write<T>(string fileName, T value)
        {
            Directory.CreateDirectory(Folder);
            var plain = JsonSerializer.SerializeToUtf8Bytes(value);
            var cipher = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(Path.Combine(Folder, fileName), cipher);
        }

        public static T? Read<T>(string fileName) where T : class
        {
            var path = Path.Combine(Folder, fileName);
            if (!File.Exists(path)) return null;
            try
            {
                var plain = ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser);
                return JsonSerializer.Deserialize<T>(plain);
            }
            catch { return null; }   // wrong user / corrupted file
        }

        public static void Delete(string fileName)
        {
            try { File.Delete(Path.Combine(Folder, fileName)); } catch { }
        }
    }
}