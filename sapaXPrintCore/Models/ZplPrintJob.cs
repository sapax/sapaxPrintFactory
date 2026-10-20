using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace sapaXPrintCore.Models
{
    public class ZplPrintJob
    {
        public Guid JobId { get; set; } = Guid.NewGuid();
        public string PrinterId { get; set; } = string.Empty;
        public string Zpl { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}