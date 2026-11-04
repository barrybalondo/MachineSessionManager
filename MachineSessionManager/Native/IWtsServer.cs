namespace MachineSessionManager.Native;

public interface IWtsServer : IDisposable
{
    IReadOnlyList<WindowsSession> GetSessions();
    WindowsSessionUser GetUser(int sessionId);
    ushort GetProtocolType(int sessionId);
    void Logoff(int sessionId);
}
