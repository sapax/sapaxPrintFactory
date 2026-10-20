using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using PdfiumViewer;

namespace sapaXPrintCore.Printing
{
    public static class PdfiumRenderer
    {
        public static List<Bitmap> Render(byte[] pdfBytes, int dpi)
        {
            if (pdfBytes == null || pdfBytes.Length == 0)
                throw new ArgumentException("PDF bytes are empty");

            var bitmaps = new List<Bitmap>();

            using var stream = new MemoryStream(pdfBytes);
            using var document = PdfDocument.Load(stream);

            for (int pageIndex = 0; pageIndex < document.PageCount; pageIndex++)
            {
                var pageSize = document.PageSizes[pageIndex];

                int widthPx = (int)(pageSize.Width / 72f * dpi);
                int heightPx = (int)(pageSize.Height / 72f * dpi);

                var bitmap = new Bitmap(widthPx, heightPx);

                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.Clear(Color.White);

                    document.Render(
                        pageIndex,
                        graphics,
                        dpi,
                        dpi,
                        new Rectangle(0, 0, widthPx, heightPx),
                        PdfRenderFlags.Annotations
                    );
                }

                bitmaps.Add(bitmap);
            }

            return bitmaps;
        }
    }
}
