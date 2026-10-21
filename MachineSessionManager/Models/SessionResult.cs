namespace MachineSessionManager.Models;

public sealed class SessionResult
{
    public string Machine { get; }
    public int? SessionId { get; }
    public string? UserName { get; }
    public string State { get; }
    public SessionStatusType Status { get; }
    public string Details { get; }
    public bool IsIssue => Status is SessionStatusType.Skipped
        or SessionStatusType.AccessDenied
        or SessionStatusType.Unreachable
        or SessionStatusType.NotConfirmed
        or SessionStatusType.TimedOut
        or SessionStatusType.Error;
    public bool IsRelevant => Status != SessionStatusType.NotSignedIn;

    public SessionResult(
        string machine,
        int? sessionId,
        string? userName,
        string state,
        SessionStatusType status,
        string details)
    {
        Machine = machine;
        SessionId = sessionId;
        UserName = userName;
        State = state;
        Status = status;
        Details = details;
    }
}
