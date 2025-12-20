using Microsoft.AspNetCore.Mvc;
using Transpargo.Models;
using Transpargo.Services;

namespace Transpargo.Controllers
{
    [ApiController]
    [Route("api/airisk")]     // <---- FINAL ROUTE
    public class AiRiskController : ControllerBase
    {
        private readonly AiRiskService _aiRiskService;

        public AiRiskController(AiRiskService aiRiskService)
        {
            _aiRiskService = aiRiskService;
        }

        [HttpPost("analyze")]
        public async Task<ActionResult<RiskAIResponse>> Analyze([FromBody] RiskAIRequest request)
        {
            if (request == null) return BadRequest("Invalid request");
            if (string.IsNullOrWhiteSpace(request.HSCode)) return BadRequest("HS Code required");

            var result = await _aiRiskService.AnalyzeAsync(request);
            return Ok(result);
        }
    }
}
