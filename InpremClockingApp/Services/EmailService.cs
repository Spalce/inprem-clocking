using System.Net;
using System.Net.Mail;

namespace InpremClockingApp.Services;

public class EmailService
{
    public async Task SendEmail(string email, string subject, string message)
    {
        var client = new SmtpClient("mail.bestoffriendshomecare.com", 465)
        {
            Credentials = new NetworkCredential("no-reply@bestoffriendshomecare.com", "[{W%zbhfZ.*]"),
            EnableSsl = true
        };
        await client.SendMailAsync(new MailMessage("info@pixelsva.com", email, subject, message) { IsBodyHtml = true });
    }
}
