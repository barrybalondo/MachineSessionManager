namespace MachineSessionManager.Console.Security;

public sealed class ImpersonationSettings
{
    public string UserName { get; }

    public ImpersonationSettings(string userName)
    {
        UserName = userName;
    }
}
