using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Text.Json;
using Transpargo.Models;

[ApiController]
[Route("createshipment")]
[Authorize(Roles = "user")]
public class CreateShipmentController : ControllerBase
{
    private readonly HttpClient _http;
    private readonly string _url;
    private readonly string _key;

    public CreateShipmentController(IConfiguration config)
    {
        _http = new HttpClient();
        _url = config["SUPABASE_URL"] + "/rest/v1/";
        _key = config["SUPABASE_SERVICE_ROLE_KEY"] ?? config["SUPABASE_KEY"];

        _http.DefaultRequestHeaders.Clear();
        _http.DefaultRequestHeaders.TryAddWithoutValidation("apikey", _key);
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"Bearer {_key}");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Prefer", "return=representation");
    }

    public class CreateShipmentModel
    {
        public string status { get; set; }
        public string duty_mode { get; set; }
        public decimal shipping_cost { get; set; }
        public int user_id { get; set; }
        public string reason { get; set; }

        public string sender_name { get; set; }
        public string sender_email { get; set; }
        public string sender_phone { get; set; }
        public string sender_address1 { get; set; }
        public string sender_city { get; set; }
        public string sender_state { get; set; }
        public string sender_postal { get; set; }
        public string sender_country { get; set; }

        public string receiver_name { get; set; }
        public string receiver_email { get; set; }
        public string receiver_phone { get; set; }
        public string receiver_address1 { get; set; }
        public string receiver_city { get; set; }
        public string receiver_state { get; set; }
        public string receiver_postal { get; set; }
        public string receiver_country { get; set; }

        public string shipment_type { get; set; }
        public int packages { get; set; }
        public decimal weight { get; set; }

        public decimal dimensions_length { get; set; }
        public decimal dimensions_width { get; set; }
        public decimal dimensions_height { get; set; }

        public string special_notes { get; set; }
        public int quantity { get; set; }

        public string product_category { get; set; }
        public decimal product_value { get; set; }
        public string product_description { get; set; }
        public string product_composition { get; set; }
        public string intended_use { get; set; }
        public string hs_code { get; set; }
        public string destinationHsCode { get; set; }
    }

    [HttpPost]
    public async Task<IActionResult> CreateShipment([FromBody] CreateShipmentModel model)
    {
        if (model == null)
        {
            Console.WriteLine("❌ MODEL IS NULL - PAYLOAD NOT RECEIVED");
            return BadRequest("Payload not received");
        }

        Console.WriteLine("======================================");
        Console.WriteLine("✅ CREATE SHIPMENT PAYLOAD RECEIVED");
        Console.WriteLine("======================================");

        Console.WriteLine(JsonSerializer.Serialize(
            model,
            new JsonSerializerOptions { WriteIndented = true }
        ));

        // ===================== LOG RECEIVED DATA =====================
        Console.WriteLine("===== RECEIVED SHIPMENT DATA =====");
        Console.WriteLine(JsonSerializer.Serialize(model, new JsonSerializerOptions { WriteIndented = true }));

        // ===================== BASIC VALIDATION =====================
        var missingFields = new List<string>();

        if (string.IsNullOrEmpty(model.sender_name)) missingFields.Add("sender_name");
        if (string.IsNullOrEmpty(model.sender_email)) missingFields.Add("sender_email");
        if (string.IsNullOrEmpty(model.receiver_name)) missingFields.Add("receiver_name");
        if (string.IsNullOrEmpty(model.receiver_email)) missingFields.Add("receiver_email");
        if (string.IsNullOrEmpty(model.shipment_type)) missingFields.Add("shipment_type");
        if (model.packages <= 0) missingFields.Add("packages");
        if (model.weight <= 0) missingFields.Add("weight");

        if (missingFields.Count > 0)
        {
            return BadRequest(new
            {
                message = "Missing or invalid fields",
                fields = missingFields
            });
        }

        var now = DateTime.Now;

        // ===================== LOGS (BACKEND GENERATED) =====================
        var senderLog = new List<LogEntry>
        {
            new LogEntry
            {
                title = "Created Shipment",
                date = now.ToString("yyyy-MM-dd"),
                time = now.ToString("hh:mm tt"),
                icon = "success",
                action = false
            },
            new LogEntry
            {
                title = "HS Validation",
                icon = "pending",
                action = false
            }
        };

        var receiverLog = new List<LogEntry>
        {
            new LogEntry
            {
                title = "Created Shipment",
                date = now.ToString("yyyy-MM-dd"),
                time = now.ToString("hh:mm tt"),
                agent = "User: " + model.sender_name,
                icon = "success",
                action = false
            },
            new LogEntry
            {
                title = "HS Validation",
                icon = "pending",
                action = false
            }
        };

        var additionalDocs = new List<object> { new { } };

        // ===================== INSERT SHIPMENT =====================
        var shipmentPayload = new
        {
            status = model.status,
            duty_mode = model.duty_mode,
            shipping_cost = model.shipping_cost,
            Sender_log = senderLog,
            Receiver_log = receiverLog,
            Additional_docs = additionalDocs,
            id = model.user_id,
            Reason = model.reason ?? ""
        };

        var shipmentResp = await _http.PostAsync(
            _url + "Shipment",
            new StringContent(JsonSerializer.Serialize(shipmentPayload), Encoding.UTF8, "application/json")
        );

        var shipmentJson = await shipmentResp.Content.ReadAsStringAsync();
        Console.WriteLine("Shipment Response: " + shipmentJson);

        if (!shipmentResp.IsSuccessStatusCode)
            return StatusCode(500, shipmentJson);

        int s_id = JsonSerializer.Deserialize<JsonElement[]>(shipmentJson)[0]
            .GetProperty("s_id").GetInt32();

        // ===================== SENDER =====================
        var senderResp = await _http.PostAsync(_url + "Sender",
            new StringContent(JsonSerializer.Serialize(new
            {
                Name = model.sender_name,
                Email = model.sender_email,
                Phone = model.sender_phone,
                Address = model.sender_address1,
                City = model.sender_city,
                State = model.sender_state,
                Postal = model.sender_postal,
                Country = model.sender_country,
                s_id
            }), Encoding.UTF8, "application/json"));

        Console.WriteLine("Sender Response: " + await senderResp.Content.ReadAsStringAsync());

        // ===================== RECEIVER =====================
        var receiverResp = await _http.PostAsync(_url + "Receiver",
            new StringContent(JsonSerializer.Serialize(new
            {
                Name = model.receiver_name,
                Email = model.receiver_email,
                Phone = model.receiver_phone,
                Address = model.receiver_address1,
                City = model.receiver_city,
                State = model.receiver_state,
                postal = model.receiver_postal,
                Country = model.receiver_country,
                s_id
            }), Encoding.UTF8, "application/json"));

        Console.WriteLine("Receiver Response: " + await receiverResp.Content.ReadAsStringAsync());

        // ===================== PRODUCT =====================
        var productResp = await _http.PostAsync(_url + "Product",
            new StringContent(JsonSerializer.Serialize(new
            {
                s_id,
                type = model.shipment_type,
                no_of_packages = model.packages,
                weight = model.weight,
                length = model.dimensions_length,
                width = model.dimensions_width,
                height = model.dimensions_height,
                special_handling = model.special_notes,
                quantity = model.quantity,
                category = model.product_category,
                value = model.product_value,
                description = model.product_description,
                composition = model.product_composition,
                intended_use = model.intended_use,
                hs_code = model.destinationHsCode,
                sender_hs_code = model.hs_code

            }), Encoding.UTF8, "application/json"));

        Console.WriteLine("Product Response: " + await productResp.Content.ReadAsStringAsync());

        return Ok(new { message = "Shipment created successfully", s_id });
    }
}
