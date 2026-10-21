using System.ComponentModel;
using MachineSessionManager.Models;
using MachineSessionManager.Native;

namespace MachineSessionManager.Sessions;

// Translates native observations/failures into the application's result model.
internal static class SessionResults
{
    internal static SessionStatusType Classify(Exception ex)
    {
        if (ex is Win32Exception error)
        {
            return error.NativeErrorCode switch
            {
                5 => SessionStatusType.AccessDenied,
                53 or 64 or 121 or 1231 or 1722 or 1726 or 1727 or 1753 => SessionStatusType.Unreachable,
                _ => SessionStatusType.Error
            };
        }

        return SessionStatusType.Error;
    }

    internal static SessionResult MakeResult(
        string machine, WindowsSession? session, WindowsSessionUser? user, SessionStatusType status, string details)
    {
        return new SessionResult(machine, session?.Id, user?.AccountName,
            session?.State.ToString() ?? "-", status, details);
    }
}
