using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System;
using System.Runtime.InteropServices;

namespace sapaXPrintCore.Printing
{
    internal static class PdfiumNative
    {
        private const string DLL = "pdfium.dll";

        [DllImport(DLL)]
        public static extern void FPDF_InitLibrary();

        [DllImport(DLL)]
        public static extern void FPDF_DestroyLibrary();

        [DllImport(DLL)]
        public static extern nint FPDF_LoadMemDocument(
            byte[] data, int size, string password);

        [DllImport(DLL)]
        public static extern void FPDF_CloseDocument(nint document);

        [DllImport(DLL)]
        public static extern int FPDF_GetPageCount(nint document);

        [DllImport(DLL)]
        public static extern nint FPDF_LoadPage(nint document, int pageIndex);

        [DllImport(DLL)]
        public static extern void FPDF_ClosePage(nint page);

        [DllImport(DLL)]
        public static extern double FPDF_GetPageWidth(nint page);

        [DllImport(DLL)]
        public static extern double FPDF_GetPageHeight(nint page);

        [DllImport(DLL)]
        public static extern nint FPDFBitmap_Create(int width, int height, int alpha);

        [DllImport(DLL)]
        public static extern void FPDFBitmap_FillRect(
            nint bitmap, int left, int top, int width, int height, uint color);

        [DllImport(DLL)]
        public static extern nint FPDFBitmap_GetBuffer(nint bitmap);

        [DllImport(DLL)]
        public static extern int FPDFBitmap_GetStride(nint bitmap);

        [DllImport(DLL)]
        public static extern void FPDFBitmap_Destroy(nint bitmap);

        [DllImport(DLL)]
        public static extern void FPDF_RenderPageBitmap(
            nint bitmap,
            nint page,
            int startX,
            int startY,
            int sizeX,
            int sizeY,
            int rotate,
            int flags
        );
    }
}
