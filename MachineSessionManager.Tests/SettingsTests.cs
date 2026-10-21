using MachineSessionManager.Configuration;
using MachineSessionManager.Email;
using MachineSessionManager.Sessions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace MachineSessionManager.Tests;

public sealed class SettingsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("Automatic")]
    public void MissingOrInvalidSessionModeIsRejected(string? mode)
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Session:ModeType"] = mode,
            ["Session:UserName"] = "DOMAIN\\alice",
            ["Session:Machines:0"] = "VM"
        });

        Assert.Throws<InvalidOperationException>(() => SettingsLoader.LoadSession(configuration));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("Sometimes")]
    public void MissingOrInvalidNotificationModeIsRejected(string? notificationMode)
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Notification:NotificationModeType"] = notificationMode,
            ["Notification:From"] = "sender@example.com"
        });

        Assert.Throws<InvalidOperationException>(() => SettingsLoader.LoadNotification(configuration));
    }

    [Fact]
    public void ExplicitModesAreReadAndMachineNamesAreNormalized()
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Session:ModeType"] = "sIgNoUt",
            ["Session:MachineTimeout"] = "00:01:00",
            ["Session:UserName"] = " DOMAIN\\alice ",
            ["Session:Machines:0"] = " VM ",
            ["Session:Machines:1"] = "vm",
            ["Notification:NotificationModeType"] = "Always"
        });

        var session = SettingsLoader.LoadSession(configuration);
        var notification = SettingsLoader.LoadNotification(configuration);

        Assert.Equal(SessionModeType.SignOut, session.ModeType);
        Assert.Equal(TimeSpan.FromMinutes(1), session.MachineTimeout);
        Assert.Equal("DOMAIN\\alice", session.UserName);
        Assert.Equal("VM", Assert.Single(session.Machines));
        Assert.Equal(NotificationModeType.Always, notification.NotificationModeType);
    }

    [Fact]
    public void SettingsDoNotExposeOrRetainMutableInputCollections()
    {
        string[] machines = ["VM"];
        string[] recipients = ["alice@example.com"];
        var session = new SessionSettings(SessionModeType.Check, "alice", machines, TimeSpan.FromSeconds(30));
        var notification = new NotificationSettings(NotificationModeType.Never, "", recipients, [], []);

        machines[0] = "CHANGED";
        recipients[0] = "changed@example.com";

        Assert.Equal("VM", Assert.Single(session.Machines));
        Assert.Equal("alice@example.com", Assert.Single(notification.To));
        Assert.Throws<NotSupportedException>(() => ((IList<string>)session.Machines)[0] = "CHANGED");
        Assert.Throws<NotSupportedException>(() => ((IList<string>)notification.To)[0] = "CHANGED");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("invalid")]
    public void MissingOrMalformedMachineTimeoutIsRejected(string? timeout)
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Session:ModeType"] = "Check",
            ["Session:UserName"] = "alice",
            ["Session:Machines:0"] = "VM",
            ["Session:MachineTimeout"] = timeout
        });

        var error = Assert.Throws<InvalidOperationException>(() => SettingsLoader.LoadSession(configuration));

        Assert.Contains("Session:MachineTimeout", error.Message);
    }

    [Theory]
    [InlineData("00:00:00")]
    [InlineData("-00:00:01")]
    [InlineData("50.00:00:00")]
    public void OutOfRangeMachineTimeoutIsRejected(string timeout)
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Session:ModeType"] = "Check",
            ["Session:UserName"] = "alice",
            ["Session:Machines:0"] = "VM",
            ["Session:MachineTimeout"] = timeout
        });

        Assert.Throws<ArgumentOutOfRangeException>(() => SettingsLoader.LoadSession(configuration));
    }

    [Fact]
    public void SmtpSettingsLoadIndependentlyOfNotificationPolicy()
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Email:Host"] = "smtp.example.com",
            ["Email:Port"] = "587",
            ["Email:UseSsl"] = "true"
        });

        var email = SettingsLoader.LoadEmail(configuration);

        Assert.Equal("smtp.example.com", email.Host);
        Assert.Equal(587, email.Port);
        Assert.True(email.UseSsl);
    }

    [Fact]
    public void NeverCanBeConfiguredWithoutSmtpDetails()
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Notification:NotificationModeType"] = "Never"
        });

        var notification = SettingsLoader.LoadNotification(configuration);
        var email = SettingsLoader.LoadEmail(configuration);

        Assert.Equal(NotificationModeType.Never, notification.NotificationModeType);
        Assert.Empty(notification.To);
        Assert.Empty(email.Host);
    }

    [Fact]
    public void LegacyEmailWhenCannotReplaceRequiredNotificationMode()
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Email:When"] = "Always",
            ["Notification:From"] = "sender@example.com"
        });

        var error = Assert.Throws<InvalidOperationException>(() => SettingsLoader.LoadNotification(configuration));

        Assert.Contains("Notification:NotificationModeType", error.Message);
    }

    [Fact]
    public void AddressesAreReadFromTheNotificationSection()
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Notification:NotificationModeType"] = "Relevant",
            ["Notification:From"] = "sender@example.com",
            ["Notification:To:0"] = "alice@example.com",
            ["Notification:Cc:0"] = "copy@example.com",
            ["Notification:Bcc:0"] = "blind@example.com",
            ["Email:To:0"] = "old-address@example.com"
        });

        var notification = SettingsLoader.LoadNotification(configuration);

        Assert.Equal("sender@example.com", notification.From);
        Assert.Equal("alice@example.com", Assert.Single(notification.To));
        Assert.Equal("copy@example.com", Assert.Single(notification.Cc));
        Assert.Equal("blind@example.com", Assert.Single(notification.Bcc));
    }

    [Fact]
    public void EmailMessagesOwnReadOnlyCopiesOfTheirAddresses()
    {
        string[] recipients = ["alice@example.com"];
        var message = new EmailMessage("sender@example.com", recipients, [], [], new EmailReport("Subject", "HTML", "Text"));

        recipients[0] = "changed@example.com";

        Assert.Equal("alice@example.com", Assert.Single(message.To));
        Assert.Throws<NotSupportedException>(() => ((IList<string>)message.To)[0] = "CHANGED");
    }

    [Fact]
    public void MissingMachineListIsRejected()
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Session:ModeType"] = "Check",
            ["Session:MachineTimeout"] = "00:01:00",
            ["Session:UserName"] = "alice"
        });

        Assert.Throws<ArgumentException>(() => SettingsLoader.LoadSession(configuration));
    }

    private static IConfiguration Configuration(Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
}
