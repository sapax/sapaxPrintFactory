using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using sapaXPrintCore.Printers;
using sapaXPrintCore.Models;

namespace sapaXPrintMaster.Services
{
    public class LocalHttpServer
    {
        private HttpListener? _listener;

        public void Start()
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add("https://localhost:7289/"); //_listener.Prefixes.Add("http://localhost:5209");
            _listener.Start();

            System.Diagnostics.Debug.WriteLine("HTTP SERVER STARTED on https://localhost:7289/");//http://localhost:5209

            Task.Run(ListenLoop);
        }

        private async Task ListenLoop()
        {
            while (_listener != null && _listener.IsListening)
            {
                try
                {
                    var context = await _listener.GetContextAsync();
                    _ = Task.Run(() => HandleRequest(context));
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(ex.ToString());
                }
            }
        }

        private async Task HandleRequest(HttpListenerContext context)
        {
            try
            {
                // Only allow POST /print
                if (context.Request.HttpMethod != "POST" ||
                    context.Request.Url?.AbsolutePath != "/print")
                {
                    context.Response.StatusCode = 404;
                    context.Response.Close();
                    return;
                }

                // Read request body
                using var reader = new StreamReader(context.Request.InputStream);
                string body = await reader.ReadToEndAsync();

                var request = JsonSerializer.Deserialize<PrintRequest>(
                    body,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    }
                );

                if (request == null)
                {
                    context.Response.StatusCode = 400;
                    WriteJson(context, "{\"success\":false,\"message\":\"Invalid request\"}");
                    return;
                }

                // ✅ RESPOND IMMEDIATELY (VERY IMPORTANT)
                WriteJson(context, "{\"success\":true,\"message\":\"Job accepted\"}");

                // ✅ PROCESS PRINT IN BACKGROUND
                Task.Run(() =>
                {
                    try
                    {
                        System.Diagnostics.Debug.WriteLine("PRINT JOB RECEIVED");
                        System.Diagnostics.Debug.WriteLine($"PrinterId: {request.PrinterId}");
                        System.Diagnostics.Debug.WriteLine($"FileName: {request.FileName}");

                        PrintJobProcessor.Process(request);

                        System.Diagnostics.Debug.WriteLine("PRINT JOB COMPLETED");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine("PRINT JOB FAILED");
                        System.Diagnostics.Debug.WriteLine(ex.ToString());
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex.ToString());
            }
        }

        private void WriteJson(HttpListenerContext context, string json)
        {
            byte[] buffer = Encoding.UTF8.GetBytes(json);
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = buffer.Length;
            context.Response.OutputStream.Write(buffer, 0, buffer.Length);
            context.Response.OutputStream.Close();
        }
    }
}
