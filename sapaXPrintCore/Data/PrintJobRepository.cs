using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using sapaXPrintCore.Models;

namespace sapaXPrintCore.Data
{
    /// <summary>A job this computer has just claimed from dbo.PrintJobs.</summary>
    public sealed class ClaimedJob
    {
        public Guid JobId { get; init; }
        public string JobType { get; init; } = "PDF";      // "PDF" or "ZPL"
        public string PrinterId { get; init; } = "";
        public string? FileName { get; init; }
        public string? Payload { get; init; }              // PDF = base64, ZPL = plain text
        public int AttemptCount { get; init; }
    }

    /// <summary>
    /// All database access of the agent. No cloud / API in between:
    /// the desktop client reads and updates dbo.PrintJobs and dbo.Printers itself.
    /// </summary>
    public sealed class PrintJobRepository
    {
        // A claimed job whose agent never reported back is handed out again after this long...
        private const int LockMinutes = 15;
        // ...but at most this many times. After that it is marked Failed.
        private const int MaxAttempts = 3;

        private readonly Func<string?> _connectionString;

        public PrintJobRepository(Func<string?> connectionString)
        {
            _connectionString = connectionString;
        }

        private async Task<SqlConnection> OpenAsync(CancellationToken ct = default)
        {
            var cs = _connectionString();
            if (string.IsNullOrWhiteSpace(cs))
                throw new InvalidOperationException("Database is not configured. Open the Administrator tab and set the connection.");

            var con = new SqlConnection(cs);
            await con.OpenAsync(ct).ConfigureAwait(false);
            return con;
        }

        // ------------------------------------------------------------------
        // Jobs tab: the newest rows of dbo.PrintJobs, all sources.
        // ------------------------------------------------------------------
        public async Task<List<DbPrintJobRow>> GetRecentJobsAsync(int top = 200, CancellationToken ct = default)
        {
            var list = new List<DbPrintJobRow>();

            using var con = await OpenAsync(ct).ConfigureAwait(false);

            // Table not created yet: nothing to show
            using (var chk = new SqlCommand("SELECT OBJECT_ID(N'dbo.PrintJobs', N'U')", con))
            {
                if (await chk.ExecuteScalarAsync(ct).ConfigureAwait(false) is null or DBNull)
                    return list;
            }

            const string sql = @"
SELECT TOP (@top) JobId, JobType, PrinterID, PrinterName, FileName, Source, Status,
       ErrorMessage, ComputerID, CreatedAt, CompletedAt
FROM dbo.PrintJobs
ORDER BY CreatedAt DESC;";

            using var cmd = new SqlCommand(sql, con) { CommandTimeout = 30 };
            cmd.Parameters.AddWithValue("@top", top);

            using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await r.ReadAsync(ct).ConfigureAwait(false))
            {
                list.Add(new DbPrintJobRow
                {
                    JobId = r.GetGuid(0),
                    JobType = Str(r, 1),
                    PrinterId = Str(r, 2),
                    PrinterName = Str(r, 3),
                    FileName = Str(r, 4),
                    Source = Str(r, 5),
                    Status = Str(r, 6),
                    ErrorMessage = Str(r, 7),
                    ComputerId = Str(r, 8),
                    CreatedAtUtc = r.GetDateTime(9),
                    CompletedAtUtc = r.IsDBNull(10) ? null : r.GetDateTime(10)
                });
            }

            return list;
        }

        // ------------------------------------------------------------------
        // Claim ONE queued job for one of this computer's active printers.
        // UPDLOCK + READPAST makes this safe even if two agents poll at once
        // (each one skips rows the other has locked).
        // ------------------------------------------------------------------
        public async Task<ClaimedJob?> ClaimNextAsync(string computerId, CancellationToken ct = default)
        {
            const string sql = @"
;WITH next AS (
    SELECT TOP (1) j.*
    FROM dbo.PrintJobs j WITH (ROWLOCK, UPDLOCK, READPAST)
    WHERE j.PrinterID IN (SELECT p.PrinterID FROM dbo.Printers p
                          WHERE p.ComputerID = @cid AND p.Active = 1)
      AND ( j.Status = 'Queued'
            OR (j.Status = 'Claimed' AND j.LockedUntil < SYSUTCDATETIME() AND j.AttemptCount < @max) )
    ORDER BY j.CreatedAt
)
UPDATE next
SET Status       = 'Claimed',
    ComputerID   = @cid,
    ClaimedAt    = SYSUTCDATETIME(),
    LockedUntil  = DATEADD(MINUTE, @lock, SYSUTCDATETIME()),
    AttemptCount = AttemptCount + 1
OUTPUT inserted.JobId, inserted.JobType, inserted.PrinterID, inserted.FileName, inserted.Payload, inserted.AttemptCount;";

            using var con = await OpenAsync(ct).ConfigureAwait(false);
            using var cmd = new SqlCommand(sql, con) { CommandTimeout = 30 };
            cmd.Parameters.AddWithValue("@cid", computerId);
            cmd.Parameters.AddWithValue("@max", MaxAttempts);
            cmd.Parameters.AddWithValue("@lock", LockMinutes);

            using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await r.ReadAsync(ct).ConfigureAwait(false))
                return null;

