using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Transpargo.Interfaces;
using Transpargo.Models;
using Transpargo.Services;

[ApiController]
[Route("hs-code")]
[AllowAnonymous] // Or [Authorize(Roles="user")] if you want auth
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
            return BadRequest("Invalid input");

        var result = await _hsCodeService.GetHsCodesAsync(input);

        // 🔹 Debug: Print to console
        Console.WriteLine("---- HS Code Debug ----");
        Console.WriteLine($"Destination Country: {input.DestinationCountry}");
        Console.WriteLine($"Category: {input.Category}");
        Console.WriteLine($"Material: {input.Material}");
        Console.WriteLine($"Product Description: {input.ProductDescription}");
        Console.WriteLine($"Destination HS Code: {result.DestinationHsCode}");
        Console.WriteLine($"Indian HS Code: {result.IndianHsCode}");
        Console.WriteLine("----------------------");

        if (result.DestinationHsCode == "NO HS CODE FOUND")
            return Ok("NO HS CODE FOUND");

        var combinedHs = $"{result.DestinationHsCode},{result.IndianHsCode}";

        return Ok(new
        {
            combined_hs_code = combinedHs,
            destination_hs_code = result.DestinationHsCode,
            indian_hs_code = result.IndianHsCode
        });
    }

}
