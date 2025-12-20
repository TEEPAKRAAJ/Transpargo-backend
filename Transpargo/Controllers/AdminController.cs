using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Text.Json;
using Transpargo.Models;
using Transpargo.DTOs;

[ApiController]
[Route("admin")]
[Authorize(Roles = "admin")]
public class AdminController : ControllerBase
{
    private readonly HttpClient _http;
    private readonly string _url;
    private readonly string _key;

    public AdminController(IConfiguration config)
    {
        _http = new HttpClient();
        _url = config["SUPABASE_URL"] + "/rest/v1/";
        _key = config["SUPABASE_SERVICE_ROLE_KEY"] ?? config["SUPABASE_KEY"];

        _http.DefaultRequestHeaders.Add("apikey", _key);
        _http.DefaultRequestHeaders.Add("Authorization", "Bearer " + _key);
        _http.DefaultRequestHeaders.Add("Prefer", "return=representation");
    }

    // ------------------ GET PENDING USERS ------------------
    [HttpGet("users")]
    public async Task<IActionResult> GetUsers()
    {
        var resp = await _http.GetAsync(_url + "users");
        var json = await resp.Content.ReadAsStringAsync();

        var users = JsonSerializer.Deserialize<List<AdminUserDto>>(
            json,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }
        );

        return Ok(users);
    }

    [HttpGet("pending")]
    public async Task<IActionResult> GetPending()
    {
        var resp = await _http.GetAsync(_url + "pending_users");
        var json = await resp.Content.ReadAsStringAsync();

        var pending = JsonSerializer.Deserialize<List<AdminUserDto>>(
            json,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }
        );

        return Ok(pending);
    }


    // ------------------ APPROVE USER ------------------
    [HttpPost("approve/{id}")]
    public async Task<IActionResult> Approve(int id)
    {
        // 1️⃣ Fetch pending user
        var resp = await _http.GetAsync($"{_url}pending_users?id=eq.{id}");
        var json = await resp.Content.ReadAsStringAsync();

        var pendingUser = JsonSerializer.Deserialize<List<PendingUser>>(json)?.FirstOrDefault();
        if (pendingUser == null)
            return NotFound(new { message = "Pending user not found" });

        // 2️⃣ Prepare insert data for users table
        var newUser = new
        {
            name = pendingUser.name,
            email = pendingUser.email,
            password = pendingUser.password,
            role = pendingUser.requested_role,
            phone_no = pendingUser.phone_no,
            is_active = true
        };

        var body = new StringContent(
            JsonSerializer.Serialize(newUser),
            Encoding.UTF8,
            "application/json"
        );

        // Insert into users
        var insertResp = await _http.PostAsync(_url + "users", body);

        if (!insertResp.IsSuccessStatusCode)
        {
            var error = await insertResp.Content.ReadAsStringAsync();
            return StatusCode((int)insertResp.StatusCode, new
            {
                message = "Error inserting into users table",
                details = error
            });
        }

        // 3️⃣ Delete from pending_users
        var deleteResp = await _http.DeleteAsync($"{_url}pending_users?id=eq.{id}");

        if (!deleteResp.IsSuccessStatusCode)
        {
            var err = await deleteResp.Content.ReadAsStringAsync();
            return StatusCode((int)deleteResp.StatusCode, new
            {
                message = "User inserted but failed to remove from pending_users",
                details = err
            });
        }

        return Ok(new { message = "User approved successfully" });
    }

    // ------------------ REJECT USER ------------------
    [HttpDelete("reject/{id}")]
    public async Task<IActionResult> Reject(int id)
    {
        var resp = await _http.DeleteAsync($"{_url}pending_users?id=eq.{id}");
        return resp.IsSuccessStatusCode
            ? Ok(new { message = "User rejected" })
            : StatusCode((int)resp.StatusCode, await resp.Content.ReadAsStringAsync());
    }

    // ------------------ DELETE USER ------------------
    [HttpDelete("delete/{email}")]
    public async Task<IActionResult> DeleteUser(string email)
    {
        var resp = await _http.DeleteAsync($"{_url}users?email=eq.{email}");
        return resp.IsSuccessStatusCode
            ? Ok(new { message = "User deleted" })
            : StatusCode((int)resp.StatusCode, await resp.Content.ReadAsStringAsync());
    }

}
