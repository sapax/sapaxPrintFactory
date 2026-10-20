using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Printing;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Xps.Packaging;

namespace sapaXPrintCore.Printing
{
    public static class XpsPrintService
    {
        public static void Print(string xpsPath, string printerName)
        {
            PrintServer server = new PrintServer();
            PrintQueue queue = server.GetPrintQueue(printerName);

            using XpsDocument xps = new XpsDocument(xpsPath, FileAccess.Read);
            var writer = PrintQueue.CreateXpsDocumentWriter(queue);

            writer.Write(xps.GetFixedDocumentSequence());
        }
    }
}
