namespace MachineSessionManager.Sessions;

public enum SessionStatusType
{
    NotSignedIn,
    SignedIn,
    Success,
    AlreadySignedOut,
    Skipped,
    AccessDenied,
    Unreachable,
    NotConfirmed,
    Error,
    TimedOut
}
