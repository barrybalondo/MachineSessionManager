using System.Globalization;
using MachineSessionManager.Email;
using MachineSessionManager.Idira;
using MachineSessionManager.Security;
using MachineSessionManager.Sessions;
using Microsoft.Extensions.Configuration;

namespace MachineSessionManager.Configuration;

public static class SettingsLoader
{
    public static IdiraSettings LoadIdira(IConfiguration configuration)
    {
        return new IdiraSettings(
            RequiredValue(configuration, "Idira:BaseUrl"),
            RequiredValue(configuration, "Idira:AppId"),
            RequiredValue(configuration, "Idira:Safe"));
    }

    public static ImpersonationSettings LoadImpersonation(IConfiguration configuration)
    {
        return new ImpersonationSettings(RequiredValue(configuration, "Impersonation:UserName"));
    }

    public static SessionSettings LoadSession(IConfiguration configuration)
    {
        return new SessionSettings(
            mode: RequiredEnum<SessionModeType>(configuration, "Session:ModeType"),
            userName: RequiredValue(configuration, "Session:UserName"),
            machines: ReadArray(configuration, "Session:Machines"),
            machineTimeout: RequiredDuration(configuration, "Session:MachineTimeout"));
    }

    public static EmailSettings LoadEmail(IConfiguration configuration)
    {
        // Transport details are only required by EmailSender when a message is sent.
        var portText = configuration["Email:Port"];
        int? port = null;
        if (!string.IsNullOrWhiteSpace(portText))
        {
            if (!int.TryParse(portText, out int parsedPort) || parsedPort < 1 || parsedPort > 65535)
            {
                throw new InvalidOperationException("Email:Port must be between 1 and 65535, or null.");
            }

            port = parsedPort;
        }

        var sslText = configuration["Email:UseSsl"];
        var useSsl = false;
        if (sslText is not null && !bool.TryParse(sslText, out useSsl))
        {
            throw new InvalidOperationException("Email:UseSsl must be true or false.");
        }

        return new EmailSettings(configuration["Email:Host"] ?? string.Empty, port, useSsl);
    }

    public static NotificationSettings LoadNotification(IConfiguration configuration)
    {
        return new NotificationSettings(
            notificationMode: RequiredEnum<NotificationModeType>(configuration, "Notification:NotificationModeType"),
            from: configuration["Notification:From"] ?? string.Empty,
            to: ReadArray(configuration, "Notification:To"),
            cc: ReadArray(configuration, "Notification:Cc"),
            bcc: ReadArray(configuration, "Notification:Bcc"));
    }

    private static T RequiredEnum<T>(IConfiguration configuration, string key) where T : struct, Enum
    {
        var value = RequiredValue(configuration, key).Trim();
        // Accept named values only; numeric strings must not enable SignOut accidentally.
        if (!Enum.GetNames<T>().Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"{key} must be one of: {string.Join(", ", Enum.GetNames<T>())}.");
        }

        return Enum.Parse<T>(value, ignoreCase: true);
    }

    private static string RequiredValue(IConfiguration configuration, string key)
    {
        var value = configuration[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Set {key} explicitly in appsettings.json.");
        }

        return value;
    }

    private static TimeSpan RequiredDuration(IConfiguration configuration, string key)
    {
        var value = RequiredValue(configuration, key).Trim();
        if (!TimeSpan.TryParseExact(value, "c", CultureInfo.InvariantCulture, out var duration))
        {
            throw new InvalidOperationException($"{key} must be a duration such as 00:01:00.");
        }

        return duration;
    }

    private static string[] ReadArray(IConfiguration configuration, string key)
    {
        return configuration.GetSection(key).GetChildren()
            .Select(item => item.Value ?? string.Empty)
            .ToArray();
    }
}
