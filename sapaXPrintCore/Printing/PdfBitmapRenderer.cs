using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace sapaXPrintCore.Printing
{
    public static class PdfBitmapRenderer
    {
        public static List<Bitmap> Render(byte[] pdfBytes, int dpi)
        {
            var bitmaps = new List<Bitmap>();

            PdfiumNative.FPDF_InitLibrary();

            nint doc = PdfiumNative.FPDF_LoadMemDocument(
                pdfBytes, pdfBytes.Length, null);

            int pageCount = PdfiumNative.FPDF_GetPageCount(doc);

            for (int i = 0; i < pageCount; i++)
            {
                nint page = PdfiumNative.FPDF_LoadPage(doc, i);

                double widthPt = PdfiumNative.FPDF_GetPageWidth(page);
                double heightPt = PdfiumNative.FPDF_GetPageHeight(page);

                int widthPx = (int)(widthPt / 72 * dpi);
                int heightPx = (int)(heightPt / 72 * dpi);

                nint bmp = PdfiumNative.FPDFBitmap_Create(widthPx, heightPx, 0);
                PdfiumNative.FPDFBitmap_FillRect(
                    bmp, 0, 0, widthPx, heightPx, 0xFFFFFFFF);

                PdfiumNative.FPDF_RenderPageBitmap(
                    bmp, page, 0, 0, widthPx, heightPx, 0, 0);

                nint buffer = PdfiumNative.FPDFBitmap_GetBuffer(bmp);
                int stride = PdfiumNative.FPDFBitmap_GetStride(bmp);

                Bitmap bitmap = new Bitmap(
                    widthPx,
                    heightPx,
                    stride,
                    PixelFormat.Format32bppArgb,
                    buffer
                );

                bitmaps.Add(new Bitmap(bitmap));

                PdfiumNative.FPDFBitmap_Destroy(bmp);
                PdfiumNative.FPDF_ClosePage(page);
            }

            PdfiumNative.FPDF_CloseDocument(doc);
            PdfiumNative.FPDF_DestroyLibrary();

            return bitmaps;
        }
    }
}
