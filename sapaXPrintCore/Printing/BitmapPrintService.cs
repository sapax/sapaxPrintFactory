using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Printing;

namespace sapaXPrintCore.Printing
{
    public static class BitmapPrintService
    {
        /// <param name="documentName">
        /// Name of the Windows spooler job. Pass a unique name (SpoolerJobMonitor.MakeDocumentName)
        /// so the job can be found again to confirm it really printed.
        /// </param>
        public static void PrintBitmaps(
            List<Bitmap> pages,
            string printerName,
            string? documentName = null
        )
        {
            if (pages == null || pages.Count == 0)
                throw new ArgumentException("No pages to print");

            int currentPage = 0;

            PrintDocument doc = new PrintDocument
            {
                DocumentName = string.IsNullOrWhiteSpace(documentName) ? "sapaX document" : documentName,
                PrinterSettings = new PrinterSettings
                {
                    PrinterName = printerName
                }
            };

            if (!doc.PrinterSettings.IsValid)
                throw new InvalidOperationException($"Printer '{printerName}' is not valid or not installed.");

            doc.PrintPage += (sender, e) =>
            {
                Bitmap bmp = pages[currentPage];

                Rectangle target = e.MarginBounds;

                // Keep aspect ratio
                float ratio = Math.Min(
                    (float)target.Width / bmp.Width,
                    (float)target.Height / bmp.Height
                );

                int width = (int)(bmp.Width * ratio);
                int height = (int)(bmp.Height * ratio);

                Rectangle drawRect = new Rectangle(
                    target.Left,
                    target.Top,
                    width,
                    height
                );

                e.Graphics.DrawImage(bmp, drawRect);

                currentPage++;
                e.HasMorePages = currentPage < pages.Count;
            };

            doc.Print();
        }
    }
}