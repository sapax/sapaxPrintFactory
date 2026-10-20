using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace sapaXPrintMaster.Services
{
    public class DbTableRow
    {
        public string SchemaName { get; set; } = "";
        public string TableName { get; set; } = "";
        public long Rows { get; set; }
    }

    public class CompanyRow
    {
        public string CompanyID { get; set; } = "";
        public string CompanyName { get; set; } = "";
        public string Display => $"{CompanyName} ({CompanyID})";
    }

    public class SubAreaRow
    {
        public string SubAreaID { get; set; } = "";
        public string SubAreaName { get; set; } = "";
        public string Display => $"{SubAreaName} ({SubAreaID})";
    }

    public static class AdminDbService
    {
        private static async Task<SqlConnection> OpenAsync()
        {
            var c = new SqlConnection(DbConfig.ConnectionString);
            await c.OpenAsync();
            return c;
        }

        public static async Task<string> TestAsync(DbSettings s)
        {
            using var c = new SqlConnection(DbConfig.Build(s));
            await c.OpenAsync();
            return c.ServerVersion;
        }

        // ---------------- tables ----------------
        public static async Task<bool> TableExistsAsync(string name)
        {
            using var c = await OpenAsync();
            return await TableExistsAsync(c, name);
        }

        private static async Task<bool> TableExistsAsync(SqlConnection c, string name)
        {
            using var cmd = new SqlCommand("SELECT OBJECT_ID(@n, N'U')", c);
            cmd.Parameters.AddWithValue("@n", "dbo." + name);
            var r = await cmd.ExecuteScalarAsync();
            return r != null && r != DBNull.Value;
        }

        /// <returns>true if created, false if it already existed</returns>
        public static async Task<bool> CreateTableAsync(string name)
        {
            var t = SchemaTemplates.Get(name);
            using var c = await OpenAsync();

            if (await TableExistsAsync(c, name))
            {
                // Table already there: bring it up to date (adds missing columns / indexes, idempotent)
                await ApplyUpgradeAsync(c, t);
                return false;
            }

            foreach (var dep in t.DependsOn)
                if (!await TableExistsAsync(c, dep))
                    throw new InvalidOperationException($"Create table '{dep}' first ({name} depends on it).");

            using var cmd = new SqlCommand(t.Sql, c);
            await cmd.ExecuteNonQueryAsync();
            return true;
        }

        private static async Task ApplyUpgradeAsync(SqlConnection c, TableTemplate t)
        {
            if (string.IsNullOrWhiteSpace(t.Upgrade)) return;
            using var cmd = new SqlCommand(t.Upgrade, c);
            await cmd.ExecuteNonQueryAsync();
        }

        public static async Task<List<string>> CreateAllMissingAsync()
        {
            var created = new List<string>();
            foreach (var t in SchemaTemplates.All)
                if (await CreateTableAsync(t.Name)) created.Add(t.Name);
            return created;
        }

        public static async Task<List<DbTableRow>> ListTablesAsync()
        {
            const string sql = @"
                SELECT s.name, t.name, SUM(p.rows)
                FROM sys.tables t
                JOIN sys.schemas s    ON s.schema_id = t.schema_id
                JOIN sys.partitions p ON p.object_id = t.object_id AND p.index_id IN (0,1)
                GROUP BY s.name, t.name ORDER BY s.name, t.name";

            var list = new List<DbTableRow>();
            using var c = await OpenAsync();
            using var cmd = new SqlCommand(sql, c);
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                list.Add(new DbTableRow { SchemaName = r.GetString(0), TableName = r.GetString(1), Rows = r.GetInt64(2) });
            return list;
        }

        // ---------------- companies / sub areas ----------------
        public static async Task<List<CompanyRow>> GetCompaniesAsync()
        {
            var list = new List<CompanyRow>();
            using var c = await OpenAsync();
            using var cmd = new SqlCommand("SELECT CompanyID, CompanyName FROM dbo.Companies WHERE Active=1 ORDER BY CompanyName", c);
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                list.Add(new CompanyRow { CompanyID = r.GetString(0), CompanyName = r.GetString(1) });
            return list;
        }

        public static async Task AddCompanyAsync(string id, string name)
        {
            using var c = await OpenAsync();
            using var cmd = new SqlCommand("INSERT INTO dbo.Companies (CompanyID, CompanyName) VALUES (@i,@n)", c);
            cmd.Parameters.AddWithValue("@i", id.Trim());
            cmd.Parameters.AddWithValue("@n", name.Trim());
            await cmd.ExecuteNonQueryAsync();
        }

        public static async Task<List<SubAreaRow>> GetSubAreasAsync(string companyId)
        {
            var list = new List<SubAreaRow>();
            using var c = await OpenAsync();
            using var cmd = new SqlCommand(
                "SELECT SubAreaID, SubAreaName FROM dbo.SubAreas WHERE CompanyID=@c AND Active=1 ORDER BY SubAreaName", c);
            cmd.Parameters.AddWithValue("@c", companyId);
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                list.Add(new SubAreaRow { SubAreaID = r.GetString(0), SubAreaName = r.GetString(1) });
            return list;
        }

        public static async Task AddSubAreaAsync(string companyId, string id, string name)
        {
            using var c = await OpenAsync();
            using var cmd = new SqlCommand(
                "INSERT INTO dbo.SubAreas (CompanyID, SubAreaID, SubAreaName) VALUES (@c,@i,@n)", c);
            cmd.Parameters.AddWithValue("@c", companyId);
            cmd.Parameters.AddWithValue("@i", id.Trim());
            cmd.Parameters.AddWithValue("@n", name.Trim());
            await cmd.ExecuteNonQueryAsync();
        }

        // ---------------- register computer (= agent) + API key ----------------
        /// <summary>
        /// Upserts the computer row and makes sure it has an active API key.
        /// Returns the RAW key only when a new key was created (the DB only keeps its hash).
        /// </summary>
        public static async Task<string?> RegisterComputerAsync(
            string computerId, string agentName, string companyId, string subAreaId, bool regenerateKey)
        {
            using var c = await OpenAsync();
            using var tx = c.BeginTransaction();

            Guid? keyId = null;
            using (var cmd = new SqlCommand(@"
                SELECT TOP 1 ApiKeyId FROM dbo.ApiKeys
                WHERE OwnerType='agent' AND OwnerId=@o AND IsActive=1 ORDER BY CreatedAt DESC", c, tx))
            {
                cmd.Parameters.AddWithValue("@o", computerId);
                keyId = await cmd.ExecuteScalarAsync() as Guid?;
            }

            string? raw = null;
            if (regenerateKey || keyId == null)
            {
                if (keyId != null)
                {
                    using var off = new SqlCommand(
                        "UPDATE dbo.ApiKeys SET IsActive=0 WHERE OwnerType='agent' AND OwnerId=@o", c, tx);
                    off.Parameters.AddWithValue("@o", computerId);
                    await off.ExecuteNonQueryAsync();
                }

                raw = "agent_live_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
                keyId = Guid.NewGuid();

                using var ins = new SqlCommand(@"
                    INSERT INTO dbo.ApiKeys (ApiKeyId, KeyPrefix, KeyHash, OwnerType, OwnerId, IsActive, CreatedAt)
                    VALUES (@id,@p,@h,'agent',@o,1,SYSUTCDATETIME())", c, tx);
                ins.Parameters.AddWithValue("@id", keyId.Value);
                ins.Parameters.AddWithValue("@p", raw[..16]);
                ins.Parameters.Add("@h", SqlDbType.VarBinary, 32).Value = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
                ins.Parameters.AddWithValue("@o", computerId);
                await ins.ExecuteNonQueryAsync();
            }

            // link to the activation key used on this machine (if any)
            int? activationId = null;
            var local = ActivationService.LoadLocal();
            if (local != null)
            {
                using var a = new SqlCommand("SELECT Id FROM dbo.ActivationKeys WHERE ActivationKey=@k", c, tx);
                a.Parameters.AddWithValue("@k", local.ActivationKey);
                activationId = await a.ExecuteScalarAsync() as int?;
            }

            using (var m = new SqlCommand(@"
                MERGE dbo.Computers AS t
                USING (SELECT @cid AS ComputerID) AS s ON t.ComputerID = s.ComputerID
                WHEN MATCHED THEN UPDATE SET ComputerName=@name, CompanyID=@co, SubAreaID=@sa,
                     ApiKeyId=@key, ActivationKeyId=COALESCE(@act, t.ActivationKeyId), Active=1
                WHEN NOT MATCHED THEN INSERT (ComputerID, ComputerName, CompanyID, SubAreaID, ApiKeyId, ActivationKeyId)
                     VALUES (@cid,@name,@co,@sa,@key,@act);", c, tx))
            {
                m.Parameters.AddWithValue("@cid", computerId);
                m.Parameters.AddWithValue("@name", agentName);
                m.Parameters.AddWithValue("@co", companyId);
                m.Parameters.AddWithValue("@sa", subAreaId);
                m.Parameters.AddWithValue("@key", keyId!.Value);
                m.Parameters.AddWithValue("@act", (object?)activationId ?? DBNull.Value);
                await m.ExecuteNonQueryAsync();
            }

            tx.Commit();
            return raw;
        }
    }
}