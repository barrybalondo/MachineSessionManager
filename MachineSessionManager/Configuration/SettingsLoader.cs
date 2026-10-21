using System.Globalization;
using MachineSessionManager.Models;
using Microsoft.Extensions.Configuration;

namespace MachineSessionManager.Configuration;

// Configuration is read once at startup, then passed around as immutable objects.
// Explicit parsing prevents a missing enum from silently becoming its zero value.
public static class SettingsLoader
{
    public static SessionSettings LoadSession(IConfiguration configuration)
    {
        var section = configuration.GetRequiredSection("Session");
        return new SessionSettings(
            RequiredEnum<SessionModeType>(section, "ModeType"),
            RequiredValue(section, "UserName"),
            ReadArray(section, "Machines"),
            RequiredDuration(section, "MachineTimeout"));
    }

    public static EmailSettings LoadEmail(IConfiguration configuration)
    {
        // Transport details are only required by EmailSender when a message is sent.
        var section = configuration.GetSection("Email");
        var portText = section["Port"];
        int? port = null;
        if (!string.IsNullOrWhiteSpace(portText))
        {
            if (!int.TryParse(portText, out int parsedPort) || parsedPort < 1 || parsedPort > 65535)
            {
                throw new InvalidOperationException("Email:Port must be between 1 and 65535, or null.");
            }

            port = parsedPort;
        }

        var sslText = section["UseSsl"];
        var useSsl = false;
        if (sslText is not null && !bool.TryParse(sslText, out useSsl))
        {
            throw new InvalidOperationException("Email:UseSsl must be true or false.");
        }

        return new EmailSettings(
            section["SmtpServer"] ?? string.Empty,
            port,
            useSsl,
            section["UserName"]);
    }

    public static NotificationSettings LoadNotification(IConfiguration configuration)
    {
        var section = configuration.GetRequiredSection("Notification");
        return new NotificationSettings(
            RequiredEnum<NotificationModeType>(section, "NotificationModeType"),
            section["From"] ?? string.Empty,
            ReadArray(section, "To"),
            ReadArray(section, "Cc"),
            ReadArray(section, "Bcc"));
    }

    private static T RequiredEnum<T>(IConfigurationSection section, string key) where T : struct, Enum
    {
        var value = RequiredValue(section, key).Trim();
        // Accept named values only; numeric strings must not enable SignOut accidentally.
        if (!Enum.GetNames<T>().Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"{section.Path}:{key} must be one of: {string.Join(", ", Enum.GetNames<T>())}.");
        }

        return Enum.Parse<T>(value, ignoreCase: true);
    }

    private static string RequiredValue(IConfigurationSection section, string key)
    {
        var value = section[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Set {section.Path}:{key} explicitly in appsettings.json.");
        }

        return value;
    }

    private static TimeSpan RequiredDuration(IConfigurationSection section, string key)
    {
        var value = RequiredValue(section, key).Trim();
        if (!TimeSpan.TryParseExact(value, "c", CultureInfo.InvariantCulture, out var duration))
        {
            throw new InvalidOperationException($"{section.Path}:{key} must be a duration such as 00:01:00.");
        }

        return duration;
    }

    private static string[] ReadArray(IConfigurationSection section, string key)
    {
        return section.GetSection(key).GetChildren()
            .Select(item => item.Value ?? string.Empty)
            .ToArray();
    }
}
