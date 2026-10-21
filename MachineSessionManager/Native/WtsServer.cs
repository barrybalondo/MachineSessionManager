using System.ComponentModel;
using System.Runtime.InteropServices;

namespace MachineSessionManager.Native;

// Adapts Windows Terminal Services (WTS) to C#; it contains no email or account-selection rules.
// WTS is the Windows API used to inspect and manage Remote Desktop sessions.
// API reference: https://learn.microsoft.com/windows/win32/api/wtsapi32/
internal sealed class WtsServer : IWtsServer
{
    // Windows commonly uses INVALID_HANDLE_VALUE, the pointer-sized value -1, as an
    // invalid-handle sentinel. new IntPtr(-1) represents all bits set on both x86/x64.
    // IntPtr is an address-sized value; it does not allocate an object in Windows.
    private static readonly IntPtr InvalidHandleValue = new IntPtr(-1);
    private readonly IntPtr _handle;
    private readonly string _machine;

    public WtsServer(string machine)
    {
        _machine = machine;

        // 1. Ask Windows for an opaque server handle. Treat it as an OS-issued token;
        //    never dereference it as a memory address or invent your own handle value.
        //    The W suffix selects the Unicode (UTF-16) entry point; A would mean ANSI.
        _handle = WTSOpenServerW(machine);

        // 2. Reject the common invalid values defensively. Although zero is a special
        //    "current server" handle in WTS, this wrapper explicitly opens a named server
        //    and must not silently operate on the local machine instead.
        //    WTSOpenServer does not guarantee that every invalid handle is zero/-1;
        //    the subsequent enumeration call is also required to validate usability.
        if (_handle == IntPtr.Zero || _handle == InvalidHandleValue)
        {
            // WTSOpenServer does not document a last-error code for this failure.
            // Do not present a potentially stale code as a meaningful Windows error.
            throw new InvalidOperationException($"Windows returned an invalid server handle for {_machine}.");
        }
    }

    public IReadOnlyList<WindowsSession> GetSessions()
    {
        // 1. Request session metadata. Windows requires reserved=0 and version=1.
        //    `out` lets the native call return both an allocated array address and its
        //    record count. A false result means no usable array: capture the error now.
        if (!WTSEnumerateSessionsW(_handle, 0, 1, out var buffer, out var count))
        {
            throw CreateLastWindowsException($"Enumerating sessions on {_machine}");
        }

        var sessions = new List<WindowsSession>();
        try
        {
            // 2. Obtain the native record size, including platform-specific padding.
            //    A pointer occupies 4 bytes in a 32-bit process and 8 in a 64-bit one,
            //    so hardcoding the total record size would be unsafe.
            var recordSize = Marshal.SizeOf<WtsSessionInfo>();
            for (var i = 0; i < count; i++)
            {
                // 3. Walk the array by address: start + (record index * record size).
                //    PtrToStructure copies one native record into a managed struct.
                var entry = IntPtr.Add(buffer, i * recordSize);
                var info = Marshal.PtrToStructure<WtsSessionInfo>(entry);

                // 4. StationName is itself a pointer, not a C# string. Copy its UTF-16
                //    text while the enumeration buffer is still alive. The resulting
                //    managed string remains valid after Windows' buffer is freed.
                var stationName = Marshal.PtrToStringUni(info.StationName) ?? string.Empty;
                sessions.Add(new WindowsSession(info.SessionId, stationName, info.State));
            }
        }
        finally
        {
            // 5. Windows allocated this memory, so Windows must release it. The GC
            //    cannot free it, and Marshal.FreeHGlobal is not the matching allocator.
            //    `finally` also runs when reading/copying a record throws an exception.
            if (buffer != IntPtr.Zero)
            {
                WTSFreeMemory(buffer);
            }
        }

        // 6. Return only managed data. Account queries happen separately so one failed
        //    session query does not discard the other sessions in this snapshot.
        return sessions.AsReadOnly();
    }

    public WindowsSessionUser GetUser(int sessionId)
    {
        // These are two specific WTS queries for one session ID, not a new enumeration.
        var userName = ReadString(sessionId, WtsInfoClassType.UserName);
        if (string.IsNullOrWhiteSpace(userName))
        {
            // Services/listeners may have no signed-in user; no domain lookup is needed.
            return new WindowsSessionUser(string.Empty, string.Empty);
        }

        var domain = ReadString(sessionId, WtsInfoClassType.DomainName);
        return new WindowsSessionUser(userName, domain);
    }

    public ushort GetProtocolType(int sessionId)
    {
        // 1. Selector 16 requests protocol information. Its returned VALUE is different:
        //    0 = console, 1 = legacy protocol, 2 = RDP. Windows returns a USHORT (2 bytes),
        //    so this buffer must be read as a number, not a null-terminated string.
        if (!WTSQuerySessionInformationW(_handle, sessionId, WtsInfoClassType.ClientProtocolType,
                out var buffer, out var byteCount))
        {
            throw CreateLastWindowsException($"Reading protocol for session {sessionId} on {_machine}");
        }

        try
        {
            // 2. Check the pointer/length before reading native memory.
            if (buffer == IntPtr.Zero || byteCount < sizeof(ushort))
            {
                throw new InvalidOperationException($"Windows returned no protocol for session {sessionId}.");
            }

            // 3. Marshal exposes a signed 16-bit reader. Reinterpret its bits as the
            //    unsigned 16-bit type documented by Windows; unchecked avoids overflow.
            return unchecked((ushort)Marshal.ReadInt16(buffer));
        }
        finally
        {
            // 4. Each successful query owns a separate allocation, even for 2 bytes.
            if (buffer != IntPtr.Zero)
            {
                WTSFreeMemory(buffer);
            }
        }
    }

