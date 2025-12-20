using Microsoft.AspNetCore.Identity.Data;
using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Text.Json;
using Transpargo.Helpers;
using Transpargo.Models;
using Transpargo.Services;

[ApiController]
[Route("auth")]
public class AuthController : ControllerBase
{
    private readonly HttpClient _http;
    private readonly string _url;
    private readonly string _key;
    private readonly JwtTokenGenerator _jwt;
    private readonly EmailService _emailService;

    public AuthController(IConfiguration config, EmailService emailService)
    {
        _http = new HttpClient();
        _url = config["SUPABASE_URL"] + "/rest/v1/";
        _key = config["SUPABASE_KEY"];

        _http.DefaultRequestHeaders.Clear();
        _http.DefaultRequestHeaders.TryAddWithoutValidation("apikey", _key);
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"Bearer {_key}");

        _jwt = new JwtTokenGenerator(config);
        _emailService = emailService;
    }

    // -------------------- SIGNUP ----------------------
    [HttpPost("signup")]
    public async Task<IActionResult> Signup([FromBody] SignupRequest req)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }
        string hashedPassword = BCrypt.Net.BCrypt.HashPassword(req.password);

        // -------- 1️⃣ If role is USER — direct create account --------
        if (req.role.ToLower() == "user")
        {
            var newUser = new
            {
                name = req.name,
                email = req.email,
                password = hashedPassword,
                role = "user",
                phone_no = req.phone_no,
                is_active = true
            };

            var body = new StringContent(
                JsonSerializer.Serialize(newUser),
                Encoding.UTF8,
                "application/json"
            );

            var insertResp = await _http.PostAsync(_url + "users", body);

            if (!insertResp.IsSuccessStatusCode)
            {
                var error = await insertResp.Content.ReadAsStringAsync();
                return StatusCode((int)insertResp.StatusCode, new
                {
                    message = "Error creating user",
                    details = error
                });
            }

            return Ok(new
            {
                message = "Signup successful!",
                directLogin = true
            });
        }


        // -------- 2️⃣ If role is admin / shipping — go to pending_users --------
        var pendingUser = new
        {
            name = req.name,
            email = req.email,
            password = hashedPassword,
            requested_role = req.role,
            phone_no = req.phone_no
        };

        var pendingBody = new StringContent(
            JsonSerializer.Serialize(pendingUser),
            Encoding.UTF8,
            "application/json"
        );

        var pendingResp = await _http.PostAsync(_url + "pending_users", pendingBody);

        if (!pendingResp.IsSuccessStatusCode)
        {
            return StatusCode((int)pendingResp.StatusCode,
                await pendingResp.Content.ReadAsStringAsync());
        }

        return Ok(new
        {
            message = "Signup submitted. Waiting for admin approval.",
            directLogin = false
        });
    }

    // -------------------- LOGIN ----------------------
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginReq req)
    {
        var response = await _http.GetAsync($"{_url}users?email=eq.{req.Email}");
        var json = await response.Content.ReadAsStringAsync();

        if (json.StartsWith("<"))
        {
            throw new Exception("Server returned HTML instead of JSON: " + json);
        }

        var users = JsonSerializer.Deserialize<List<UserRecord>>(json);

        if (users == null || users.Count == 0)
            return Unauthorized("User not found.");

        var user = users[0];

        if (!user.is_active)
            return Unauthorized("User not approved.");

        if (!BCrypt.Net.BCrypt.Verify(req.Password, user.password))
            return Unauthorized("Wrong password.");

        string token = _jwt.GenerateToken(user.email, user.role, user.name, user.id.ToString());

        return Ok(new
        {
            token,
            user.id,
            user.name,
            user.role,
            user.email
        });
    }

    [HttpPost("forgotpassword/{email}")]
    public async Task<IActionResult> ForgotPassword(string email)
    {
        //get the user matching with the entered email
        var response = await _http.GetAsync($"{_url}users?email=eq.{email}");
        var json = await response.Content.ReadAsStringAsync();
        Console.WriteLine(json); // See what you actually got

        var users = JsonSerializer.Deserialize<List<UserRecord>>(json);

        if (users == null || users.Count == 0)
            return NotFound(new { message = "User not found" });

        var user = users[0];

        //create a token
        var token = Convert.ToBase64String(Guid.NewGuid().ToByteArray())
                               .Replace("=", "").Replace("+", "").Replace("/", "");

        //save in db
        var resetEntry = new
        {
            email = email,
            token = token,
            expiry = DateTime.UtcNow.AddMinutes(15)
        };

        var body = new StringContent(
            JsonSerializer.Serialize(resetEntry),
            Encoding.UTF8,
            "application/json"
        );

        await _http.PostAsync(_url + "password_reset_tokens", body);

        //setting email and send
        string resetLink = $"http://localhost:5173/resetpassword?token={token}";
        string emailBody = $@"
        <h2>Password Reset</h2>
        <p>Click the link below to reset your password:</p>
        <a href='{resetLink}'>Reset Password</a>
        <p>This link expires in 15 minutes.</p>
    ";

        await _emailService.SendEmailAsync(email, "Reset Password", emailBody);

        return Ok(new { message = "Reset email sent" });
    }

    [HttpPost("resetpassword")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordReq req)
    {
        if (string.IsNullOrEmpty(req.Token) || string.IsNullOrEmpty(req.Password))
        {
            return BadRequest(new { message = "Token and password are required." });
        }

        // Verify token exists and not expired
        var tokenResponse = await _http.GetAsync($"{_url}password_reset_tokens?token=eq.{req.Token}");
        var tokenJson = await tokenResponse.Content.ReadAsStringAsync();

        if (tokenJson.StartsWith("<"))
        {
            throw new Exception("Server returned HTML instead of JSON: " + tokenJson);
        }

        var tokens = JsonSerializer.Deserialize<List<PasswordResetToken>>(tokenJson);

        if (tokens == null || tokens.Count == 0)
        {
            return BadRequest(new { message = "Invalid or expired reset token." });
        }

        var resetToken = tokens[0];

        // Check if token expired
        if (resetToken.expiry < DateTime.UtcNow)
        {
            return BadRequest(new { message = "Reset token has expired." });
        }

        //Hash the new password
        string hashedPassword = BCrypt.Net.BCrypt.HashPassword(req.Password);

        // Update user's password
        var updateUser = new
        {
            password = hashedPassword
        };

        var updateBody = new StringContent(
            JsonSerializer.Serialize(updateUser),
            Encoding.UTF8,
            "application/json"
        );

        var updateResponse = await _http.PatchAsync(
            $"{_url}users?email=eq.{resetToken.email}",
            updateBody
        );

        if (!updateResponse.IsSuccessStatusCode)
        {
            var error = await updateResponse.Content.ReadAsStringAsync();
            return StatusCode((int)updateResponse.StatusCode, new
            {
                message = "Error updating password",
                details = error
            });
        }

        // Delete the used token
        var deleteResponse = await _http.DeleteAsync(
            $"{_url}password_reset_tokens?token=eq.{req.Token}"
        );

        return Ok(new { message = "Password has been reset successfully!" });
    }


}
