using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Transpargo.Models; // <- Make sure HsCodeInput model is here
using Transpargo.Services;
using Transpargo.Interfaces;

[ApiController]
[Route("hs-code")]
[AllowAnonymous]

public class HsCodeController : ControllerBase
{
    private readonly IHsCodeService _hsCodeService;

    public HsCodeController(IHsCodeService hsCodeService)
    {
        _hsCodeService = hsCodeService;
    }

    [HttpPost("fetch")]
    public async Task<IActionResult> FetchHsCode([FromBody] HsCodeInput input)
    {
        if (input == null)
            return BadRequest(new { message = "Invalid HS Code input" });

        try
        {
            string hsCode = await _hsCodeService.GetHsCodeAsync(input);

            return Ok(new { hsCode });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Failed to fetch HS code", details = ex.Message });
        }
    }
}
