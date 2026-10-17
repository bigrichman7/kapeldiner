using KapeldinerService.Web.Auth;
using Microsoft.Extensions.Configuration;

namespace KapeldinerService.Tests.Api;

public sealed class UserEmailSenderTests
{
    [Fact]
    public async Task MissingSmtpConfigurationIsReportedWithoutAttemptingDelivery()
    {
        var sender = new SmtpUserEmailSender(new ConfigurationBuilder().Build());

        EmailSendResult result = await sender.SendCredentialsAsync(
            "user@example.com",
            "Test User",
            "test-user",
            "a-generated-password",
            CancellationToken.None);

        Assert.Equal(EmailSendResult.NotConfigured, result);
    }
}
