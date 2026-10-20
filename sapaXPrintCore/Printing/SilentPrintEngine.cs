using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

namespace sapaXPrintCore.Printing
{
    public static class SilentPrintEngine
    {
        public static void Print(string pdfPath, string printerName)
        {
            string xpsPath = PdfToXpsConverter.Convert(pdfPath);
            XpsPrintService.Print(xpsPath, printerName);
            File.Delete(xpsPath);
        }
    }
}
