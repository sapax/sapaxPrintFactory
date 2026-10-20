using Microsoft.Data.SqlClient;
using sapaXPrintCore.Printers;
using sapaXPrintMaster.Services;
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace sapaXPrintMaster.Services
{

    public class ActivationRecord
    {
        public string ActivationKey { get; set; } = "";
        public string ComputerId { get; set; } = "";
        public bool Expiration { get; set; }
        public DateTime? ExpirationDate { get; set; }
        public DateTime ActivatedAt { get; set; }
    }

    public enum ActivationResult
    {
        Valid,
        NotFound,
        Inactive,
        Expired,
        ConnectionError
    }

    public static class ActivationService
    {

    private static readonly string Folder =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "sapaXPrintMaster");

        private static readonly string FilePath = Path.Combine(Folder, "activation.json");

        // -------------------------------
        // Local activation record
        // -------------------------------
        public static ActivationRecord? LoadLocal()
        {
            if (!File.Exists(FilePath)) return null;

            try
            {
                var json = File.ReadAllText(FilePath);
                return JsonSerializer.Deserialize<ActivationRecord>(json);
            }
            catch
            {
                return null;
            }
        }

        // True only if there's a locally saved activation that hasn't expired.
        public static bool IsActivatedLocally()
        {
            var record = LoadLocal();
            if (record == null) return false;

            if (record.Expiration && record.ExpirationDate.HasValue &&
                record.ExpirationDate.Value <= DateTime.UtcNow)
            {
                ClearLocal(); // expired — force re-activation next launch
                return false;
            }

            return true;
        }

        public static void ClearLocal()
        {
            try
            {
                if (File.Exists(FilePath))
                    File.Delete(FilePath);
            }
            catch
            {
                // best-effort cleanup
            }
        }

        private static void SaveLocal(ActivationRecord record)
        {
            if (!Directory.Exists(Folder))
                Directory.CreateDirectory(Folder);

            var json = JsonSerializer.Serialize(record, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(FilePath, json);
        }

        // -------------------------------
        // Direct SQL validation
        // -------------------------------
        public static async Task<(ActivationResult Result, string Message)> ValidateAndActivateAsync(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return (ActivationResult.NotFound, "Enter an activation key.");

            try
            {
            using var conn = new SqlConnection(DbConfig.ConnectionString);
            await conn.OpenAsync();

            using var cmd = new SqlCommand(
                    "SELECT Active, Expiration, ExpirationDate " +
                    "FROM [dbo].[ActivationKeys] WHERE ActivationKey = @key",
                    conn);
                cmd.Parameters.AddWithValue("@key", key.Trim());

                using var reader = await cmd.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                    return (ActivationResult.NotFound, "Invalid activation key.");

                bool active = reader.GetBoolean(0);
                bool expiration = reader.GetBoolean(1);
                DateTime? expirationDate = reader.IsDBNull(2) ? null : reader.GetDateTime(2);

                if (!active)
                    return (ActivationResult.Inactive, "This activation key has been disabled.");

                if (expiration && expirationDate.HasValue && expirationDate.Value <= DateTime.UtcNow)
                    return (ActivationResult.Expired, "This activation key has expired.");

                reader.Close();

                SaveLocal(new ActivationRecord
                {
                    ActivationKey = key.Trim(),
                    ComputerId = ComputerIdentityService.GetComputerId(),
                    Expiration = expiration,
                    ExpirationDate = expirationDate,
                    ActivatedAt = DateTime.UtcNow
                });

                return (ActivationResult.Valid, "Activated successfully.");
            }
            catch (Exception ex)
            {
                return (ActivationResult.ConnectionError, $"Could not reach activation server: {ex.Message}");
            }
        }
    }
}