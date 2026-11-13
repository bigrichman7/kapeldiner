using KapeldinerService.Web.Tickets;
using QuestPDF.Infrastructure;

namespace KapeldinerService.Tests.Tickets;

public sealed class EventTicketPdfGeneratorTests
{
    [Fact]
    public void GeneratedTicketPdfContainsTheTicketVerificationQrCode()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var generator = new EventTicketPdfGenerator();
        byte[] pdf = generator.Generate(
        [
            new EventTicketData(
                "Проверка QR-кода",
                DateTime.UtcNow.AddDays(1),
                "1",
                1,
                new Uri("https://example.com/ticket/verification-token"))
        ]);

        Assert.StartsWith(
            "%PDF-",
            System.Text.Encoding.ASCII.GetString(pdf, 0, 5),
            StringComparison.Ordinal);
    }
}