            return new ClaimedJob
            {
                JobId = r.GetGuid(0),
                JobType = r.GetString(1),
                PrinterId = r.GetString(2),
                FileName = r.IsDBNull(3) ? null : r.GetString(3),
                Payload = r.IsDBNull(4) ? null : r.GetString(4),
                AttemptCount = r.GetInt32(5)
            };
        }

        // ------------------------------------------------------------------
        // Report the final result (Completed / Failed). Retries a few times
        // so a short network blip does not leave the job stuck as "Claimed".
        // ------------------------------------------------------------------
        public async Task CompleteAsync(
            Guid jobId, bool success, string? error = null,
            string? printerName = null, string? printerStatus = null,
            int? spoolerJobId = null, string? spoolerStatus = null)
        {
            const string sql = @"
UPDATE dbo.PrintJobs
SET Status        = @status,
    ErrorMessage  = @err,
    PrinterName   = COALESCE(@pn, PrinterName),
    PrinterStatus = @ps,
    SpoolerJobId  = @sj,
    SpoolerStatus = @ss,
    CompletedAt   = SYSUTCDATETIME(),
    LockedUntil   = NULL
WHERE JobId = @id;";

            Exception? last = null;
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    using var con = await OpenAsync().ConfigureAwait(false);
                    using var cmd = new SqlCommand(sql, con) { CommandTimeout = 30 };
                    cmd.Parameters.AddWithValue("@id", jobId);
                    cmd.Parameters.AddWithValue("@status", success ? "Completed" : "Failed");
                    cmd.Parameters.AddWithValue("@err", Db(Trunc(error, 1000)));
                    cmd.Parameters.AddWithValue("@pn", Db(Trunc(printerName, 200)));
                    cmd.Parameters.AddWithValue("@ps", Db(Trunc(printerStatus, 50)));
                    cmd.Parameters.AddWithValue("@sj", Db(spoolerJobId));
                    cmd.Parameters.AddWithValue("@ss", Db(Trunc(spoolerStatus, 200)));
                    await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                    return;
                }
                catch (Exception ex)
                {
                    last = ex;
                    await Task.Delay(TimeSpan.FromSeconds(attempt)).ConfigureAwait(false);
                }
            }

            throw new Exception("Could not save the job result to the database: " + last?.Message, last);
        }

        // ------------------------------------------------------------------
        // Heartbeat + status of this computer's registered printers.
        // Registration (which printers exist and are Active) is done only by the
        // SuperAdmin in the Administrator tab. The agent never adds or re-activates printers.
        // ------------------------------------------------------------------
        public async Task SyncPrintersAsync(string computerId, IEnumerable<PrinterInfo> printers, CancellationToken ct = default)
        {
            using var con = await OpenAsync(ct).ConfigureAwait(false);

            using (var chk = new SqlCommand("SELECT COUNT(1) FROM dbo.Computers WHERE ComputerID = @cid AND Active = 1", con))
            {
                chk.Parameters.AddWithValue("@cid", computerId);
                if (Convert.ToInt32(await chk.ExecuteScalarAsync(ct).ConfigureAwait(false)) == 0)
                    throw new InvalidOperationException(
                        "This computer is not registered (or is inactive). Register it in the Administrator tab.");
            }

            var syncTime = DateTime.UtcNow;
            using var tx = con.BeginTransaction();

            const string update = @"
                UPDATE dbo.Printers
                SET PrinterName = @name, Status = @status, LastSeenAt = @now
                WHERE ComputerID = @cid AND PrinterID = @pid;";

            foreach (var p in printers)
            {
                using var cmd = new SqlCommand(update, con, tx);
                cmd.Parameters.AddWithValue("@cid", computerId);
                cmd.Parameters.AddWithValue("@pid", p.PrinterId);
                cmd.Parameters.AddWithValue("@name", Db(Trunc(p.PrinterName, 200)));
                cmd.Parameters.AddWithValue("@status", Db(Trunc(p.Status, 50)));
                cmd.Parameters.AddWithValue("@now", syncTime);
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            using (var beat = new SqlCommand(
                "UPDATE dbo.Computers SET LastSeenAt = SYSUTCDATETIME() WHERE ComputerID = @cid", con, tx))
            {
                beat.Parameters.AddWithValue("@cid", computerId);
                await beat.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            // Claimed long ago, tried too often, never reported back -> give up
            using (var dead = new SqlCommand(@"
                UPDATE dbo.PrintJobs
                SET Status = 'Failed', ErrorMessage = 'Agent stopped responding while printing this job.',
                    CompletedAt = SYSUTCDATETIME(), LockedUntil = NULL
                WHERE Status = 'Claimed' AND LockedUntil < SYSUTCDATETIME() AND AttemptCount >= @max;", con, tx))
            {
                dead.Parameters.AddWithValue("@max", MaxAttempts);
                await dead.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            tx.Commit();
        }

        private static string Str(SqlDataReader r, int i) => r.IsDBNull(i) ? "" : r.GetString(i);

        private static object Db(object? v) => v ?? DBNull.Value;

        private static string? Trunc(string? s, int max) =>
            s == null ? null : (s.Length <= max ? s : s.Substring(0, max));
    }
}