    public void Logoff(int sessionId)
    {
        // false means "submit the request and return", not "the session has ended".
        // SessionSignOut checks eligibility/ownership before calling this method and
        // verifies completion afterward. This native wrapper only submits the request.
        if (!WTSLogoffSession(_handle, sessionId, false))
        {
            throw CreateLastWindowsException($"Logging off session {sessionId} on {_machine}");
        }
    }

    public void Dispose()
    {
        // The server handle and query buffers have different lifetimes/cleanup APIs.
        // A caller's `using IWtsServer ...` closes this handle when finished, including
        // when a query throws; buffers are freed earlier by their own finally blocks.
        WTSCloseServer(_handle);
    }

    private string ReadString(int sessionId, WtsInfoClassType infoClass)
    {
        // 1. The enum selects WHICH field to read. It is passed to Windows as an int.
        //    Windows fills `buffer` with a pointer to a null-terminated UTF-16 string.
        //    The final out parameter is the byte count; a null terminator ends this string,
        //    so the string conversion below does not need the count (hence `out _`).
        if (!WTSQuerySessionInformationW(_handle, sessionId, infoClass, out var buffer, out _))
        {
            throw CreateLastWindowsException($"Reading {infoClass} for session {sessionId} on {_machine}");
        }

        try
        {
            // 2. Copy the native string into GC-managed memory before freeing its buffer.
            return buffer == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUni(buffer) ?? string.Empty;
        }
        finally
        {
            // 3. Free the allocation even if conversion failed.
            if (buffer != IntPtr.Zero)
            {
                WTSFreeMemory(buffer);
            }
        }
    }

    private static Win32Exception CreateLastWindowsException(string operation)
    {
        // Native functions usually report failure through false/an invalid handle.
        // They do not automatically throw C# exceptions. SetLastError=true below asks
        // .NET to cache the native error code, which must be read immediately on failure.
        // This helper returns an exception; `throw CreateLastWindowsException(...)`
        // at the call site throws it. The code/message/context are retained for reporting.
        var code = Marshal.GetLastPInvokeError();
        return new Win32Exception(code,
            $"{operation}: {new Win32Exception(code).Message} (Windows error {code}).");
    }

    // Sequential means field order and normal native alignment match WTS_SESSION_INFOW.
    // Do not add Pack=1: that would remove padding expected by Windows on 64-bit systems.
    // The native fields are DWORD (32 bits), LPWSTR (pointer-sized), and an enum (32 bits).
    // Fields are appropriate here because this struct describes unmanaged memory.
    // The private containing type keeps these public fields hidden outside this wrapper.
    [StructLayout(LayoutKind.Sequential)]
    private struct WtsSessionInfo
    {
        public int SessionId;
        public IntPtr StationName;
        public WtsConnectionStateType State;
    }

    // This is a small subset of Windows' WTS_INFO_CLASS, not values chosen by this app.
    // The full Windows enum begins at zero: WTSUserName is entry 5, WTSDomainName is 7,
    // and WTSClientProtocolType is 16. Omitting unused members makes explicit numbers
    // essential; renumbering them would request entirely different fields from Windows.
    // https://learn.microsoft.com/windows/win32/api/wtsapi32/ne-wtsapi32-wts_info_class
    // Nesting/private visibility is an ordinary C# encapsulation choice, not a Windows
    // requirement: only this wrapper needs these native query selectors.
    private enum WtsInfoClassType
    {
        UserName = 5,
        DomainName = 7,
        ClientProtocolType = 16
    }

    // DllImport is the C# declaration of a function implemented in a native Windows DLL,
    // not in a .NET assembly/NuGet package. `extern` means it has no C# method body.
    // Calling these methods crosses the managed/native boundary (called P/Invoke).
    // EntryPoint names the exact exported function. ExactSpelling prevents suffix probing.
    // CharSet.Unicode converts the machine string into the UTF-16 form expected by W.
    // SetLastError preserves the Windows error code for CreateLastWindowsException.
    // https://learn.microsoft.com/dotnet/standard/native-interop/pinvoke
    [DllImport("wtsapi32.dll", EntryPoint = "WTSOpenServerW",
        CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr WTSOpenServerW(string machine);

    // Native BOOL is a 4-byte value, so explicitly marshal it as UnmanagedType.Bool.
    // `out` matches native pointer-to-output parameters; Windows supplies their values.
    [DllImport("wtsapi32.dll", EntryPoint = "WTSEnumerateSessionsW", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSEnumerateSessionsW(
        IntPtr server, int reserved, int version, out IntPtr sessions, out int count);

    [DllImport("wtsapi32.dll", EntryPoint = "WTSQuerySessionInformationW", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSQuerySessionInformationW(
        IntPtr server, int sessionId, WtsInfoClassType infoClass, out IntPtr buffer, out int bytes);

    [DllImport("wtsapi32.dll", EntryPoint = "WTSLogoffSession", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSLogoffSession(
        IntPtr server, int sessionId, [MarshalAs(UnmanagedType.Bool)] bool wait);

    // Match the release function to the resource: query buffer vs server handle.
    [DllImport("wtsapi32.dll", EntryPoint = "WTSFreeMemory", ExactSpelling = true)]
    private static extern void WTSFreeMemory(IntPtr memory);

    [DllImport("wtsapi32.dll", EntryPoint = "WTSCloseServer", ExactSpelling = true)]
    private static extern void WTSCloseServer(IntPtr server);
}
