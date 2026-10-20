using PdfSharpCore.Pdf.IO;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Documents;
using System.Windows.Xps;
using System.Windows.Xps.Packaging;

namespace sapaXPrintCore.Printing
{
    public static class PdfToXpsConverter
    {
        public static string Convert(string pdfPath)
        {
            string xpsPath = Path.ChangeExtension(pdfPath, ".xps");

            using var pdf = PdfReader.Open(pdfPath, PdfDocumentOpenMode.ReadOnly);
            using var xpsDoc = new XpsDocument(xpsPath, FileAccess.Write);

            FixedDocument fixedDoc = new FixedDocument();

            foreach (var page in pdf.Pages)
            {
                FixedPage fixedPage = new FixedPage
                {
                    Width = 816,
                    Height = 1056
                };

                fixedDoc.Pages.Add(new PageContent
                {
                    Child = fixedPage
                });
            }

            XpsDocumentWriter writer =
                XpsDocument.CreateXpsDocumentWriter(xpsDoc);

            writer.Write(fixedDoc);
            return xpsPath;
        }
    }
}
