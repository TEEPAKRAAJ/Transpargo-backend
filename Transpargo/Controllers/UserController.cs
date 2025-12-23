using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Text.Json;
using Transpargo.Models;

[ApiController]
[Route("user")]
[Authorize(Roles = "user")]  // Protect user routes
public class UserController : ControllerBase
{
    private readonly HttpClient _http;
    private readonly string _url;
    private readonly string _key;

    public UserController(IConfiguration config)
    {
        _http = new HttpClient();
        _url = config["SUPABASE_URL"] + "/rest/v1/";
        _key = config["SUPABASE_SERVICE_ROLE_KEY"] ?? config["SUPABASE_KEY"];

        _http.DefaultRequestHeaders.Clear();
        _http.DefaultRequestHeaders.TryAddWithoutValidation("apikey", _key);
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"Bearer {_key}");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Prefer", "return=representation");
    }

    // ------------------ GET KNOWLEDGE BASE ------------------
    [HttpGet("knowledge")]
    public async Task<IActionResult> GetKnowledge()
    {
        var resp = await _http.GetAsync(_url + "Knowledge_Base");
        var json = await resp.Content.ReadAsStringAsync();

        if (!resp.IsSuccessStatusCode)
        {
            return StatusCode((int)resp.StatusCode, new
            {
                message = "Error reading Knowledge_Base table",
                details = json
            });
        }

        // 🔥 FIX: return JSON as-is
        return Content(json, "application/json");
    }



    [HttpGet("{id}")]
    public async Task<IActionResult> GetUserShipments(int id)
    {
        string query =
            $"{_url}Shipment?id=eq.{id}" +
            "&select=s_id,status,created_at,Receiver(*),Product(*)";

        var resp = await _http.GetAsync(query);
        var json = await resp.Content.ReadAsStringAsync();

        if (!resp.IsSuccessStatusCode)
        {
            return StatusCode((int)resp.StatusCode, json);
        }

        var raw = JsonSerializer.Deserialize<JsonElement>(json);

        var list = raw.EnumerateArray().Select(item =>
        {
            // ----- Receiver -----
            var receiverArray = item.GetProperty("Receiver").EnumerateArray();
            var receiver = receiverArray.FirstOrDefault();

            string receiverName =
                receiver.ValueKind != JsonValueKind.Undefined &&
                receiver.TryGetProperty("Name", out var rname)
                ? rname.GetString()
                : "N/A";

            // ----- Product -----
            var productArray = item.GetProperty("Product").EnumerateArray();
            var product = productArray.FirstOrDefault();

            string productName =
                product.ValueKind != JsonValueKind.Undefined &&
                product.TryGetProperty("type", out var pname)
                ? pname.GetString()
                : "N/A";

            decimal productValue =
                product.ValueKind != JsonValueKind.Undefined &&
                product.TryGetProperty("value", out var pvalue)
                ? pvalue.GetDecimal()
                : 0;

            return new
            {
                shipment_id = item.GetProperty("s_id").GetInt32(),
                status = item.GetProperty("status").GetString(),
                updated_at = item.GetProperty("created_at").GetString(),
                receiver_name = receiverName,
                product_name = productName,   // <-- using `type` column
                value = productValue          // <-- using `value` column
            };
        }).ToList();

        return Ok(list);
    }

    // ===================== GET SHIPMENT DETAILS BY SHIPMENT ID =====================
    [HttpGet("shipment/{shipmentId}")]
    public async Task<IActionResult> GetShipmentDetails(int shipmentId)
    {
        string query =
            $"{_url}Shipment?s_id=eq.{shipmentId}"
            + "&select=s_id,status,created_at,"
            + "Product:Product(*),"
            + "Sender:Sender(*),"
            + "Receiver:Receiver(*)";

        var resp = await _http.GetAsync(query);
        var json = await resp.Content.ReadAsStringAsync();

        if (!resp.IsSuccessStatusCode)
        {
            return StatusCode((int)resp.StatusCode, new
            {
                message = "Error fetching shipment details",
                details = json
            });
        }

        var arr = JsonSerializer.Deserialize<JsonElement[]>(json);
        if (arr.Length == 0)
            return NotFound(new { message = "Shipment not found" });

        var item = arr[0];

        // Product table is 1-to-1 → returned as array
        var product = item.GetProperty("Product")[0];
        var sender = item.GetProperty("Sender")[0];
        var receiver = item.GetProperty("Receiver")[0];

        var result = new
        {
            shipment_id = item.GetProperty("s_id").GetInt32(),
            product_name = product.GetProperty("type").GetString(),
            origin = sender.GetProperty("Country").GetString(),
            destination = receiver.GetProperty("Country").GetString(),
            product_category = product.GetProperty("category").GetString(),
            declared_value = product.GetProperty("value").GetDecimal(),
            weight = product.GetProperty("weight").GetDecimal(),
            hs_code = product.GetProperty("sender_hs_code").GetString() ?? "",
            rec_hs_code = product.GetProperty("hs_code").GetString() ?? ""

        };

        return Ok(result);
    }

    // ------------------------------------------------------
    // GET RESOLUTION DETAILS (USED BY FRONTEND)
    // ------------------------------------------------------
    [HttpGet("reason/{shipmentId}")]
    public async Task<IActionResult> GetResolutionData(int shipmentId)
    {
        string query =
            $"{_url}Shipment?s_id=eq.{shipmentId}"
            + "&select=s_id,status,Reason,Additional_docs,Sender(Country),Receiver(Country)";

        var resp = await _http.GetAsync(query);
        var json = await resp.Content.ReadAsStringAsync();

        if (!resp.IsSuccessStatusCode)
            return StatusCode((int)resp.StatusCode, json);

        var arr = JsonSerializer.Deserialize<JsonElement[]>(json);
        if (arr.Length == 0)
            return NotFound(new { message = "Shipment not found" });

        var item = arr[0];

        // additional_docs is stored as JSON
        var extraDocs = item.GetProperty("Additional_docs").ToString();
        var parsedDocs = JsonSerializer.Deserialize<List<Dictionary<string, string>>>(extraDocs);

        return Ok(new
        {
            shipment_id = item.GetProperty("s_id").GetInt32(),
            status = item.GetProperty("status").GetString(),
            reason = item.GetProperty("Reason").GetString(),
            origin = item.GetProperty("Sender")[0].GetProperty("Country").GetString(),
            destination = item.GetProperty("Receiver")[0].GetProperty("Country").GetString(),
            required_docs = parsedDocs
        });
    }

    [HttpGet("get-sender/{id}")]
    public async Task<IActionResult> GetSenderTimeline(int id)
    {
        string query =
            $"{_url}Shipment?s_id=eq.{id}&select=Sender_log";

        var resp = await _http.GetAsync(query);
        var json = await resp.Content.ReadAsStringAsync();

        if (!resp.IsSuccessStatusCode)
            return StatusCode((int)resp.StatusCode, json);

        var arr = JsonSerializer.Deserialize<JsonElement[]>(json);
        if (arr == null || arr.Length == 0)
            return NotFound("Shipment not found");

        // 🔥 Convert JsonElement → List<LogEntry>
        var senderLogElement = arr[0].GetProperty("Sender_log");

        var senderLogs = JsonSerializer.Deserialize<List<LogEntry>>(
            senderLogElement.GetRawText()
        );

        return Ok(senderLogs ?? new List<LogEntry>());
    }



    [HttpGet("get-receiver/{id}")]
    public async Task<IActionResult> GetReceiverTimeline(int id)
    {
        string query =
            $"{_url}Shipment?s_id=eq.{id}&select=Receiver_log";

        var resp = await _http.GetAsync(query);
        var json = await resp.Content.ReadAsStringAsync();

        if (!resp.IsSuccessStatusCode)
            return StatusCode((int)resp.StatusCode, json);

        var arr = JsonSerializer.Deserialize<JsonElement[]>(json);
        if (arr == null || arr.Length == 0)
            return NotFound("Shipment not found");

        var receiverLogElement = arr[0].GetProperty("Receiver_log");

        var receiverLogs = JsonSerializer.Deserialize<List<LogEntry>>(
            receiverLogElement.GetRawText()
        );

        return Ok(receiverLogs ?? new List<LogEntry>());
    }


    [HttpGet("verify-receiver/{email}/{shipmentId}")]
    public async Task<IActionResult> VerifyReceiver(string email, int shipmentId)
    {
        if (string.IsNullOrEmpty(email))
            return BadRequest("Email missing");

        string query =
            $"{_url}Receiver?Email=eq.{email}&s_id=eq.{shipmentId}";

        var resp = await _http.GetAsync(query);
        var json = await resp.Content.ReadAsStringAsync();

        if (!resp.IsSuccessStatusCode)
            return StatusCode((int)resp.StatusCode, json);

        var arr = JsonSerializer.Deserialize<JsonElement[]>(json);

        if (arr.Length == 0)
            return Ok(new { valid = false });

        return Ok(new { valid = true });
    }

}
