using Microsoft.Data.SqlClient;
using System;

namespace sapaXPrintMaster.Services
{
    public class DbSettings
    {
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public bool UseWindowsAuth { get; set; }
        public string User { get; set; } = "";
        public string Password { get; set; } = "";
    }

    /// <summary>
    /// No credentials in source code. The SuperAdmin enters them in the Administrator tab;
    /// they are stored encrypted (DPAPI) under %AppData%.
    /// </summary>
    public static class DbConfig
    {
        private const string FileName = "db.dat";
        private static DbSettings? _cached;

        public static DbSettings? Load() => _cached ??= SecureStore.Read<DbSettings>(FileName);

        public static void Save(DbSettings s)
        {
            SecureStore.Write(FileName, s);
            _cached = s;
        }

        public static void Clear()
        {
            SecureStore.Delete(FileName);
            _cached = null;
        }

        public static bool IsConfigured =>
            Load() is { } s && !string.IsNullOrWhiteSpace(s.Server) && !string.IsNullOrWhiteSpace(s.Database);

        public static string ConnectionString =>
            Build(Load() ?? throw new InvalidOperationException(
                "The database is not configured. Open the Administrator tab and enter the connection."));

        public static string Build(DbSettings s)
        {
            var b = new SqlConnectionStringBuilder
            {
                DataSource = s.Server.Trim(),
                InitialCatalog = s.Database.Trim(),
                // Azure SQL serverless can take a while to resume after a pause
                ConnectTimeout = 30,
                Encrypt = true,
                TrustServerCertificate = false
            };

            if (s.UseWindowsAuth)
            {
                b.IntegratedSecurity = true;
            }
            else
            {
                b.UserID = s.User.Trim();
                b.Password = s.Password;
            }

            return b.ConnectionString;
        }
    }
}