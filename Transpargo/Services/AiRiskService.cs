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
            apiKey = config["DEEPSEEK_API_KEY"]; // Must exist in appsettings.json / env
        }

        public async Task<RiskAIResponse?> AnalyzeAsync(RiskAIRequest input)
        {
            string prompt = $@"
You are a customs compliance AI. 
Return ONLY JSON. No explanations. No markdown.

If HS Code is valid → generate realistic trade compliance output.
If unsure BUT HS Code looks real → still produce documents.

VALID RESPONSE FORMAT:
{{
  ""hsCode"": ""{input.HSCode}"",
  ""requiredDocuments"": [""Commercial Invoice"", ""Packing List"", ""Certificate of Origin"", ""Any other relevant docs""],
  ""keyRisks"": [""Risk1"", ""Risk2""],
  ""recommendations"": [""Recommendation1"", ""Recommendation2""],
  ""summary"": ""Short summary about compliance requirements"",
  ""riskLevel"": ""Low|Medium|High"",
  ""riskScore"": 10-90,
  ""confidence"": 0.5-1.0
}}

INVALID RESPONSE FORMAT:
{{
  ""hsCode"": ""{input.HSCode}"",
  ""requiredDocuments"": [],
  ""keyRisks"": [],
  ""recommendations"": [],
  ""summary"": ""Invalid HS Code"",
  ""riskLevel"": ""Unknown"",
  ""riskScore"": 0,
  ""confidence"": 0.0
}}
";

            var body = new
            {
                model = "deepseek-ai/deepseek-v3.1-terminus",
                messages = new[] {
                    new{ role="system", content="Return ONLY JSON strictly" },
                    new{ role="user", content=prompt }
                },
                temperature = 0.3,
                max_tokens = 700
            };

            var req = new HttpRequestMessage(HttpMethod.Post,
                "https://integrate.api.nvidia.com/v1/chat/completions");

            req.Headers.Add("Authorization", $"Bearer {apiKey}");
            req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

            var res = await _client.SendAsync(req);
            var raw = await res.Content.ReadAsStringAsync();


            //------------------ Extract message.content JSON only ------------------//
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
            catch
            {
                return ErrorFallback(input.HSCode, "API did not return JSON format");
            }

            //------------------ Clean & isolate JSON ------------------//
            int start = json.IndexOf("{");
            int end = json.LastIndexOf("}");
            if (start < 0 || end < 0)
                return ErrorFallback(input.HSCode, "Response did not contain JSON");

            json = json.Substring(start, end - start + 1)
                       .Replace("```json", "")
                       .Replace("```", "")
                       .Trim();

            //------------------ Deserialize ------------------//
            RiskAIResponse? data = null;
            try
            {
                data = JsonSerializer.Deserialize<RiskAIResponse>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch
            {
                return ErrorFallback(input.HSCode, "JSON parsing failed");
            }

            //------------------ Final Validation ------------------//
            if (data == null)
                return ErrorFallback(input.HSCode, "Empty response");

            // Prevent unknown for valid responses
            if (data.requiredDocuments == null || data.requiredDocuments.Count == 0)
            {
                data.requiredDocuments = new() { "Commercial Invoice", "Packing List", "Certificate of Origin" };
                data.keyRisks = new() { "Additional compliance verification needed" };
                data.recommendations = new() { "Provide missing certificates if required" };
                data.riskLevel = "Medium";
                data.riskScore = 50;
                data.summary = "AI returned incomplete fields, fallback docs applied.";
                data.confidence = 0.6;
            }

            return data;
        }


        // 🔥 Reusable fallback
        private RiskAIResponse ErrorFallback(string hs, string msg)
        {
            return new RiskAIResponse
            {
                hsCode = hs,
                riskLevel = "Medium",
                riskScore = 50,
                confidence = 0.5,
                summary = msg,
                requiredDocuments = new() { "Commercial Invoice", "Packing List", "Certificate of Origin" },
                keyRisks = new() { "Incomplete compliance info" },
                recommendations = new() { "Check HS code or provide more details" }
            };
        }
    }
}
