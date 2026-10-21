namespace MachineSessionManager.Native;

public sealed class WtsServerFactory : IWtsServerFactory
{
    public IWtsServer Open(string machine)
    {
        return new WtsServer(machine);
    }
}
