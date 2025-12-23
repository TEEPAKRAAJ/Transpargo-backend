using System.Net.Http;
using System.Text;
using System.Text.Json;
using Transpargo.Models;

namespace Transpargo.Services
{
    public class AiRiskService
    {
        private readonly HttpClient _client;
        private readonly string apiKey;

        public AiRiskService(IConfiguration config)
        {
            _client = new HttpClient();
            apiKey = config["DEEPSEEK_API_KEY"];
        }

        public async Task<RiskAIResponse?> AnalyzeAsync(RiskAIRequest input)
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
if HS Code is invalid or you're unsure, or if the confidence is < 100 use:
- riskLevel: ""Unknown""
- riskScore: 0
- confidence: 0.0
- empty arrays for documents/risks/recommendations
- summary: ""Invalid HS Code""";

            var body = new
            {
                model = "deepseek-ai/deepseek-v3.1-terminus",
                messages = new[] {
                    new { role = "system", content = "You are a JSON-only API. Return valid JSON objects with no markdown formatting, no code blocks, no explanations. Only output the raw JSON object." },
                    new { role = "user", content = prompt }
                },
                temperature = 0.2,
                max_tokens = 700,
                response_format = new { type = "json_object" }
            };

            var req = new HttpRequestMessage(HttpMethod.Post,
                "https://integrate.api.nvidia.com/v1/chat/completions");

            req.Headers.Add("Authorization", $"Bearer {apiKey}");
            req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

            var res = await _client.SendAsync(req);
            if (!res.IsSuccessStatusCode)
            {
                return ErrorFallback(input.HSCode, $"API error: {res.StatusCode}");
            }

            var raw = await res.Content.ReadAsStringAsync();

            // Extract message.content JSON
            string json = "";
            try
            {
                using var root = JsonDocument.Parse(raw);
                json = root.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString() ?? "";
            }
            catch (Exception ex)
            {
                return ErrorFallback(input.HSCode, $"API response parsing failed: {ex.Message}");
            }

            // Clean the JSON string (remove any potential markdown or whitespace)
            json = json.Trim();

            // Remove markdown code blocks if present
            if (json.StartsWith(""))
            {
                int firstNewline = json.IndexOf('\n');
                int lastBackticks = json.LastIndexOf("");
                if (firstNewline > 0 && lastBackticks > firstNewline)
                {
                    json = json.Substring(firstNewline + 1, lastBackticks - firstNewline - 1).Trim();
                }
            }

            // Ensure we have valid JSON boundaries
            if (!json.StartsWith("{") || !json.EndsWith("}"))
            {
                int start = json.IndexOf("{");
                int end = json.LastIndexOf("}");
                if (start >= 0 && end > start)
                {
                    json = json.Substring(start, end - start + 1);
                }
                else
                {
                    return ErrorFallback(input.HSCode, "Response did not contain valid JSON");
                }
            }

            // Deserialize to RiskAIResponse
            RiskAIResponse? data = null;
            try
            {
                data = JsonSerializer.Deserialize<RiskAIResponse>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch (JsonException ex)
            {
                return ErrorFallback(input.HSCode, $"JSON parsing failed: {ex.Message}");
            }

            // Validate response
            if (data == null)
            {
                return ErrorFallback(input.HSCode, "Deserialization returned null");
            }

            // Ensure all required fields are present (initialize if null)
            data.hsCode ??= input.HSCode;
            data.requiredDocuments ??= new List<string>();
            data.keyRisks ??= new List<string>();
            data.recommendations ??= new List<string>();
            data.summary ??= "No summary provided";
            data.riskLevel ??= "Unknown";

            // Validate numeric ranges
            if (data.riskScore < 0) data.riskScore = 0;
            if (data.riskScore > 100) data.riskScore = 100;
            if (data.confidence < 0.0) data.confidence = 0.0;
            if (data.confidence > 1.0) data.confidence = 1.0;

            return data;
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