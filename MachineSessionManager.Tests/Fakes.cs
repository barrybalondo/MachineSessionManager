using MachineSessionManager.Email;
using MachineSessionManager.Native;

namespace MachineSessionManager.Tests;

internal sealed class FakeWtsServer : IWtsServer
{
    public IReadOnlyList<int> LogoffRequests => _logoffRequests.AsReadOnly();

    private readonly IReadOnlyList<WindowsSession> _sessions;
    private readonly Func<int, WindowsSessionUser> _readUser;
    private readonly Func<int, ushort> _protocol;
    private readonly Action<int> _logoff;
    private readonly Exception? _enumerationError;
    private readonly Action _dispose;
    private readonly List<int> _logoffRequests = [];

    public FakeWtsServer(
        WindowsSession[] sessions,
        Func<int, WindowsSessionUser> readUser,
        Func<int, ushort>? protocol = null,
        Action<int>? logoff = null,
        Exception? enumerationError = null,
        Action? dispose = null)
    {
        _sessions = Array.AsReadOnly(sessions);
        _readUser = readUser;
        _protocol = protocol ?? (_ => 2);
        _logoff = logoff ?? (_ => { });
        _enumerationError = enumerationError;
        _dispose = dispose ?? (() => { });
    }


    public IReadOnlyList<WindowsSession> GetSessions()
    {
        if (_enumerationError is not null)
        {
            throw _enumerationError;
        }

        return _sessions;
    }

    public WindowsSessionUser GetUser(int sessionId)
    {
        return _readUser(sessionId);
    }

    public ushort GetProtocolType(int sessionId)
    {
        return _protocol(sessionId);
    }

    public void Logoff(int sessionId)
    {
        _logoffRequests.Add(sessionId);
        _logoff(sessionId);
    }

    public void Dispose()
    {
        _dispose();
    }
}

internal sealed class FakeWtsServerFactory : IWtsServerFactory
{
    private readonly Func<string, IWtsServer> _open;

    public FakeWtsServerFactory(Func<string, IWtsServer> open)
    {
        _open = open;
    }

    public IWtsServer Open(string machine)
    {
        return _open(machine);
    }
}

internal sealed class FakeEmailSender : IEmailSender
{
    public IReadOnlyList<EmailMessage> Messages => _messages.AsReadOnly();
    public IReadOnlyList<EmailReport> Reports => _messages.Select(message => message.Report).ToArray();

    private readonly bool _fail;
    private readonly Exception? _error;
    private readonly List<EmailMessage> _messages = [];

    public FakeEmailSender(bool fail = false, Exception? error = null)
    {
        _fail = fail;
        _error = error;
    }


    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        if (_error is not null)
        {
            throw _error;
        }

        if (_fail)
        {
            throw new InvalidOperationException("Simulated SMTP failure.");
        }

        _messages.Add(message);
        return Task.CompletedTask;
    }
}
