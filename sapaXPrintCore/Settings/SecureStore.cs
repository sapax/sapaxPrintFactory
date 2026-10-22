using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;

namespace sapaXPrintCore.Settings
{
    /// <summary>
    /// Machine-wide settings in %ProgramData%\sapaXPrintMaster, encrypted with DPAPI (LocalMachine).
    /// The desktop app and the Windows service read the same files.
    /// Old per-user files in %AppData% are migrated the first time they are read.
    /// </summary>
    public static class SecureStore
    {
        public static readonly string Folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "sapaXPrintMaster");

        private static readonly string LegacyFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "sapaXPrintMaster");

        public static void Write<T>(string fileName, T value)
        {
            EnsureFolder();
            var plain = JsonSerializer.SerializeToUtf8Bytes(value);
            var cipher = ProtectedData.Protect(plain, null, DataProtectionScope.LocalMachine);
            File.WriteAllBytes(Path.Combine(Folder, fileName), cipher);
        }

        public static T? Read<T>(string fileName) where T : class
        {
            var path = Path.Combine(Folder, fileName);
            if (File.Exists(path))
            {
                try
                {
                    var plain = ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.LocalMachine);
                    return JsonSerializer.Deserialize<T>(plain);
                }
                catch { return null; }
            }

            return ReadLegacy<T>(fileName);
        }

        public static void Delete(string fileName)
        {
            try { File.Delete(Path.Combine(Folder, fileName)); } catch { }
        }

        // Old per-user file: read it with the current user's key, copy it to the machine store, remove the old file.
        private static T? ReadLegacy<T>(string fileName) where T : class
        {
            var legacy = Path.Combine(LegacyFolder, fileName);
            if (!File.Exists(legacy)) return null;

            try
            {
                var plain = ProtectedData.Unprotect(File.ReadAllBytes(legacy), null, DataProtectionScope.CurrentUser);
                var value = JsonSerializer.Deserialize<T>(plain);
                if (value is null) return null;

                Write(fileName, value);
                try { File.Delete(legacy); } catch { }
                return value;
            }
            catch { return null; }
        }

        // Lets normal users change the settings from the desktop app (SYSTEM always has access).
        private static void EnsureFolder()
        {
            if (Directory.Exists(Folder)) return;

            var dir = Directory.CreateDirectory(Folder);
            var security = dir.GetAccessControl();
            var users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);

            security.AddAccessRule(new FileSystemAccessRule(
                users,
                FileSystemRights.Modify,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));

            dir.SetAccessControl(security);
        }
    }
}