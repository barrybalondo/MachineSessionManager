using System.Globalization;
using MachineSessionManager.Email;
using MachineSessionManager.Sessions;
using Microsoft.Extensions.Configuration;

namespace MachineSessionManager.Configuration;

// Configuration is read once at startup, then passed around as immutable objects.
// Explicit parsing prevents a missing enum from silently becoming its zero value.
public static class SettingsLoader
{
    public static SessionSettings LoadSession(IConfiguration configuration)
    {
        var section = configuration.GetRequiredSection(ConfigurationKeys.Session.Section);
        return new SessionSettings(
            RequiredEnum<SessionModeType>(section, ConfigurationKeys.Session.ModeType),
            RequiredValue(section, ConfigurationKeys.Session.UserName),
            ReadArray(section, ConfigurationKeys.Session.Machines),
            RequiredDuration(section, ConfigurationKeys.Session.MachineTimeout));
    }

    public static EmailSettings LoadEmail(IConfiguration configuration)
    {
        // Transport details are only required by EmailSender when a message is sent.
        var section = configuration.GetSection(ConfigurationKeys.Email.Section);
        var portText = section[ConfigurationKeys.Email.Port];
        int? port = null;
        if (!string.IsNullOrWhiteSpace(portText))
        {
            if (!int.TryParse(portText, out int parsedPort) || parsedPort < 1 || parsedPort > 65535)
            {
                throw new InvalidOperationException(
                    $"{ConfigurationKeys.Email.PortPath} must be between 1 and 65535, or null.");
            }

            port = parsedPort;
        }

        var sslText = section[ConfigurationKeys.Email.UseSsl];
        var useSsl = false;
        if (sslText is not null && !bool.TryParse(sslText, out useSsl))
        {
            throw new InvalidOperationException($"{ConfigurationKeys.Email.UseSslPath} must be true or false.");
        }

        return new EmailSettings(
            section[ConfigurationKeys.Email.Host] ?? string.Empty,
            port,
            useSsl);
    }

    public static NotificationSettings LoadNotification(IConfiguration configuration)
    {
        var section = configuration.GetRequiredSection(ConfigurationKeys.Notification.Section);
        return new NotificationSettings(
            RequiredEnum<NotificationModeType>(section, ConfigurationKeys.Notification.NotificationModeType),
            section[ConfigurationKeys.Notification.From] ?? string.Empty,
            ReadArray(section, ConfigurationKeys.Notification.To),
            ReadArray(section, ConfigurationKeys.Notification.Cc),
            ReadArray(section, ConfigurationKeys.Notification.Bcc));
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
