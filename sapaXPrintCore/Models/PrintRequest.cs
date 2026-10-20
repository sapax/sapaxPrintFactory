using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace sapaXPrintCore.Models
{
    public class PrintRequest
    {
        // REQUIRED: Cloud job reference
        public Guid JobId { get; set; }
        public string? PrinterName { get; set; }   // use name
        public string? PrinterId { get; set; } = string.Empty; // use id
        public string FileName { get; set; } = string.Empty;
        public string FileBase64 { get; set; } = string.Empty;
    }
}

