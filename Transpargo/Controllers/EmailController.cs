using Microsoft.AspNetCore.Mvc;
using Transpargo.Services;

namespace Transpargo.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class EmailController : ControllerBase
    {
        private readonly EmailService _email;

        public EmailController(EmailService email)
        {
            _email = email;
        }

        public class EmailRequest
        {
            public string To { get; set; }
            public string Issue { get; set; }
            public string ShipmentId { get; set; }
        }

        [HttpPost("send")]
        public async Task<IActionResult> SendIssueEmail([FromBody] EmailRequest req)
        {
            var subject = $"Issue related to Shipment {req.ShipmentId}";

            var body = $@"
                <h2>Shipment Issue Report</h2>
                <p><strong>Shipment ID:</strong> {req.ShipmentId}</p>
                <p><strong>Issue:</strong></p>
                <p>{req.Issue}</p>
            ";

            await _email.SendEmailAsync(req.To, subject, body);
            return Ok(new { message = "Email sent successfully" });
        }
    public class ShipmentNotificationRequest
        {
            public string To { get; set; }
            public string ShipmentId { get; set; }
            public string Message { get; set; }   // Full body content
        }

        [HttpPost("notify")]
        public async Task<IActionResult> SendShipmentNotification(
            [FromBody] ShipmentNotificationRequest req)
        {
            if (string.IsNullOrEmpty(req.To) ||
                string.IsNullOrEmpty(req.ShipmentId) ||
                string.IsNullOrEmpty(req.Message))
            {
                return BadRequest("Invalid email request");
            }
            
            var subject = $"Shipment Update - {req.ShipmentId}";
            var safeMessage = System.Net.WebUtility.HtmlEncode(req.Message)
        .Replace("\r\n", "<br />")
        .Replace("\n", "<br />");

            var body = $@"
                <h2>Shipment Status Update</h2>
                <p><strong>Shipment ID:</strong> {req.ShipmentId}</p>
                <hr />
                <p>{safeMessage}</p>
                <br />
                <p>
                    If you have any questions, please contact our support team.
                </p>
                <p><strong>–Team Transpargo</strong></p>
            ";

            await _email.SendEmailAsync(req.To, subject, body);

            return Ok(new { message = "Shipment notification email sent" });
        }
    }
}