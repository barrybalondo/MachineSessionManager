namespace MachineSessionManager.Native;

// Creates an open server connection; the calling method owns and disposes it.
public interface IWtsServerFactory
{
    IWtsServer Open(string machine);
}
