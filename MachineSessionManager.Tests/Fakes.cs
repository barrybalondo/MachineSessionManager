using MachineSessionManager.Email;
using MachineSessionManager.Idira;
using MachineSessionManager.Native;
using MachineSessionManager.Security;

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

    private readonly Action? _onSend;

    private readonly List<EmailMessage> _messages = [];

    public FakeEmailSender(bool fail = false, Exception? error = null, Action? onSend = null)
    {
        _fail = fail;
        _error = error;
        _onSend = onSend;
    }

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        _onSend?.Invoke();

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

internal sealed class FakeIdiraCredentialProvider : IIdiraCredentialProvider
{
    public const string Password = "test-password";

    public int CallCount { get; private set; }

    public string? RequestedUserName { get; private set; }

    private readonly Exception? _error;

    private readonly Action? _onGet;

    public FakeIdiraCredentialProvider(Exception? error = null, Action? onGet = null)
    {
        _error = error;
        _onGet = onGet;
    }

    public Task<string> GetPasswordAsync(string userName, CancellationToken cancellationToken = default)
    {
        CallCount++;
        RequestedUserName = userName;
        _onGet?.Invoke();

        return _error is null ? Task.FromResult(Password) : Task.FromException<string>(_error);
    }
}

internal sealed class FakeWindowsImpersonation : IWindowsImpersonation
{
    public int CallCount { get; private set; }

    public string? CurrentUserName => _currentUserName.Value;

    public string? ReceivedPassword { get; private set; }

    private readonly AsyncLocal<string?> _currentUserName = new();

    private readonly Exception? _error;

    public FakeWindowsImpersonation(Exception? error = null)
    {
        _error = error;
    }

    public async Task<T> RunAsync<T>(string userName, string password, Func<Task<T>> action)
    {
        CallCount++;
        ReceivedPassword = password;

        if (_error is not null)
        {
            throw _error;
        }

        var previous = _currentUserName.Value;
        _currentUserName.Value = userName;

        try
        {
            return await action();
        }
        finally
        {
            _currentUserName.Value = previous;
        }
    }
}
