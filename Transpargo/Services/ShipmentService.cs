using System.Text.Json;

namespace Transpargo.Services
{
    public class ShipmentService
    {
        private readonly HttpClient _http;
        private readonly string _url;

        public ShipmentService(IConfiguration config)
        {
            _http = new HttpClient();
            _url = config["SUPABASE_URL"] + "/rest/v1/";

            _http.DefaultRequestHeaders.TryAddWithoutValidation("apikey", config["SUPABASE_SERVICE_ROLE_KEY"]);
            _http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"Bearer {config["SUPABASE_SERVICE_ROLE_KEY"]}");
        }

        public async Task<object?> GetShipmentDetails(string shipmentId, string userId)
        {
            var resp = await _http.GetAsync(_url + $"Shipments?ShipmentId=eq.{shipmentId}&UserId=eq.{userId}");
            var json = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode || json == "[]")
                return null;

            return JsonSerializer.Deserialize<List<object>>(json)!.FirstOrDefault();
        }
    }
}
