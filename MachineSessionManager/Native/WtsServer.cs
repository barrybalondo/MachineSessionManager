using System.ComponentModel;
using System.Runtime.InteropServices;

namespace MachineSessionManager.Native;

// API reference: https://learn.microsoft.com/windows/win32/api/wtsapi32/
internal sealed class WtsServer : IWtsServer
{
    private const string WtsLibraryName = "wtsapi32.dll";

    private static readonly IntPtr InvalidHandleValue = new IntPtr(-1);

    private readonly IntPtr _handle;

    private readonly string _machine;

    public WtsServer(string machine)
    {
        _machine = machine;

        _handle = WTSOpenServerW(machine);

        // Zero means the local server in WTS; reject it for an explicitly named server.
        // Enumeration must still validate the handle because failure has no fixed value.
        if (_handle == IntPtr.Zero || _handle == InvalidHandleValue)
        {
            // WTSOpenServer does not define GetLastError for this failure.
            throw new InvalidOperationException($"Windows returned an invalid server handle for {_machine}.");
        }
    }

    public IReadOnlyList<WindowsSession> GetSessions()
    {
        // WTS requires reserved = 0 and version = 1.
        if (!WTSEnumerateSessionsW(
            server: _handle,
            reserved: 0,
            version: 1,
            sessions: out var buffer,
            count: out var count))
        {
            throw CreateLastWindowsException($"Enumerating sessions on {_machine}");
        }

        var sessions = new List<WindowsSession>();
        try
        {
            // Native padding and pointer size differ between x86 and x64.
            var recordSize = Marshal.SizeOf<WtsSessionInfo>();
            for (var i = 0; i < count; i++)
            {
                var entry = IntPtr.Add(buffer, i * recordSize);
                var info = Marshal.PtrToStructure<WtsSessionInfo>(entry);

                // Copy the UTF-16 text before the enumeration buffer is freed.
                var stationName = Marshal.PtrToStringUni(info.StationName) ?? string.Empty;
                sessions.Add(new WindowsSession(info.SessionId, stationName, info.State));
            }
        }
        finally
        {
            // WTS allocations must be released with WTSFreeMemory.
            if (buffer != IntPtr.Zero)
            {
                WTSFreeMemory(buffer);
            }
        }

        // Query accounts separately so one failed lookup does not discard the snapshot.
        return sessions.AsReadOnly();
    }

    public WindowsSessionUser GetUser(int sessionId)
    {
        var userName = ReadString(sessionId, WtsInfoClassType.UserName);
        if (string.IsNullOrWhiteSpace(userName))
        {
            // Service and listener sessions may have no signed-in user.
            return new WindowsSessionUser(string.Empty, string.Empty);
        }

        var domain = ReadString(sessionId, WtsInfoClassType.DomainName);
        return new WindowsSessionUser(userName, domain);
    }

    public ushort GetProtocolType(int sessionId)
    {
        // WTSClientProtocolType returns a USHORT: 0 = console, 1 = legacy, 2 = RDP.
        if (!WTSQuerySessionInformationW(
            server: _handle,
            sessionId: sessionId,
            infoClass: WtsInfoClassType.ClientProtocolType,
            buffer: out var buffer,
            bytes: out var byteCount))
        {
            throw CreateLastWindowsException($"Reading protocol for session {sessionId} on {_machine}");
        }

        try
        {
            if (buffer == IntPtr.Zero || byteCount < sizeof(ushort))
            {
                throw new InvalidOperationException($"Windows returned no protocol for session {sessionId}.");
            }

            // Preserve the unsigned USHORT bits returned through the signed reader.
            return unchecked((ushort)Marshal.ReadInt16(buffer));
        }
        finally
        {
            if (buffer != IntPtr.Zero)
            {
                WTSFreeMemory(buffer);
            }
        }
    }

    public void Logoff(int sessionId)
    {
        // false submits the request; SessionSignOut verifies completion separately.
        if (!WTSLogoffSession(_handle, sessionId, wait: false))
        {
            throw CreateLastWindowsException($"Logging off session {sessionId} on {_machine}");
        }
    }

    public void Dispose()
    {
        WTSCloseServer(_handle);
    }

    private string ReadString(int sessionId, WtsInfoClassType infoClass)
    {
        // WTS strings are null-terminated UTF-16; the byte count is not needed here.
        if (!WTSQuerySessionInformationW(
            server: _handle,
            sessionId: sessionId,
            infoClass: infoClass,
            buffer: out var buffer,
            bytes: out _))
        {
            throw CreateLastWindowsException($"Reading {infoClass} for session {sessionId} on {_machine}");
        }

        try
        {
            return buffer == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUni(buffer) ?? string.Empty;
        }
        finally
        {
            if (buffer != IntPtr.Zero)
            {
                WTSFreeMemory(buffer);
            }
        }
    }

    private static Win32Exception CreateLastWindowsException(string operation)
    {
        // Capture the cached native error before another interop call can replace it.
        var code = Marshal.GetLastPInvokeError();
        return new Win32Exception(code, $"{operation}: {new Win32Exception(code).Message} (Windows error {code}).");
    }

    // Match WTS_SESSION_INFOW's native alignment; Pack = 1 breaks x64 pointer alignment.
    [StructLayout(LayoutKind.Sequential)]
    private struct WtsSessionInfo
    {
        public int SessionId;

        public IntPtr StationName;

        public WtsConnectionStateType State;
    }

    // Values are fixed by WTS_INFO_CLASS and must not be renumbered.
    // https://learn.microsoft.com/windows/win32/api/wtsapi32/ne-wtsapi32-wts_info_class
    private enum WtsInfoClassType
    {
        UserName = 5,
        DomainName = 7,
        ClientProtocolType = 16
    }

    [DllImport(
        WtsLibraryName,
        EntryPoint = "WTSOpenServerW",
        CharSet = CharSet.Unicode,
        ExactSpelling = true,
        SetLastError = true)]
    private static extern IntPtr WTSOpenServerW(string machine);

    // Native BOOL is four bytes.
    [DllImport(WtsLibraryName, EntryPoint = "WTSEnumerateSessionsW", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSEnumerateSessionsW(
        IntPtr server,
        int reserved,
        int version,
        out IntPtr sessions,
        out int count);

    [DllImport(WtsLibraryName, EntryPoint = "WTSQuerySessionInformationW", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSQuerySessionInformationW(
        IntPtr server,
        int sessionId,
        WtsInfoClassType infoClass,
        out IntPtr buffer,
        out int bytes);

    [DllImport(WtsLibraryName, EntryPoint = "WTSLogoffSession", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSLogoffSession(
        IntPtr server,
        int sessionId,
        [MarshalAs(UnmanagedType.Bool)] bool wait);

    [DllImport(WtsLibraryName, EntryPoint = "WTSFreeMemory", ExactSpelling = true)]
    private static extern void WTSFreeMemory(IntPtr memory);

    [DllImport(WtsLibraryName, EntryPoint = "WTSCloseServer", ExactSpelling = true)]
    private static extern void WTSCloseServer(IntPtr server);
}
