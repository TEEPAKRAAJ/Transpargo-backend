using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;


namespace Transpargo.Controllers
{
    [ApiController]
    [Route("api/dangerous-goods")]
    [Authorize]

    public class DangerousGoodsController : ControllerBase
    {
        private readonly HttpClient _http;
        private readonly string _baseUrl;
        private readonly string _apiKey;

        public DangerousGoodsController(IConfiguration config)
        {
            _baseUrl = config["SUPABASE_URL"] + "/rest/v1/";
            _apiKey = config["SUPABASE_KEY"];

            _http = new HttpClient();
            _http.DefaultRequestHeaders.Add("apikey", _apiKey);
            _http.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
        }

        // ---------------------------------------------------
        // 1) GET all dangerous products
        // ---------------------------------------------------
        private long GetUserIdFromJwt()
        {
            var userIdClaim = User.FindFirst("user_id");

            if (userIdClaim == null)
                throw new UnauthorizedAccessException("user_id claim missing in token");

            return long.Parse(userIdClaim.Value);
        }

        [HttpGet("products")]
        public async Task<IActionResult> GetDangerousProducts()
        {
            long userId = GetUserIdFromJwt();
            Console.WriteLine($"🔐 JWT userId = {userId}");

            // 1️⃣ Get shipments belonging to this user
            string shipmentQuery =
                $"{_baseUrl}Shipment?"
                + $"id=eq.{userId}"
                + "&select=s_id";

            var shipResp = await _http.GetAsync(shipmentQuery);
            var shipJson = await shipResp.Content.ReadAsStringAsync();

            Console.WriteLine("📦 Shipment raw JSON:");
            Console.WriteLine(shipJson);

            var shipList = JsonSerializer.Deserialize<List<JsonElement>>(shipJson);

            if (shipList == null || shipList.Count == 0)
                return Ok(new List<DangerousProductDto>());

            var shipmentIds = shipList
                .Select(s => s.GetProperty("s_id").GetInt64())
                .ToList();

            // 2️⃣ Get dangerous products for those shipments
            string shipmentIdCsv = string.Join(",", shipmentIds);

            string productQuery =
                $"{_baseUrl}Product?"
                + $"s_id=in.({shipmentIdCsv})"
                + "&category=ilike.*dangerous*"
                + "&select=product_id,category,description,hs_code,s_id";

            var prodResp = await _http.GetAsync(productQuery);
            var prodJson = await prodResp.Content.ReadAsStringAsync();

            Console.WriteLine("📦 Product raw JSON:");
            Console.WriteLine(prodJson);

            if (!prodResp.IsSuccessStatusCode)
                return StatusCode((int)prodResp.StatusCode, prodJson);

            var products = JsonSerializer.Deserialize<List<DangerousProductDto>>(prodJson);
            return Ok(products);
        }



        // ---------------------------------------------------
        // 2) GET details of one dangerous product
        // ---------------------------------------------------
        [HttpGet("details/{productId}")]
        public async Task<IActionResult> GetDangerousGoodsDetails(long productId)
        {
            // ---------------- PRODUCT ----------------
            string productQuery =
                $"{_baseUrl}Product?" +
                $"product_id=eq.{productId}" +
                "&select=*";

            var productResp = await _http.GetAsync(productQuery);
            var productJson = await productResp.Content.ReadAsStringAsync();

            if (!productResp.IsSuccessStatusCode)
                return StatusCode((int)productResp.StatusCode, productJson);

            var productList = JsonSerializer.Deserialize<List<ProductDto>>(productJson);

            if (productList == null || productList.Count == 0)
                return NotFound("Product not found");

            var product = productList[0];

            ShipmentDto shipment = null;
            SenderDto sender = null;
            ReceiverDto receiver = null;

            // ---------------- SHIPMENT ----------------
            if (product.s_id > 0)
            {
                string shipmentQuery =
                    $"{_baseUrl}Shipment?" +
                    $"s_id=eq.{product.s_id}" +
                    "&select=*,Sender:Sender(*),Receiver:Receiver(*)";

                var shipResp = await _http.GetAsync(shipmentQuery);
                var shipJson = await shipResp.Content.ReadAsStringAsync();

                if (shipResp.IsSuccessStatusCode)
                {
                    var shipList = JsonSerializer.Deserialize<List<ShipmentDto>>(shipJson);

                    if (shipList != null && shipList.Count > 0)
                    {
                        shipment = shipList[0];
                        sender = shipment.Sender?.FirstOrDefault();
                        receiver = shipment.Receiver?.FirstOrDefault();
                    }
                }
            }

            // ---------------- FINAL RESPONSE ----------------
            return Ok(new
            {
                product,
                shipment,
                sender,
                receiver
            });
        }
    }

    // ===================================================
    // DTOs
    // ===================================================

    public class DangerousProductDto
    {
        public int product_id { get; set; }
        public string description { get; set; }
        public string hs_code { get; set; }
        public decimal weight { get; set; }
        public decimal value { get; set; }
    }

    public class ProductDto
    {
        public long product_id { get; set; }
        public string description { get; set; }
        public string hs_code { get; set; }

        [JsonPropertyName("s_id")]
        public long s_id { get; set; }
    }

    public class ShipmentDto
    {
        public long s_id { get; set; }
        public List<SenderDto> Sender { get; set; }
        public List<ReceiverDto> Receiver { get; set; }
    }

    public class SenderDto
    {
        public string Name { get; set; }
        public string Email { get; set; }
        public string Phone { get; set; }
    }

    public class ReceiverDto
    {
        public string Name { get; set; }
        public string Email { get; set; }
        public string Phone { get; set; }
    }
}

