using System.Globalization;
using System.Reflection;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using QRCoder;

namespace KapeldinerService.Web.Tickets;

public sealed record EventTicketData(
    string EventTitle,
    DateTime StartAtUtc,
    string RowLabel,
    int SeatNumber,
    Uri VerificationUrl);

public interface IEventTicketPdfGenerator
{
    byte[] Generate(IReadOnlyList<EventTicketData> tickets);
}

public sealed class EventTicketPdfGenerator : IEventTicketPdfGenerator
{
    private static readonly CultureInfo RussianCulture = CultureInfo.GetCultureInfo("ru-RU");
    private static readonly TimeZoneInfo MoscowTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow");
    private static readonly byte[] Logo = LoadLogo();

    public byte[] Generate(IReadOnlyList<EventTicketData> tickets)
    {
        ArgumentNullException.ThrowIfNull(tickets);
        if (tickets.Count == 0)
            throw new ArgumentException("At least one ticket is required.", nameof(tickets));

        return Document.Create(document =>
        {
            foreach (EventTicketData[] pageTickets in tickets.Chunk(5))
            {
                document.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(24);
                    page.DefaultTextStyle(style => style.FontFamily("Lato").FontColor(Colors.Black));
                    page.Content().Column(column =>
                    {
                        column.Spacing(8);
                        foreach (EventTicketData ticket in pageTickets)
                            column.Item().Height(145).Element(container => ComposeTicket(container, ticket));
                    });
                });
            }
        }).GeneratePdf();
    }

    private static void ComposeTicket(IContainer container, EventTicketData ticket)
    {
        DateTimeOffset eventTime = TimeZoneInfo.ConvertTime(
            new DateTimeOffset(DateTime.SpecifyKind(ticket.StartAtUtc, DateTimeKind.Utc)),
            MoscowTimeZone);
        string eventDate = eventTime.ToString("dd MMMM yyyy 'года'", RussianCulture);
        string eventClock = eventTime.ToString("HH:mm", RussianCulture);
        using QRCodeGenerator qrGenerator = new();
        using QRCodeData qrCodeData = qrGenerator.CreateQrCode(ticket.VerificationUrl.AbsoluteUri, QRCodeGenerator.ECCLevel.Q);
        using PngByteQRCode qrCode = new(qrCodeData);
        byte[] qrCodeImage = qrCode.GetGraphic(6);

        container
            .Border(1)
            .BorderColor(Colors.Grey.Lighten1)
            .Row(row =>
            {
                row.ConstantItem(83)
                    .Padding(8)
                    .AlignCenter()
                    .AlignMiddle()
                    .Image(Logo)
                    .FitArea();

                row.RelativeItem()
                    .PaddingVertical(8)
                    .PaddingHorizontal(6)
                    .Column(column =>
                    {
                        column.Spacing(2);
                        column.Item().AlignCenter().Text("Пригласительный билет").FontSize(13).Bold();
                        column.Item().AlignCenter().Text(ticket.EventTitle).FontSize(18).Bold().Italic();
                        column.Item().AlignCenter().Text($"Ряд {ticket.RowLabel}    Место {ticket.SeatNumber}")
                            .FontSize(16).Bold().Italic();
                        column.Item().AlignCenter().Text(eventDate).FontSize(12).Bold();
                        column.Item().AlignCenter().Text(eventClock).FontSize(12).Bold();
                        column.Item().PaddingTop(4).AlignCenter().Text(
                            "КЦ ГУ МВД России по г. Санкт-Петербургу и Ленинградской области\n"
                            + "(ул. Харьковская, 9, м. «Площадь Восстания», «Площадь Александра Невского»)")
                            .FontSize(8)
                            .AlignCenter();
                    });

                row.ConstantItem(105)
                    .BorderLeft(1)
                    .BorderColor(Colors.Grey.Darken1)
                    .Padding(6)
                    .Column(column =>
                    {
                        column.Spacing(4);
                        column.Item().AlignCenter().Text("КОНТРОЛЬ").FontSize(11).Bold();
                        column.Item()
                            .AlignCenter()
                            .Width(44)
                            .Height(44)
                            .Image(qrCodeImage)
                            .FitArea();
                        column.Item().AlignCenter().Text("М.П.").FontSize(11).Bold();
                    });
            });
    }

    private static byte[] LoadLogo()
    {
        using Stream stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("KapeldinerService.Web.ticket-logo.jpg")
            ?? throw new InvalidOperationException("The embedded ticket logo is missing.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
