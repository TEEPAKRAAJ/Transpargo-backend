using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Transpargo.Models;

namespace Transpargo.Controllers
{
    [ApiController]
    [Route("api/profile")]
    [Authorize]
    public class ProfileController : ControllerBase
    {
        private readonly HttpClient _http;
        private readonly string _baseUrl;
        private readonly string _apiKey;

        public ProfileController(IConfiguration config)
        {
            _baseUrl = config["SUPABASE_URL"] + "/rest/v1/";
            _apiKey = config["SUPABASE_KEY"]
                ?? throw new InvalidOperationException("SUPABASE_KEY missing");

            _http = new HttpClient();
            _http.DefaultRequestHeaders.Clear();
            _http.DefaultRequestHeaders.Add("apikey", _apiKey);
            _http.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
            _http.DefaultRequestHeaders.Add("Accept", "application/json");
        }

        // ======================================================
        // GET PROFILE
        // ======================================================
        [HttpGet("{userId:long}")]
        public async Task<IActionResult> GetProfile(long userId)
        {
            var tokenUserId =
                User.FindFirst("user_id")?.Value ??
                User.FindFirst(ClaimTypes.NameIdentifier)?.Value ??
                User.FindFirst("sub")?.Value;

            if (!long.TryParse(tokenUserId, out var jwtId) || jwtId != userId)
                return Forbid("User ID mismatch");

            var response = await _http.GetAsync(
                $"{_baseUrl}users?id=eq.{userId}&select=id,name,email,phone_no"
            );

            var json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                return StatusCode((int)response.StatusCode, json);

            var rows = JsonSerializer.Deserialize<JsonElement[]>(json);

            if (rows == null || rows.Length == 0)
                return NotFound("User not found");

            var u = rows[0];

            return Ok(new
            {
                id = u.GetProperty("id").GetInt64(),
                name = u.GetProperty("name").GetString() ?? "",
                email = u.GetProperty("email").GetString() ?? "",
                phone = u.TryGetProperty("phone_no", out var p) ? p.GetString() : ""
            });
        }

        // ======================================================
        // UPDATE PROFILE
        // ======================================================
        [HttpPut("{userId:long}")]
        public async Task<IActionResult> UpdateProfile(
            long userId,
            [FromBody] UpdateProfileModel model)
        {
            var tokenUserId =
                User.FindFirst("user_id")?.Value ??
                User.FindFirst(ClaimTypes.NameIdentifier)?.Value ??
                User.FindFirst("sub")?.Value;

            if (!long.TryParse(tokenUserId, out var jwtId) || jwtId != userId)
                return Forbid("User ID mismatch");

            var payload = new
            {
                name = model.name,
                email = model.email,
                phone_no = model.phone
            };

            var req = new HttpRequestMessage(
                HttpMethod.Patch,
                $"{_baseUrl}users?id=eq.{userId}"
            )
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(payload),
                    Encoding.UTF8,
                    "application/json"
                )
            };

            var response = await _http.SendAsync(req);
            var json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                return StatusCode((int)response.StatusCode, json);

            return Ok(new { message = "Profile updated successfully" });
        }

        // ======================================================
        // CHANGE PASSWORD
        // ======================================================
        [HttpPut("change-password/{userId:long}")]
        [Authorize]
        public async Task<IActionResult> ChangePassword(long userId, [FromBody] ChangePasswordReq req)
        {
            if (string.IsNullOrEmpty(req.CurrentPassword) || string.IsNullOrEmpty(req.NewPassword))
            {
                return BadRequest(new { message = "Both passwords are required" });
            }

            // Verify logged-in user matches route userId
            var tokenUserId = User.FindFirst("user_id")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!long.TryParse(tokenUserId, out var jwtId) || jwtId != userId)
                return Forbid("User ID mismatch");

            // Fetch user from Supabase
            var userResp = await _http.GetAsync($"{_baseUrl}users?id=eq.{userId}");
            var userJson = await userResp.Content.ReadAsStringAsync();
            var users = JsonSerializer.Deserialize<List<UserRecord>>(userJson);

            if (users == null || users.Count == 0)
                return Unauthorized("User not found");

            var user = users[0];

            // Verify current password
            if (!BCrypt.Net.BCrypt.Verify(req.CurrentPassword, user.password))
                return Unauthorized(new { message = "Current password is incorrect" });

            // Hash new password
            var updatePayload = new
            {
                password = BCrypt.Net.BCrypt.HashPassword(req.NewPassword)
            };

            var updateResp = await _http.PatchAsync(
                $"{_baseUrl}users?id=eq.{userId}",
                new StringContent(
                    JsonSerializer.Serialize(updatePayload),
                    Encoding.UTF8,
                    "application/json"
                )
            );

            if (!updateResp.IsSuccessStatusCode)
                return StatusCode((int)updateResp.StatusCode, await updateResp.Content.ReadAsStringAsync());

            return Ok(new { message = "Password updated successfully" });
        }

    }// ======================================================
     // DTOs
     // ======================================================
    public class UpdateProfileModel
    {
        public string name { get; set; }
        public string email { get; set; }
        public string phone { get; set; }
    }

    public class ChangePasswordReq
    {
        public string CurrentPassword { get; set; }
        public string NewPassword { get; set; }
    }
}

