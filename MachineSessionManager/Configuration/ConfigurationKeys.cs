namespace MachineSessionManager.Configuration;

// These names are the external appsettings/command-line contract.
// Keep them explicit so renaming a model property does not change configuration keys.
internal static class ConfigurationKeys
{
    public static class Session
    {
        public const string Section = "Session";

        public const string ModeType = "ModeType";

        public const string UserName = "UserName";

        public const string Machines = "Machines";

        public const string MachineTimeout = "MachineTimeout";
    }

    public static class Notification
    {
        public const string Section = "Notification";

        public const string NotificationModeType = "NotificationModeType";

        public const string From = "From";

        public const string To = "To";

        public const string Cc = "Cc";

        public const string Bcc = "Bcc";

        public const string FromPath = Section + ":" + From;

        public const string ToPath = Section + ":" + To;
    }

    public static class Email
    {
        public const string Section = "Email";

        public const string Host = "Host";

        public const string Port = "Port";

        public const string UseSsl = "UseSsl";

        public const string HostPath = Section + ":" + Host;

        public const string PortPath = Section + ":" + Port;

        public const string UseSslPath = Section + ":" + UseSsl;
    }
}
