using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace sapaXPrintCore.Models
{
    public enum PrintJobStatus
    {
        Queued,
        Printing,   // being rendered / sent to the Windows spooler
        Spooled,    // accepted by the spooler, waiting for the printer to really take it
        Completed,  // CONFIRMED: spooler delivered the job to the printer
        Failed
    }

    public class PrintJob
    {
        public Guid JobId { get; } = Guid.NewGuid();
        public Guid CloudJobId { get; set; }

        public string JobType { get; set; } = "PDF";      // "PDF" or "ZPL"

        public string PrinterId { get; set; } = string.Empty;
        public string PrinterName { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public byte[] PdfBytes { get; set; } = Array.Empty<byte>();

        // Name given to the Windows spooler job (lets us find it again to confirm it)
        public string DocumentName { get; set; } = string.Empty;

        // Printer state at the moment the job was sent (Online, Offline, ...)
        public string? PrinterStatus { get; set; }

        // What the Windows spooler reported for this job
        public int? SpoolerJobId { get; set; }
        public string? SpoolerStatus { get; set; }

        public PrintJobStatus Status { get; set; } = PrintJobStatus.Queued;
        public DateTime CreatedAt { get; } = DateTime.UtcNow;
        public string? LastError { get; set; }
    }
}