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

    Task<EmailSendResult> SendEventQuotaAsync(
        string email,
        string name,
        string eventTitle,
        DateTimeOffset startAt,
        int availableSeats,
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
        string body = $"Здравствуйте, {name}!\n\n"
            + $"Для входа используйте логин: {login}\n"
            + $"Пароль: {password}\n\n"
            + "Это постоянный пароль для учетной записи.";

        return await SendAsync(email, "Доступ к Капельдинеру", body, cancellationToken);
    }

    public Task<EmailSendResult> SendEventQuotaAsync(
        string email,
        string name,
        string eventTitle,
        DateTimeOffset startAt,
        int availableSeats,
        CancellationToken cancellationToken)
    {
        string eventDate = startAt.ToString("dd.MM.yyyy 'в' HH:mm", CultureInfo.GetCultureInfo("ru-RU"));
        string subjectTitle = eventTitle.Replace('\r', ' ').Replace('\n', ' ');
        string body = $"Здравствуйте, {name}!\n\n"
            + $"Опубликовано новое мероприятие «{eventTitle}», которое состоится {eventDate} (время московское).\n"
            + $"Для вас выделено мест: {availableSeats}.\n\n"
            + "Забронировать места можно на сайте: https://kapeldiner.org\n\n"
            + "С уважением,\nКапельдинер";

        return SendAsync(email, $"Новое мероприятие «{subjectTitle}» — ваша квота", body, cancellationToken);
    }

    private async Task<EmailSendResult> SendAsync(
        string email,
        string subject,
        string body,
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

        string fromName = configuration["Email:Smtp:FromName"] ?? "Капельдинер";
        using var message = new MailMessage
        {
            Subject = subject,
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
