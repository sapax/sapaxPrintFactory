using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Drawing;

namespace sapaXPrintCore.Models
{
    public class PdfRenderResult
    {
        public List<Bitmap> Pages { get; set; } = new();
        public int Dpi { get; set; }
    }
}
