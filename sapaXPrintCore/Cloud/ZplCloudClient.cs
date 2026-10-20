using sapaXPrintCore.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace sapaXPrintCore.Cloud
{
    public class ZplCloudClient
    {
        private readonly HttpClient _http;
        private readonly string _cloudUrl;
        private readonly string _agentId;
        private readonly string _apiKey;

        public ZplCloudClient(string cloudUrl, string agentId, string apiKey)
        {
            _cloudUrl = cloudUrl;
            _agentId = agentId;
            _apiKey = apiKey;
            _http = new HttpClient();
        }

        public async Task<ZplPrintJobDto?> FetchNextAsync()
        {
            var req = new HttpRequestMessage(
                HttpMethod.Get,
                $"{_cloudUrl}/api/print-jobs/zpl/next"
            );

            req.Headers.Add("X-Agent-Id", _agentId);
            req.Headers.Add("X-API-KEY", _apiKey);

            var res = await _http.SendAsync(req);
            if (!res.IsSuccessStatusCode)
                return null;

            var json = await res.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<ZplPrintJobDto>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
    }

}
