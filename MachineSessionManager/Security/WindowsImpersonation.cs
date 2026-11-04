using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;

namespace MachineSessionManager.Security;

public sealed class WindowsImpersonation : IWindowsImpersonation
{
    private const int LogonBatch = 4;

    private const int LogonProviderDefault = 0;

    private readonly ILogger<WindowsImpersonation> _logger;

    public WindowsImpersonation(ILogger<WindowsImpersonation> logger)
    {
        _logger = logger;
    }

    public async Task<T> RunAsync<T>(string userName, string password, Func<Task<T>> action)
    {
        using var caller = WindowsIdentity.GetCurrent();

        _logger.LogInformation(
            "Creating Windows batch logon for {Account} from {Identity}",
            userName,
            caller.Name);
        _logger.LogInformation(
            "App directory: {AppDirectory}; runtime directory: {RuntimeDirectory}; process architecture: {Architecture}",
            AppContext.BaseDirectory,
            RuntimeEnvironment.GetRuntimeDirectory(),
            RuntimeInformation.ProcessArchitecture);

        using var token = LogOn(userName, password);

        // RunImpersonatedAsync carries the identity across await and Task.Run. The runtime
        // duplicates the token for its ExecutionContext, so timed-out native workers retain
        // their identity until they return even after our original handle is disposed.
        return await WindowsIdentity.RunImpersonatedAsync(
            token,
            () =>
            {
                using var identity = WindowsIdentity.GetCurrent();

                _logger.LogInformation("Remote session operations are running as {Identity}", identity.Name);

                return action();
            });
    }

    private static SafeAccessTokenHandle LogOn(string userName, string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);
        if (string.IsNullOrEmpty(password) || password.Contains('\0'))
        {
            throw new ArgumentException("The impersonation password is empty or contains a null character.");
        }

        var separator = userName.IndexOf('\\');
        var account = separator < 0 ? userName : userName[(separator + 1)..];
        // LogonUser requires a null domain for UPNs.
        var domain = separator < 0 ? null : userName[..separator];
        var passwordBuffer = Marshal.StringToHGlobalUni(password);
        try
        {
            // Batch logon validates the password now and retains outbound credentials.
            // The service account needs "Log on as a batch job" on this computer.
            if (!LogonUserW(
                userName: account,
                domain: domain,
                password: passwordBuffer,
                logonType: LogonBatch,
                logonProvider: LogonProviderDefault,
                token: out var token))
            {
                var error = Marshal.GetLastWin32Error();
                token?.Dispose();
                throw new Win32Exception(error);
            }

            if (token.IsInvalid)
            {
                token.Dispose();
                throw new InvalidOperationException("Windows returned an invalid impersonation token.");
            }

            return token;
        }
        finally
        {
            Marshal.ZeroFreeGlobalAllocUnicode(passwordBuffer);
        }
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LogonUserW(
        string userName,
        string? domain,
        IntPtr password,
        int logonType,
        int logonProvider,
        out SafeAccessTokenHandle token);
}
