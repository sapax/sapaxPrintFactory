using Microsoft.Data.SqlClient;
using sapaXPrintCore.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
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

    public class ComputerRow
    {
        public string ComputerID { get; set; } = "";
        public string ComputerName { get; set; } = "";
        public string Display => string.IsNullOrEmpty(ComputerID)
            ? ComputerName
            : $"{ComputerName} ({ComputerID})";
    }

    public class RegisteredPrinterRow
    {
        public string ComputerID { get; set; } = "";
        public string ComputerName { get; set; } = "";
        public string CompanyName { get; set; } = "";
        public string SubAreaName { get; set; } = "";
        public string PrinterID { get; set; } = "";
        public string PrinterName { get; set; } = "";
        public string Status { get; set; } = "";
        public bool Active { get; set; }
        public DateTime? LastSeenAt { get; set; }
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

        public static async Task<List<string>> GetMissingTableNamesAsync()
        {
            var missing = new List<string>();
            using var c = await OpenAsync();
            foreach (var t in SchemaTemplates.All)
                if (!await TableExistsAsync(c, t.Name))
                    missing.Add(t.Name);
            return missing;
        }

        /// <returns>true if created, false if it already existed (then it is upgraded)</returns>
        public static async Task<bool> CreateTableAsync(string name)
        {
            var t = SchemaTemplates.Get(name);
            using var c = await OpenAsync();

            if (await TableExistsAsync(c, name))
            {
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

        // ---------------- agent: computer + its printers ----------------
        public static async Task<bool> IsComputerRegisteredAsync(string computerId)
        {
            using var c = await OpenAsync();
            if (!await TableExistsAsync(c, "Computers")) return false;

            using var cmd = new SqlCommand(
                "SELECT COUNT(1) FROM dbo.Computers WHERE ComputerID=@cid AND Active=1", c);
            cmd.Parameters.AddWithValue("@cid", computerId);
            return Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
        }

        /// <summary>
        /// Registers this computer and the selected printers. Printers of this computer that are not
        /// selected become inactive. Runs in one transaction.
        /// </summary>
        public static async Task RegisterComputerAsync(
            string computerId, string agentName, string companyId, string subAreaId, IEnumerable<PrinterInfo> printers)
        {
            var list = printers.ToList();

            using var c = await OpenAsync();
            using var tx = c.BeginTransaction();

            using (var m = new SqlCommand(@"
                MERGE dbo.Computers AS t
                USING (SELECT @cid AS ComputerID) AS s ON t.ComputerID = s.ComputerID
                WHEN MATCHED THEN UPDATE SET ComputerName=@name, CompanyID=@co, SubAreaID=@sa, Active=1
                WHEN NOT MATCHED THEN INSERT (ComputerID, ComputerName, CompanyID, SubAreaID)
                     VALUES (@cid, @name, @co, @sa);", c, tx))
            {
                m.Parameters.AddWithValue("@cid", computerId);
                m.Parameters.AddWithValue("@name", agentName);
                m.Parameters.AddWithValue("@co", companyId);
                m.Parameters.AddWithValue("@sa", subAreaId);
                await m.ExecuteNonQueryAsync();
            }

            // Deactivate every printer of this computer, then activate the selected ones
            using (var off = new SqlCommand("UPDATE dbo.Printers SET Active=0 WHERE ComputerID=@cid", c, tx))
            {
                off.Parameters.AddWithValue("@cid", computerId);
                await off.ExecuteNonQueryAsync();
            }

            foreach (var p in list)
            {
                using var pm = new SqlCommand(@"
                    MERGE dbo.Printers AS t
                    USING (SELECT @cid AS ComputerID, @pid AS PrinterID) AS s
                       ON t.ComputerID = s.ComputerID AND t.PrinterID = s.PrinterID
                    WHEN MATCHED THEN UPDATE SET PrinterName=@pname, Active=1, LastSeenAt=SYSUTCDATETIME()
                    WHEN NOT MATCHED THEN INSERT (ComputerID, PrinterID, PrinterName, Active, LastSeenAt)
                         VALUES (@cid, @pid, @pname, 1, SYSUTCDATETIME());", c, tx);
                pm.Parameters.AddWithValue("@cid", computerId);
                pm.Parameters.AddWithValue("@pid", p.PrinterId);
                pm.Parameters.AddWithValue("@pname", p.PrinterName);
                await pm.ExecuteNonQueryAsync();
            }

            tx.Commit();
        }
        // ---------------- registered computers and printers (view) ----------------
        public static async Task<List<ComputerRow>> GetComputersAsync()
        {
            var list = new List<ComputerRow>();
            using var c = await OpenAsync();
            if (!await TableExistsAsync(c, "Computers")) return list;

            using var cmd = new SqlCommand(
                "SELECT ComputerID, ComputerName FROM dbo.Computers ORDER BY ComputerName", c);
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                list.Add(new ComputerRow
                {
                    ComputerID = r.GetString(0),
                    ComputerName = r.IsDBNull(1) ? "" : r.GetString(1)
                });
            return list;
        }

        /// <param name="computerId">null = all computers</param>
        public static async Task<List<RegisteredPrinterRow>> GetRegisteredPrintersAsync(string? computerId)
        {
            var list = new List<RegisteredPrinterRow>();
            using var c = await OpenAsync();
            if (!await TableExistsAsync(c, "Computers") || !await TableExistsAsync(c, "Printers"))
                return list;

            using var cmd = new SqlCommand(@"
        SELECT c.ComputerID, c.ComputerName, co.CompanyName, sa.SubAreaName,
               p.PrinterID, p.PrinterName, p.Status, p.Active, p.LastSeenAt
        FROM dbo.Printers p
        JOIN dbo.Computers c       ON c.ComputerID = p.ComputerID
        LEFT JOIN dbo.Companies co ON co.CompanyID = c.CompanyID
        LEFT JOIN dbo.SubAreas sa  ON sa.CompanyID = c.CompanyID AND sa.SubAreaID = c.SubAreaID
        WHERE (@cid IS NULL OR c.ComputerID = @cid)
        ORDER BY c.ComputerName, p.PrinterName;", c);

            cmd.Parameters.Add("@cid", SqlDbType.NVarChar, 100).Value = (object?)computerId ?? DBNull.Value;

            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                list.Add(new RegisteredPrinterRow
                {
                    ComputerID = r.GetString(0),
                    ComputerName = r.IsDBNull(1) ? "" : r.GetString(1),
                    CompanyName = r.IsDBNull(2) ? "" : r.GetString(2),
                    SubAreaName = r.IsDBNull(3) ? "" : r.GetString(3),
                    PrinterID = r.GetString(4),
                    PrinterName = r.IsDBNull(5) ? "" : r.GetString(5),
                    Status = r.IsDBNull(6) ? "" : r.GetString(6),
                    Active = r.GetBoolean(7),
                    LastSeenAt = r.IsDBNull(8) ? null : r.GetDateTime(8)
                });
            }
            return list;
        }
        // ---------------- print queue (Manual source) ----------------
        /// <summary>
        /// Adds a PDF to dbo.PrintJobs. The agent for the printer claims it and prints it.
        /// </summary>
        public static async Task<Guid> EnqueuePdfJobAsync(
            string printerId, string fileName, string filePath, string source, string requestedBy)
        {
            byte[] bytes = await File.ReadAllBytesAsync(filePath);

            using var c = await OpenAsync();

            using (var check = new SqlCommand(
                "SELECT COUNT(1) FROM dbo.Printers WHERE PrinterID=@p AND Active=1", c))
            {
                check.Parameters.AddWithValue("@p", printerId);
                if (Convert.ToInt32(await check.ExecuteScalarAsync()) == 0)
                    throw new InvalidOperationException(
                        "This printer is not registered to an agent. A SuperAdmin must register the computer and its printers in the Administrator tab.");
            }

            var jobId = Guid.NewGuid();
            using var cmd = new SqlCommand(@"
                INSERT INTO dbo.PrintJobs (JobId, JobType, PrinterID, FileName, Payload, Source, RequestedBy, Status)
                VALUES (@id, 'PDF', @p, @f, @payload, @src, @by, 'Queued');", c);

            cmd.Parameters.AddWithValue("@id", jobId);
            cmd.Parameters.AddWithValue("@p", printerId);
            cmd.Parameters.AddWithValue("@f", fileName);
            cmd.Parameters.Add("@payload", SqlDbType.NVarChar, -1).Value = Convert.ToBase64String(bytes);
            cmd.Parameters.AddWithValue("@src", source);
            cmd.Parameters.AddWithValue("@by", requestedBy);

            await cmd.ExecuteNonQueryAsync();
            return jobId;
        }

        // ---------------- readable errors (no long SQL text in the UI) ----------------
        public static string FriendlyError(Exception ex)
        {
            if (ex is SqlException se)
            {
                switch (se.Number)
                {
                    case 18456:
                        return "Sign-in to the database failed. Check the database user name and password.";
                    case 40615:
                        return "This PC's IP address is not allowed by the database firewall. Add it in the Azure SQL server firewall rules.";
                    case 4060:
                        return "The database name was not found on this server.";
                }
            }

            if (ex.Message.Contains("Timeout", StringComparison.OrdinalIgnoreCase))
                return "The database did not respond in time. Check the server name and the firewall rule for this PC's IP, then try again.";

            string first = ex.Message.Split('\n')[0].Trim();
            return first.Length > 200 ? first[..200] + "..." : first;
        }
    }
}