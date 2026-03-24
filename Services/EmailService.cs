using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using Microsoft.Extensions.Configuration;
using System.IO;
using System.Threading.Tasks;

namespace ParkingGarageAPI.Services
{
    public interface IEmailService
    {
        Task<bool> SendEmailAsync(string to, string subject, string body, string attachmentPath = null);
        Task<bool> SendInvoiceEmailAsync(string to, string subject, string body, string invoicePdfPath);
    }
    
    public class EmailService : IEmailService
    {
        private readonly IConfiguration _configuration;
        
        public EmailService(IConfiguration configuration)
        {
            _configuration = configuration;
        }
        
        public async Task<bool> SendEmailAsync(string to, string subject, string body, string attachmentPath = null)
        {
            try
            {
                var email = new MimeMessage();
                
                // Feladó
                email.From.Add(MailboxAddress.Parse(_configuration["EmailSettings:From"]));
                
                // Címzett
                email.To.Add(MailboxAddress.Parse(to));
                
                // Tárgy
                email.Subject = subject;
                
                // Tartalom
                var builder = new BodyBuilder();
                builder.HtmlBody = body;
                
                // Csatolmány hozzáadása, ha van
                if (!string.IsNullOrEmpty(attachmentPath) && File.Exists(attachmentPath))
                {
                    builder.Attachments.Add(attachmentPath);
                }
                
                email.Body = builder.ToMessageBody();
                
                // Kapcsolódás az SMTP szerverhez
                using var smtp = new SmtpClient();
                await smtp.ConnectAsync(
                    _configuration["EmailSettings:SmtpServer"], 
                    int.Parse(_configuration["EmailSettings:Port"]), 
                    SecureSocketOptions.StartTls
                );
                
                // Hitelesítés
                await smtp.AuthenticateAsync(
                    _configuration["EmailSettings:Username"], 
                    _configuration["EmailSettings:Password"]
                );
                
                // Email küldése
                await smtp.SendAsync(email);
                
                // Kapcsolat bontása
                await smtp.DisconnectAsync(true);
                
                return true;
            }
            catch (Exception ex)
            {
                // Naplózás
                Console.WriteLine($"Hiba történt az email küldése közben: {ex.Message}");
                return false;
            }
        }
        
        public async Task<bool> SendInvoiceEmailAsync(string to, string subject, string body, string invoicePdfPath)
        {
            // Számlához kötelező a PDF csatolmány; ha hiányzik, ne küldjünk félkész emailt.
            if (string.IsNullOrWhiteSpace(invoicePdfPath) || !File.Exists(invoicePdfPath))
            {
                Console.WriteLine($"Számla email küldés megszakítva: hiányzó PDF csatolmány. Path: {invoicePdfPath}");
                return false;
            }

            return await SendEmailAsync(to, subject, body, invoicePdfPath);
        }
    }
} 