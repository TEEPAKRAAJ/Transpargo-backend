using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Text.Json;
using Transpargo.Models;
using Transpargo.Services;

namespace Transpargo.Controllers
{
    [ApiController]
    [Route("api/tariff")]
    [Authorize]
    public class TariffController : ControllerBase
    {
        private readonly HttpClient _http;
        private readonly string _baseUrl;
        private readonly string _apiKey;
        private readonly IConfiguration _config;
        private readonly TradeComplianceService _trade;

        public TariffController(IConfiguration config, TradeComplianceService trade)
        {
            _config = config;
            _baseUrl = config["SUPABASE_URL"] + "/rest/v1/";
            _apiKey = config["SUPABASE_KEY"] ?? throw new InvalidOperationException("SUPABASE_KEY missing");

            _http = new HttpClient();
            _http.DefaultRequestHeaders.Clear();
            _http.DefaultRequestHeaders.Add("apikey", _apiKey);
            _http.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");

            _trade = trade;
        }

        [HttpGet("user-shipments")]
        public async Task<IActionResult> GetUserShipments()
        {
            try
            {
                var userId = User.FindFirst("user_id")?.Value
                          ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                if (string.IsNullOrEmpty(userId))
                    return Unauthorized("User not authenticated");

                string query =
                    $"{_baseUrl}Shipment?"
                    + $"id=eq.{userId}"
                    + "&select=s_id"
                    + "&order=s_id.desc";

                var response = await _http.GetAsync(query);
                var json = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                    return StatusCode((int)response.StatusCode, json);

                var rows = JsonSerializer.Deserialize<List<JsonElement>>(json);

                if (rows == null)
                    return Ok(new List<int>());

                var shipmentIds = rows.Select(r => r.GetProperty("s_id").GetInt32()).ToList();

                return Ok(shipmentIds);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("tariffbyid/{shipmentId}")]
        public async Task<IActionResult> GetTariffById(int shipmentId)
        {
            try
            {
                var userId = User.FindFirst("user_id")?.Value
                      ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                if (string.IsNullOrEmpty(userId))
                    return Unauthorized("User not authenticated");

                string query =
                    $"{_baseUrl}Shipment?"
                    + $"s_id=eq.{shipmentId}&id=eq.{userId}"
                    + "&select=s_id,status,created_at,duty_mode,shipping_cost,"
                    + "Sender_log,Receiver_log,Sender:Sender(*),Receiver:Receiver(*),Product:Product(*)";

                var response = await _http.GetAsync(query);
                var json = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                    return StatusCode((int)response.StatusCode, json);

                var rows = JsonSerializer.Deserialize<List<JsonElement>>(json);

                if (rows == null || rows.Count == 0)
                    return NotFound("Shipment not found");

                var record = rows[0];
                var product = record.GetProperty("Product")[0];
                var receiver = record.GetProperty("Receiver")[0];

                // --- Extract ---
                string dest = receiver.GetProperty("Country").GetString() ?? "";
                string hs = product.TryGetProperty("hs_code", out var h1) ? h1.GetString() ?? "" : "";

                decimal declared = product.TryGetProperty("value", out var v1) ? v1.GetDecimal() : 0;
                decimal weight = product.TryGetProperty("weight", out var w1) ? w1.GetDecimal() : 0;

                // --- Duty + GST only ---
                var (dutyPct, gstPct) = await _trade.ComputeAsync(dest, hs, declared, weight);

                decimal duty = Math.Round((declared * dutyPct / 100), 2);
                decimal gst = Math.Round(((declared + duty) * gstPct / 100), 2);

                decimal totalPayable = duty + gst;

                return Ok(new
                {
                    dutyRate = dutyPct,
                    gstRate = gstPct,
                    duty,
                    gst,
                    totalPayable
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("tariffbyuser")]
        public async Task<IActionResult> GetTariffByUser([FromBody] TariffCalc req)
        {
            try
            {
                var hscode = req.hscode;
                var country = req.country;
                var value = req.value;
                var weight = req.weight;

                var (dutyPct, gstPct) = await _trade.ComputeAsync(country, hscode, value, weight);

                decimal duty = Math.Round((value * dutyPct / 100), 2);
                decimal gst = Math.Round(((value + duty) * gstPct / 100), 2);

                decimal totalPayable = duty + gst;

                return Ok(new
                {
                    dutyRate = dutyPct,
                    gstRate = gstPct,
                    duty,
                    gst,
                    totalPayable
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }
}
