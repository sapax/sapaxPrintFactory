using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace sapaXPrintCore.Models
{
    /// <summary>One row of dbo.PrintJobs, shown in the Jobs tab.</summary>
    public sealed class DbPrintJobRow
    {
        public Guid JobId { get; init; }
        public string JobType { get; init; } = "";
        public string PrinterId { get; init; } = "";
        public string PrinterName { get; init; } = "";
        public string FileName { get; init; } = "";
        public string Source { get; init; } = "";
        public string Status { get; init; } = "";
        public string ErrorMessage { get; init; } = "";
        public string ComputerId { get; init; } = "";
        public DateTime CreatedAtUtc { get; init; }
        public DateTime? CompletedAtUtc { get; init; }

        public string ShortId => JobId.ToString()[..8];

        public string Created => CreatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    }
}