namespace MachineSessionManager.Native;

// This boundary also lets tests simulate Windows errors without signing anyone out.
public interface IWtsServer : IDisposable
{
    IReadOnlyList<WindowsSession> GetSessions();
    WindowsSessionUser GetUser(int sessionId);
    ushort GetProtocolType(int sessionId);
    void Logoff(int sessionId);
}
