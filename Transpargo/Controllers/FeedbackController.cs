using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Text.Json;

[ApiController]
[Route("feedback")]
public class FeedbackController : ControllerBase
{
    private readonly HttpClient _http;
    private readonly string _url;
    private readonly string _key;

    public FeedbackController(IConfiguration config)
    {
        _http = new HttpClient();
        _url = config["SUPABASE_URL"] + "/rest/v1/";
        _key = config["SUPABASE_KEY"];

        _http.DefaultRequestHeaders.Clear();
        _http.DefaultRequestHeaders.TryAddWithoutValidation("apikey", _key);
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"Bearer {_key}");
        _http.DefaultRequestHeaders.TryAddWithoutValidation(
            "Prefer",
            "return=minimal"
        );
    }

    // ================= MODELS =================

    public class FeedbackInput
    {
        public long shipment_id { get; set; }
        public int rating { get; set; }
        public string message { get; set; }
    }

    public class FeedbackOutput
    {
        public int Id { get; set; }
        public int Shipment_id { get; set; }
        public string Type { get; set; }
        public string Message { get; set; }
        public int Rating { get; set; }
        public DateTime Created_at { get; set; }
    }

    // ================= POST =================

    [HttpPost]
    public async Task<IActionResult> Submit([FromBody] FeedbackInput model)
    {
        if (model == null)
            return BadRequest("Invalid payload");

        if (model.rating < 1 || model.rating > 5)
            return BadRequest("Rating must be between 1 and 5");

        // 🔍 CHECK IF FEEDBACK ALREADY EXISTS
        var checkResponse = await _http.GetAsync(
            _url + $"feedback_complaints?shipment_id=eq.{model.shipment_id}&select=id"
        );

        if (!checkResponse.IsSuccessStatusCode)
            return StatusCode(500, "Failed to validate existing feedback");

        var existing = await checkResponse.Content.ReadAsStringAsync();

        if (existing != "[]")
        {
            return BadRequest("Feedback already provided for this shipment");
        }

        // 🔁 DETERMINE TYPE
        string type =
            model.rating <= 2 ? "Complaint" :
            model.rating == 3 ? "Neutral" :
            "Feedback";

        var payload = new
        {
            shipment_id = model.shipment_id,
            rating = model.rating,
            message = model.message,
            type = type
        };

        // 📥 INSERT
        var response = await _http.PostAsync(
            _url + "feedback_complaints",
            new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json"
            )
        );

        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync();
            return StatusCode(500, err);
        }

        return Ok(new { message = "Feedback submitted successfully" });
    }


    // ================= GET =================


    [HttpGet("veiwfeedback")]
    public async Task<IActionResult> getfeedback()
    {
        try
        {
            var response = await _http.GetAsync(_url + "feedback_complaints");
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine("Error in retreiving the data from DB");
            }

            var feedbacks = await response.Content.ReadAsStringAsync();
            var rows = JsonSerializer.Deserialize<List<FeedbackOutput>>(feedbacks,
                     new JsonSerializerOptions
                     {
                         PropertyNameCaseInsensitive = true
                     }

                );
            int total_rate = 0;
            double avg_rate = 0.0;
            foreach (var row in rows)
            {
                total_rate += row.Rating;
            }

            avg_rate = (double)total_rate / rows.Count;
            if (rows != null && rows.Count > 0)
            {
                return Ok(new
                {
                    feedback = rows,
                    avg = avg_rate
                });
            }
        }
        catch (Exception e)
        {
            Console.WriteLine("Something went wrng and a exception was thrown: " + e);
        }
        return Ok(new
        {
            feedback = new List<FeedbackOutput>(),
            avg = 0
        });

    }

}
