using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Transpargo.Controllers;
using Transpargo.Models;

[ApiController]
[Route("user/{shipmentId}/documents")]
[Authorize(Roles = "user")]
public class DocumentController : ControllerBase
{
    private readonly HttpClient _http;
    private readonly string _restUrl;
    private readonly string _projectUrl;
    private readonly string _bucketUrl;
    private readonly string _key;

    public DocumentController(IConfiguration config)
    {
        _http = new HttpClient();
        _projectUrl = config["SUPABASE_URL"];
        _restUrl = $"{_projectUrl}/rest/v1/";
        _bucketUrl = $"{_projectUrl}/storage/v1/object/";
        _key = config["SUPABASE_SERVICE_ROLE_KEY"] ?? config["SUPABASE_KEY"];

        _http.DefaultRequestHeaders.Clear();
        _http.DefaultRequestHeaders.TryAddWithoutValidation("apikey", _key);
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"Bearer {_key}");
    }

    // -------------------- UPLOAD --------------------
    [HttpPost]
    public async Task<IActionResult> UploadDocument(
        int shipmentId,
        [FromForm] IFormFile File,
        [FromForm] string DocumentType)
    {
        if (File == null || File.Length == 0)
            return BadRequest("File missing");

        try
        {
            string bucket = "documents";
            string filePath = $"{bucket}/{shipmentId}/{Guid.NewGuid()}_{File.FileName}";
            string uploadUrl = $"{_bucketUrl}{filePath}";

            using var content = new MultipartFormDataContent();
            var fileContent = new StreamContent(File.OpenReadStream());
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(File.ContentType);
            content.Add(fileContent, "file", File.FileName);

            var uploadReq = new HttpRequestMessage(HttpMethod.Post, uploadUrl);
            uploadReq.Headers.Add("apikey", _key);
            uploadReq.Headers.Add("Authorization", $"Bearer {_key}");
            uploadReq.Content = content;

            var uploadResp = await _http.SendAsync(uploadReq);
            if (!uploadResp.IsSuccessStatusCode)
                return StatusCode((int)uploadResp.StatusCode, await uploadResp.Content.ReadAsStringAsync());

            var body = new
            {
                document_name = DocumentType,
                document_url = filePath,
                s_id = shipmentId
            };

            var insertReq = new HttpRequestMessage(HttpMethod.Post, $"{_restUrl}Document")
            {
                Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
            };
            insertReq.Headers.Add("Prefer", "return=representation");

            var insertResp = await _http.SendAsync(insertReq);
            if (!insertResp.IsSuccessStatusCode)
                return StatusCode((int)insertResp.StatusCode, await insertResp.Content.ReadAsStringAsync());

            return Ok(new { message = "Document uploaded successfully" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = ex.Message });
        }
    }

    // -------------------- GET DOCUMENTS --------------------
    [HttpGet]
    public async Task<IActionResult> GetDocuments(int shipmentId)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, $"{_restUrl}Document?s_id=eq.{shipmentId}");
        req.Headers.Add("apikey", _key);

        var resp = await _http.SendAsync(req);
        var json = await resp.Content.ReadAsStringAsync();

        if (!resp.IsSuccessStatusCode)
            return StatusCode((int)resp.StatusCode, json);

        return Ok(JsonSerializer.Deserialize<List<DocumentRecord>>(json));
    }

    // -------------------- DELETE --------------------
    [HttpDelete("{documentName}")]
    public async Task<IActionResult> DeleteDocument(int shipmentId, string documentName)
    {
        try
        {
            var fetchUrl = $"{_restUrl}Document?s_id=eq.{shipmentId}&document_name=eq.{documentName}";
            var fetchReq = new HttpRequestMessage(HttpMethod.Get, fetchUrl);
            fetchReq.Headers.Add("apikey", _key);
            fetchReq.Headers.Add("Authorization", $"Bearer {_key}");

            var fetchResp = await _http.SendAsync(fetchReq);
            var json = await fetchResp.Content.ReadAsStringAsync();
            var docs = JsonSerializer.Deserialize<List<DocumentRecord>>(json);

            if (docs == null || docs.Count == 0)
                return NotFound("Document not found");

            string filePath = docs[0].document_url;

            var deleteReq = new HttpRequestMessage(HttpMethod.Delete, $"{_bucketUrl}{filePath}");
            deleteReq.Headers.Add("apikey", _key);
            deleteReq.Headers.Add("Authorization", $"Bearer {_key}");

            var deleteResp = await _http.SendAsync(deleteReq);
            if (!deleteResp.IsSuccessStatusCode)
                return StatusCode((int)deleteResp.StatusCode, await deleteResp.Content.ReadAsStringAsync());

            var dbReq = new HttpRequestMessage(HttpMethod.Delete, $"{_restUrl}Document?s_id=eq.{shipmentId}&document_name=eq.{documentName}");
            dbReq.Headers.Add("Prefer", "return=representation");
            dbReq.Headers.Add("apikey", _key);
            dbReq.Headers.Add("Authorization", $"Bearer {_key}");

            var dbResp = await _http.SendAsync(dbReq);
            if (!dbResp.IsSuccessStatusCode)
                return StatusCode((int)dbResp.StatusCode, await dbResp.Content.ReadAsStringAsync());

            return Ok(new { message = "Document deleted successfully" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = ex.Message });
        }
    }

    // -------------------- STATUS UPDATE --------------------
    [HttpPut("status")]
    public async Task<IActionResult> UpdateDocumentStatus(int shipmentId)
    {
        try
        {
            var fetchReq = new HttpRequestMessage(HttpMethod.Get, $"{_restUrl}Shipment?s_id=eq.{shipmentId}");
            fetchReq.Headers.Add("apikey", _key);
            fetchReq.Headers.Add("Authorization", $"Bearer {_key}");

            var fetchResp = await _http.SendAsync(fetchReq);
            var fetchJson = await fetchResp.Content.ReadAsStringAsync();

            if (!fetchResp.IsSuccessStatusCode)
                return StatusCode((int)fetchResp.StatusCode, fetchJson);

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var shipments = JsonSerializer.Deserialize<List<ShipmentRecord>>(fetchJson, options);

            if (shipments == null || shipments.Count == 0)
                return NotFound("Shipment not found");

            var shipment = shipments[0];

            shipment.Sender_log ??= new List<LogEntry>();
            shipment.Receiver_log ??= new List<LogEntry>();

            void UpdateLastToError(List<LogEntry> logs)
            {
                if (logs == null || logs.Count == 0) return;

                var last = logs.Last();
                last.icon = "error";
                last.date = DateTime.Now.ToString("yyyy-MM-dd");
                last.time = DateTime.Now.ToString("hh:mm tt");
            }

            UpdateLastToError(shipment.Sender_log);
            UpdateLastToError(shipment.Receiver_log);

            var patchBody = new
            {
                status = "Document Uploaded",
                Sender_log = shipment.Sender_log,
                Receiver_log = shipment.Receiver_log
            };

            var patchReq = new HttpRequestMessage(HttpMethod.Patch, $"{_restUrl}Shipment?s_id=eq.{shipmentId}");
            patchReq.Headers.Add("apikey", _key);
            patchReq.Headers.Add("Authorization", $"Bearer {_key}");
            patchReq.Headers.Add("Prefer", "return=representation");

            patchReq.Content = new StringContent(JsonSerializer.Serialize(patchBody, options), Encoding.UTF8, "application/json");

            var patchResp = await _http.SendAsync(patchReq);
            var patchJson = await patchResp.Content.ReadAsStringAsync();

            if (!patchResp.IsSuccessStatusCode)
                return StatusCode((int)patchResp.StatusCode, patchJson);

            return Ok(new { success = true, message = "Shipment status changed + last log updated" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = ex.Message });
        }
    }

    // =============================== AI DOCUMENT GENERATOR ===============================

    [AllowAnonymous]
    [HttpGet("required-docs/{hsCode}")]
    public async Task<IActionResult> GetRequiredDocs(
    [FromRoute] int shipmentId,
    [FromRoute] string hsCode
)
    {
        try
        {
            Console.WriteLine("========== GetRequiredDocs START ==========");
            Console.WriteLine($"ShipmentId: {shipmentId}");
            Console.WriteLine($"HS Code: {hsCode}");

            // =====================================================
            // 1️⃣ GET DESTINATION COUNTRY FROM Receiver TABLE
            // =====================================================
            var receiverReq = new HttpRequestMessage(
                HttpMethod.Get,
                $"{_restUrl}Receiver?s_id=eq.{shipmentId}&select=Country"
            );
            receiverReq.Headers.Add("apikey", _key);
            receiverReq.Headers.Add("Authorization", $"Bearer {_key}");

            var receiverResp = await _http.SendAsync(receiverReq);
            var receiverJson = await receiverResp.Content.ReadAsStringAsync();

            Console.WriteLine("---- RECEIVER FETCH ----");
            Console.WriteLine($"STATUS: {receiverResp.StatusCode}");
            Console.WriteLine(receiverJson);

            if (!receiverResp.IsSuccessStatusCode)
                return StatusCode((int)receiverResp.StatusCode, receiverJson);

            var receivers = JsonSerializer.Deserialize<List<ReceiverRow>>(receiverJson);

            if (receivers == null || receivers.Count == 0 ||
                string.IsNullOrWhiteSpace(receivers[0].Country))
            {
                Console.WriteLine("Receiver country NOT FOUND");
                return BadRequest("Destination country not found");
            }

            string country = receivers[0].Country.Trim();
            Console.WriteLine($"Destination Country: {country}");

            // =====================================================
            // 2️⃣ CHECK ShipmentDoc TABLE
            // =====================================================
            var docReq = new HttpRequestMessage(
                HttpMethod.Get,
                $"{_restUrl}ShipmentDoc?hscode=eq.{hsCode}&country=eq.{country}"
            );
            docReq.Headers.Add("apikey", _key);
            docReq.Headers.Add("Authorization", $"Bearer {_key}");

            var docResp = await _http.SendAsync(docReq);
            var docJson = await docResp.Content.ReadAsStringAsync();

            Console.WriteLine("---- SHIPMENTDOC CHECK ----");
            Console.WriteLine($"STATUS: {docResp.StatusCode}");
            Console.WriteLine(docJson);

            if (!docResp.IsSuccessStatusCode)
                return StatusCode((int)docResp.StatusCode, docJson);

            var existingDocs = JsonSerializer.Deserialize<List<ShipmentDocRow>>(docJson);

            if (existingDocs?.FirstOrDefault()?.documents?.Any() == true)
            {
                Console.WriteLine("Documents FOUND in DB. Returning cached data.");
                return Ok(new
                {
                    required_documents = existingDocs[0].documents
                });
            }

            Console.WriteLine("Documents NOT FOUND. Calling AI...");

            // =====================================================
            // 3️⃣ CALL AI (ONLY ONCE)
            // =====================================================
            var aiClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            aiClient.DefaultRequestHeaders.Add(
                "Authorization",
                $"Bearer {Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY")}"
            );

            var payload = new
            {
                model = "deepseek-ai/deepseek-v3.1-terminus",
                messages = new[]
                {
                new
                {
                    role = "user",
                    content =
                        $"Return all mandatory and important documents required for 100% customs clearance " +
                        $"for products under HS Code {hsCode} when importing into {country}. " +
                        $"Do not include conditional phrases such as 'if applicable'. " +
                        $"Do not use slashes (/); use the word 'or'. " +
                        $"Output strictly as a pure JSON array of document names only."
                }
            },
                temperature = 0.2,
            };

            var aiResp = await aiClient.PostAsync(
                "https://integrate.api.nvidia.com/v1/chat/completions",
                new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            );

            var raw = await aiResp.Content.ReadAsStringAsync();

            Console.WriteLine("---- AI RAW RESPONSE ----");
            Console.WriteLine($"STATUS: {aiResp.StatusCode}");
            Console.WriteLine(raw);

            if (!aiResp.IsSuccessStatusCode || string.IsNullOrWhiteSpace(raw))
                throw new Exception("AI request failed");

            using var root = JsonDocument.Parse(raw);

            var content = root.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            Console.WriteLine("---- AI MESSAGE CONTENT ----");
            Console.WriteLine(content);

            int start = content.IndexOf("[");
            int end = content.LastIndexOf("]");

            if (start < 0 || end <= start)
                throw new Exception("Invalid AI JSON array");

            var jsonArray = content.Substring(start, end - start + 1);

            Console.WriteLine("---- AI JSON ARRAY ----");
            Console.WriteLine(jsonArray);

            var docs = JsonSerializer.Deserialize<List<string>>(jsonArray);

            if (docs == null || docs.Count == 0)
                throw new Exception("AI returned empty document list");

            // =====================================================
            // 4️⃣ UPSERT INTO ShipmentDoc TABLE
            // =====================================================
            var upsertBody = new
            {
                hscode = hsCode,
                country = country,
                documents = docs
            };

            Console.WriteLine("---- SHIPMENTDOC UPSERT REQUEST ----");
            Console.WriteLine(JsonSerializer.Serialize(upsertBody));

            var upsertReq = new HttpRequestMessage(
                HttpMethod.Post,
                $"{_restUrl}ShipmentDoc"
            );

            upsertReq.Headers.Add("apikey", _key);
            upsertReq.Headers.Add("Authorization", $"Bearer {_key}");
            upsertReq.Headers.Add("Prefer", "resolution=merge-duplicates");

            upsertReq.Content = new StringContent(
                JsonSerializer.Serialize(upsertBody),
                Encoding.UTF8,
                "application/json"
            );

            var upsertResp = await _http.SendAsync(upsertReq);
            var upsertRespBody = await upsertResp.Content.ReadAsStringAsync();

            Console.WriteLine("---- SHIPMENTDOC UPSERT RESPONSE ----");
            Console.WriteLine($"STATUS: {upsertResp.StatusCode}");
            Console.WriteLine(upsertRespBody);

            if (!upsertResp.IsSuccessStatusCode)
                throw new Exception($"ShipmentDoc upsert failed: {upsertRespBody}");

            Console.WriteLine("========== GetRequiredDocs SUCCESS ==========");

            return Ok(new
            {
                required_documents = docs
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine("========== GetRequiredDocs ERROR ==========");
            Console.WriteLine(ex.ToString());

            return Ok(new
            {
                required_documents = new List<string>
            {
                "Commercial Invoice",
                "Packing List",
                "Bill of Lading",
                "Certificate of Origin",
                "Insurance Certificate",
                "Customs Declaration Form"
            }
            });
        }
    }
    private class ReceiverRow
    {
        public string Country { get; set; }
    }

    private class ShipmentDocRow
    {
        public string hscode { get; set; }
        public string country { get; set; }
        public List<string> documents { get; set; }
    }


    private class ShipmentRecord
    {
        public int s_id { get; set; }
        public string status { get; set; }
        public List<LogEntry> Sender_log { get; set; }
        public List<LogEntry> Receiver_log { get; set; }
    }

    private class DocumentRecord
    {
        public int id { get; set; }
        public string document_name { get; set; }
        public string document_url { get; set; }
        public int s_id { get; set; }
    }
}
