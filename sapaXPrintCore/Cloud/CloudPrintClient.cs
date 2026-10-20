using sapaXPrintCore.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

namespace sapaXPrintCore.Cloud
{
    public class CloudPrintClient
    {
        private readonly HttpClient _http;

        public CloudPrintClient(string cloudUrl, string agentId, string apiKey)
        {
            _http = new HttpClient
            {
                BaseAddress = new Uri(cloudUrl.TrimEnd('/')),
                Timeout = TimeSpan.FromSeconds(15)
            };

            _http.DefaultRequestHeaders.Add("X-Agent-Id", agentId);
            _http.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
        }

        /// <summary>
        /// Tells the cloud which printers this agent can print to.
        /// Without this, the cloud never hands any job to this agent.
        /// </summary>
        public async Task RegisterAsync(IEnumerable<string> printerIds)
        {
            var response = await _http.PostAsJsonAsync(
                "/api/agents/register",
                printerIds.ToArray());

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                throw new Exception($"Register failed ({(int)response.StatusCode}): {body}");
            }
        }

        public async Task<CloudPrintJobDto?> FetchNextJobAsync()
        {
            var response = await _http.GetAsync("/api/print-jobs/next");

            // No job (NORMAL)
            if (response.StatusCode == HttpStatusCode.NoContent)
                return null;

            // Real error
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                throw new Exception($"Cloud error ({(int)response.StatusCode}): {body}");
            }

            // Job received
            return await response.Content.ReadFromJsonAsync<CloudPrintJobDto>(
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            );
        }

        public async Task MarkCompleted(
            Guid jobId,
            bool success,
            string? error = null,
            string? printerName = null,
            string? printerStatus = null,
            int? spoolerJobId = null,
            string? spoolerStatus = null)
        {
            var payload = new { success, error, printerName, printerStatus, spoolerJobId, spoolerStatus };

            var response = await _http.PostAsJsonAsync(
                $"/api/print-jobs/{jobId}/complete",
                payload
            );

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                throw new Exception($"Complete failed: {body}");
            }
        }
    }
}