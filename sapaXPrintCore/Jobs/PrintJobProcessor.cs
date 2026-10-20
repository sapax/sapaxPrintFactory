using System;
using System.Collections.Generic;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using sapaXPrintCore.Jobs;
using sapaXPrintCore.Models;
using sapaXPrintCore.Printers;

public static class PrintJobProcessor
{

    public static void Process(PrintRequest request)
    {
        Console.WriteLine(">>> PrintJobProcessor.Process HIT <<<");
        Console.WriteLine("PRINT JOB RECEIVED");
        Console.WriteLine($"PrinterId: [{request.PrinterId}]");
        Console.WriteLine($"FileName: [{request.FileName}]");

        // 1️⃣ Validate request
        if (string.IsNullOrWhiteSpace(request.PrinterId))
            throw new Exception("PrinterId is EMPTY – JSON binding failed");

        if (string.IsNullOrWhiteSpace(request.FileBase64))
            throw new Exception("FileBase64 is EMPTY");

        // 2️⃣ Resolve printer locally (ID → Name)
        var printer = PrinterDiscoveryService.GetPrinterById(request.PrinterId);

        if (printer == null)
            throw new Exception($"Printer not found for ID {request.PrinterId}");

        Console.WriteLine($"Resolved PrinterName: {printer.PrinterName}");

        // 3️⃣ Decode PDF (store INSIDE job)
        byte[] pdfBytes;
        try
        {
            pdfBytes = Convert.FromBase64String(request.FileBase64);

        }
        catch
        {
            throw new Exception("Invalid Base64 PDF data");
        }

        // 4️⃣ Create print job (DO NOT PRINT HERE)
        var job = new PrintJob
        {
            CloudJobId = request.JobId, // REQUIRED
            PrinterId = request.PrinterId,
            PrinterName = printer.PrinterName,
            FileName = request.FileName,
            PdfBytes = pdfBytes,
            Status = PrintJobStatus.Queued
        };


        // 5️⃣ Enqueue job
        PrintJobQueue.Enqueue(job);

        Console.WriteLine($"JOB QUEUED: {job.JobId}");
        Console.WriteLine($"PRINTING ON: {job.PrinterId}");


    }
}
