using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Linq;
using System.Text;
using System.Text.Json;
using Transpargo.Models;
using Transpargo.Services;

[ApiController]
[Route("analytics")]
[Authorize(Roles = "Shipping_agency")]
public class AnalyticsController : ControllerBase
{
    private readonly HttpClient _http;
    private readonly string _url;
    private readonly string _key;
    private readonly INimService _nimService;

    public AnalyticsController(IConfiguration config, INimService nimService)
    {
        _http = new HttpClient();
        _url = config["SUPABASE_URL"] + "/rest/v1/";
        _key = config["SUPABASE_SERVICE_ROLE_KEY"] ?? config["SUPABASE_KEY"];

        _http.DefaultRequestHeaders.Add("apikey", _key);
        _http.DefaultRequestHeaders.Add("Authorization", "Bearer " + _key);
        _http.DefaultRequestHeaders.Add("Prefer", "return=representation");

        _nimService = nimService;
    }

    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary()
    {
        try
        {
            // Fetch Supabase Data
            var shipmentsResponse = await _http.GetAsync(_url + "Shipment");
            shipmentsResponse.EnsureSuccessStatusCode();
            var shipmentsJson = await shipmentsResponse.Content.ReadAsStringAsync();

            // Deserialize shipments normally (for other analytics)
            var shipments = JsonSerializer.Deserialize<List<Shipment>>(shipmentsJson);

            // Deserialize AGAIN as raw JSON (to read "status" which is missing in the model)
            var shipmentsRaw = JsonSerializer.Deserialize<List<JsonElement>>(shipmentsJson);

            var receiversResponse = await _http.GetAsync(_url + "Receiver");
            receiversResponse.EnsureSuccessStatusCode();
            var receiversJson = await receiversResponse.Content.ReadAsStringAsync();
            var receivers = JsonSerializer.Deserialize<List<Receiver>>(receiversJson);

            // Handle empty shipments
            if (shipments == null || !shipments.Any())
            {
                return Ok(new AnalyticsSummary
                {
                    TotalShipments = 0,
                    InProcess = 0,
                    ClearanceSucessRate = 0,
                    AvgDutyPaid = 0,
                    AbortedRate = 0,
                    ShipmentsOverMonths = new Dictionary<string, int>(),
                    ShipmentsPerCountry = new Dictionary<string, int>(),
                    StatusDistribution = new Dictionary<string, int>(),
                    AiSummary = "No shipments available for analysis"
                });
            }

            // Analytics Calculations
            var total = shipments.Count;

            var cleared = shipments.Count(s =>
                s.Sender_Log != null &&
                s.Sender_Log.Any() &&
                s.Sender_Log.Last().title == "Delivered" &&
                s.Sender_Log.Last().icon == "success");

            var clearanceRate = total > 0 ? (double)cleared / total * 100 : 0;
            var totalCost = shipments.Sum(s => s.ShippingCost);
            var avgCost = total > 0 ? (double)totalCost / total : 0;

            var shipmentsOverMonths = shipments
                .GroupBy(s => new { s.created_at.Year, s.created_at.Month })
                .OrderBy(g => g.Key.Year)
                .ThenBy(g => g.Key.Month)
                .ToDictionary(
                    g => new DateTime(g.Key.Year, g.Key.Month, 1).ToString("MMM-yyyy"),
                    g => g.Count()
                );

            // NEW: Shipment Status Distribution from RAW JSON
            // NEW: Shipment Status Distribution from RAW JSON
            var statusDistribution = new Dictionary<string, int>
            {
                { "Delivered", 0 },
                { "Returned", 0 },
                { "Destroyed", 0 },
                { "Aborted" , 0}
            };

            // Count Delivered / Returned / Destroyed
            foreach (var rawShip in shipmentsRaw)
            {
                string status = "";

                if (rawShip.TryGetProperty("status", out var statusProp))
                    status = statusProp.GetString() ?? "";

                status = status.Trim();

                if (statusDistribution.ContainsKey(status))
                    statusDistribution[status]++;
            }

            // AFTER counting — now calculate In-Process
            int delivered = statusDistribution["Delivered"];
            int returned = statusDistribution["Returned"];
            int destroyed = statusDistribution["Destroyed"];
            int aborted = statusDistribution["Aborted"];

            int inProcess = total - (delivered + returned + destroyed + aborted);

            // Add In-Process for the pie chart
            statusDistribution["In-Process"] = inProcess;

            // Shipments per country
            var shipmentsPerCountry = new Dictionary<string, int>();
            if (receivers != null && receivers.Any())
            {
                shipmentsPerCountry = shipments
                    .Join(receivers,
                        s => s.SId,
                        r => r.ShipmentId,
                        (s, r) => r.Country)
                    .GroupBy(country => country)
                    .ToDictionary(g => g.Key ?? "Unknown", g => g.Count());
            }

            var abortedRate = total > 0 ? (double)statusDistribution["Aborted"] / total * 100 : 0;

            Console.WriteLine("Aborted:");
            Console.WriteLine(abortedRate);
            // Create summary model
            var summary = new AnalyticsSummary
            {
                TotalShipments = total,
                InProcess = inProcess,           // NEW
                ClearanceSucessRate = Math.Round(clearanceRate, 2),
                AvgDutyPaid = Math.Round(avgCost, 2),
                AbortedRate = Math.Round(abortedRate, 2),
                ShipmentsOverMonths = shipmentsOverMonths,
                ShipmentsPerCountry = shipmentsPerCountry,
                StatusDistribution = statusDistribution
            };


            var summaryJson = JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true });

            // AI Summary
            string prompt = $"What measures can the shipping agency take based on this data? 50 words:\n{summaryJson}";
            var aiResponse = await _nimService.AskAsync(prompt);

            summary.AiSummary = aiResponse;

            return Ok(summary);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message, stack = ex.StackTrace });
        }
    }
}
