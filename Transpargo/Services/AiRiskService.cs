using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Transpargo.Models;

namespace Transpargo.Services
{
    public class AiRiskService
    {
        private readonly HttpClient _client;
        private readonly string _apiKey;

        public AiRiskService(IConfiguration config)
        {
            _apiKey = config["DEEPSEEK_API_KEY"];
            _client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
        }

        public async Task<RiskAIResponse> AnalyzeAsync(RiskAIRequest input)
        {
            string prompt = $@"
Analyze customs compliance for:
- HS Code: {input.HSCode}
- Product Category: {input.ProductCategory}
- Destination Country: {input.DestinationCountry}

Return ONLY a JSON object with this EXACT structure (no markdown, no explanations):

{{
  ""hsCode"": ""string"",
  ""requiredDocuments"": [""string array""],
  ""keyRisks"": [""string array""],
  ""recommendations"": [""string array""],
  ""summary"": ""string"",
  ""riskLevel"": ""Low or Medium or High or Unknown"",
  ""riskScore"": number between 0-100,
  ""confidence"": number between 0.0-1.0
}}
If the HS code is not the correct value for that product category in the destination country then return as follows or
if HS Code is invalid or you're unsure, or if the confidence is < 90 use:
- riskLevel: ""Unknown""
- riskScore: 0
- confidence: 0.0
- empty arrays for documents/risks/recommendations
- summary: ""Invalid HS Code""";

            var body = new
            {
                model = "deepseek-ai/deepseek-v3.1-terminus",
                messages = new[]
                {
                    new { role = "system", content = "You are a JSON-only API. Return valid JSON objects with no markdown formatting, no code blocks, no explanations. Only output the raw JSON object." },
                    new { role = "user", content = prompt }
                },
                temperature = 0.1,
                max_tokens = 500
            };

            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    Console.WriteLine($"---- AI REQUEST (Attempt {attempt}) ----");

                    // ✅ CREATE REQUEST INSIDE LOOP
                    using var request = new HttpRequestMessage(
                        HttpMethod.Post,
                        "https://integrate.api.nvidia.com/v1/chat/completions"
                    );

                    request.Headers.Add("Authorization", $"Bearer {_apiKey}");
                    request.Content = new StringContent(
                        JsonSerializer.Serialize(body),
                        Encoding.UTF8,
                        "application/json"
                    );

                    var response = await _client.SendAsync(request);
                    var raw = await response.Content.ReadAsStringAsync();

                    
                    

                    if (!response.IsSuccessStatusCode)
                        throw new Exception($"API error {response.StatusCode}");

                    using var root = JsonDocument.Parse(raw);

                    var content = root.RootElement
                        .GetProperty("choices")[0]
                        .GetProperty("message")
                        .GetProperty("content")
                        .GetString();

                    if (string.IsNullOrWhiteSpace(content))
                        throw new Exception("Empty AI content");

                    // ✅ STRIP MARKDOWN SAFELY
                    var json = StripMarkdown(content);
                    Console.WriteLine($"json AI RESPONSE:\n{json}");

                    // ✅ VALIDATE JSON
                    using var _ = JsonDocument.Parse(json);

                    var data = JsonSerializer.Deserialize<RiskAIResponse>(
                        json,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                    );

                    if (data == null)
                        throw new Exception("Deserialization failed");

                    // Defaults
                    data.hsCode ??= input.HSCode;
                    data.requiredDocuments ??= new();
                    data.keyRisks ??= new();
                    data.recommendations ??= new();
                    data.summary ??= "No summary";
                    data.riskLevel ??= "Unknown";

                    data.riskScore = Math.Clamp(data.riskScore, 0, 100);
                    data.confidence = Math.Clamp(data.confidence, 0, 1);

                    return data;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"AI ERROR (Attempt {attempt}): {ex.Message}");

                    if (attempt == 3)
                        return ErrorFallback(input.HSCode, ex.Message);

                    await Task.Delay(1000 * attempt);
                }
            }

            return ErrorFallback(input.HSCode, "AI failure");
        }

        private static string StripMarkdown(string input)
        {
            input = input.Trim();

            if (input.StartsWith("```"))
            {
                int firstNewLine = input.IndexOf('\n');
                int lastFence = input.LastIndexOf("```");

                if (firstNewLine != -1 && lastFence > firstNewLine)
                {
                    return input.Substring(firstNewLine + 1, lastFence - firstNewLine - 1).Trim();
                }
            }

            return input;
        }

        private RiskAIResponse ErrorFallback(string hs, string msg)
        {
            return new RiskAIResponse
            {
                hsCode = hs,
                riskLevel = "Unknown",
                riskScore = 0,
                confidence = 0.0,
                summary = msg,
                requiredDocuments = new List<string>(),
                keyRisks = new List<string> { "Unable to process request" },
                recommendations = new List<string> { "Verify HS code and try again" }
            };
        }
    }
}
