using System.Net.Http.Json;
using System.Text.Json;

namespace Transpargo.Services
{
    public interface INimService
    {
        Task<string> AskAsync(string question);
    }

    public class NimService : INimService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;

        public NimService(IConfiguration config, HttpClient httpClient)
        {
            _httpClient = httpClient;
            _apiKey = config["DEEPSEEK_API_KEY"] ?? throw new ArgumentNullException("DEEPSEEK_API_KEY missing");

            _httpClient.BaseAddress = new Uri("https://integrate.api.nvidia.com/v1/");
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
        }

        public async Task<string> AskAsync(string question)
        {
            var payload = new
            {
                model = "deepseek-ai/deepseek-v3.1-terminus",
                messages = new[] { new { role = "user", content = question } },
                temperature = 0.2,
                top_p = 0.7,
                max_tokens = 8192
            };

            var response = await _httpClient.PostAsJsonAsync("chat/completions", payload);
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new Exception($"DeepSeek API error ({response.StatusCode}): {content}");

            using var doc = JsonDocument.Parse(content);
            return doc.RootElement
                      .GetProperty("choices")[0]
                      .GetProperty("message")
                      .GetProperty("content")
                      .GetString() ?? "";
        }
    }
}

