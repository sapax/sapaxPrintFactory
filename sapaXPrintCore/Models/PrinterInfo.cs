using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace sapaXPrintCore.Models
{
    public class PrinterInfo
    {
        public bool IsActive { get; set; }
        public string PrinterName { get; set; } = string.Empty;
        public string PrinterId { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public bool IsDefault { get; set; }
        public string DriverName { get; set; } = string.Empty;
        public bool IsZebra { get; set; }
        public string? ZebraIp { get; set; }

    }
}


