using Transpargo.Interfaces;
using Transpargo.Models;
using Transpargo.Services;

public class HsCodeService : IHsCodeService
{
    private readonly HttpClient _http;
    private readonly INimService _deepSeek;
    private readonly IConfiguration _config;

    public HsCodeService(
        IHttpClientFactory factory,
        INimService deepSeek,
        IConfiguration config)
    {
        _http = factory.CreateClient();
        _deepSeek = deepSeek;
        _config = config;
    }

    public async Task<HsCodeResult> GetHsCodesAsync(HsCodeInput input)
    {
        // 1️⃣ Fetch destination HS candidates from Supabase
        var supabaseUrl = _config["SUPABASE_URL"] + "/rest/v1/Hscode_data";
        var apiKey = _config["SUPABASE_KEY"];

        var query =
            $"?Country=ilike.{input.DestinationCountry}" +
            $"&Category=ilike.{input.Category}";

        var request = new HttpRequestMessage(HttpMethod.Get, supabaseUrl + query);
        request.Headers.Add("apikey", apiKey);
        request.Headers.Add("Authorization", $"Bearer {apiKey}");

        var response = await _http.SendAsync(request);
        var json = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode || json == "[]")
            return NoHsFound();

        // 2️⃣ Ask DeepSeek to find BEST destination HS code
        var prompt = $@"
You are an HS Code expert.

User Product Description:
{input.ProductDescription}

Available HS Code Records:
{json}

Choose ONLY the best matching HS Code (Destination Country).
Return ONLY the HS Code number.
If no good match exists, return NO_MATCH.
";

        var destinationHs = await _deepSeek.AskAsync(prompt);
        Console.WriteLine(json);
        if (destinationHs.Contains("NO_MATCH"))
            return NoHsFound();

        // 3️⃣ Convert destination HS → Indian HS (8 digit)
        var indiaPrompt = $@"
Convert this HS Code to Indian HS Code.
Destination HS Code: {destinationHs}
Return ONLY the Indian HS Code.
Return ONLY the HS Code number.
";

        var indianHs = await _deepSeek.AskAsync(indiaPrompt);

        return new HsCodeResult
        {
            DestinationHsCode = destinationHs.Trim(),
            IndianHsCode = indianHs.Trim()
        };
    }

    private HsCodeResult NoHsFound() => new()
    {
        IndianHsCode = "NO HS CODE FOUND",
        DestinationHsCode = "NO HS CODE FOUND"
    };
}
