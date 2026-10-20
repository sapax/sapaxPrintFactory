using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace sapaXPrintCore.Models
{
    public class CloudPrintJobDto
    {
        public Guid Id { get; set; }
        public string PrinterId { get; set; } = "";
        public string? Zpl { get; set; }
        public string? FileName { get; set; }
        public string? FileBase64 { get; set; }
    }
}
