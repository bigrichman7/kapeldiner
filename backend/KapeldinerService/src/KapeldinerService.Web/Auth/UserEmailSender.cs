using System.Globalization;
using System.Net;
using System.Net.Mail;
using System.Text;

namespace KapeldinerService.Web.Auth;

public interface IUserEmailSender
{
    Task<EmailSendResult> SendCredentialsAsync(
        string email,
        string name,
        string login,
        string password,
        CancellationToken cancellationToken);
}

public enum EmailSendResult
{
    Sent,
    NotConfigured,
    Failed
}

public sealed class SmtpUserEmailSender(IConfiguration configuration) : IUserEmailSender
{
    public async Task<EmailSendResult> SendCredentialsAsync(
        string email,
        string name,
        string login,
        string password,
        CancellationToken cancellationToken)
    {
        string? host = configuration["Email:Smtp:Host"];
        string? fromAddress = configuration["Email:Smtp:FromAddress"];
        string? userName = configuration["Email:Smtp:UserName"];
        string? passwordSetting = configuration["Email:Smtp:Password"];
        if (string.IsNullOrWhiteSpace(host)
            || string.IsNullOrWhiteSpace(fromAddress)
            || string.IsNullOrWhiteSpace(configuration["Email:Smtp:Port"])
            || (string.IsNullOrWhiteSpace(userName) != string.IsNullOrWhiteSpace(passwordSetting)))
        {
            return EmailSendResult.NotConfigured;
        }

        if (!int.TryParse(configuration["Email:Smtp:Port"], NumberStyles.None, CultureInfo.InvariantCulture, out int port)
            || port is < 1 or > 65535)
        {
            return EmailSendResult.NotConfigured;
        }

        string fromName = configuration["Email:Smtp:FromName"] ?? "Kapeldiner";
        string body = $"Здравствуйте, {name}!\n\n"
            + $"Для входа используйте логин: {login}\n"
            + $"Пароль: {password}\n\n"
            + "Это постоянный пароль для учетной записи.";

        using var message = new MailMessage
        {
            Subject = "Доступ к Kapeldiner",
            Body = body,
            BodyEncoding = Encoding.UTF8,
            SubjectEncoding = Encoding.UTF8
        };
        try
        {
            message.From = new MailAddress(fromAddress, fromName);
            message.To.Add(new MailAddress(email));
        }
        catch (FormatException)
        {
            return EmailSendResult.NotConfigured;
        }

        using var client = new SmtpClient(host, port)
        {
            EnableSsl = true,
            UseDefaultCredentials = string.IsNullOrWhiteSpace(userName)
        };
        if (!string.IsNullOrWhiteSpace(userName))
        {
            client.Credentials = new NetworkCredential(userName, passwordSetting);
        }

        try
        {
            await client.SendMailAsync(message, cancellationToken);
            return EmailSendResult.Sent;
        }
        catch (SmtpException)
        {
            return EmailSendResult.Failed;
        }
    }
}
