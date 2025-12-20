using System.Net;
using System.Net.Mail;

namespace Transpargo.Services
{
    public class EmailService
    {
        private readonly IConfiguration _config;

        public EmailService(IConfiguration config)
        {
            _config = config;
        }

        public async Task SendEmailAsync(string to, string subject, string htmlBody)
        {
            var fromEmail = _config["EmailSettings:From"];
            var appPassword = _config["EmailSettings:AppPassword"];  // Gmail App Password

            using (var client = new SmtpClient("smtp.gmail.com", 587))
            {
                client.Credentials = new NetworkCredential(fromEmail, appPassword);
                client.EnableSsl = true;

                var mail = new MailMessage()
                {
                    From = new MailAddress(fromEmail),
                    Subject = subject,
                    Body = htmlBody,
                    IsBodyHtml = true
                };

                mail.To.Add(to);

                await client.SendMailAsync(mail);
            }
        }
    }
}
