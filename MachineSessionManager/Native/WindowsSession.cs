namespace MachineSessionManager.Native;

public sealed class WindowsSession
{
    public int Id { get; }
    public string StationName { get; }
    public WtsConnectionStateType State { get; }

    public WindowsSession(int id, string stationName, WtsConnectionStateType state)
    {
        Id = id;
        StationName = stationName;
        State = state;
    }
}

public sealed class WindowsSessionUser
{
    public string UserName { get; }
    public string Domain { get; }
    public string AccountName => string.IsNullOrEmpty(Domain) ? UserName : $"{Domain}\\{UserName}";

    public WindowsSessionUser(string userName, string domain)
    {
        UserName = userName;
        Domain = domain;
    }
}

// Numeric values are defined by the Windows WTS_CONNECTSTATE_CLASS enumeration.
public enum WtsConnectionStateType
{
    Active = 0,
    Connected = 1,
    ConnectQuery = 2,
    Shadow = 3,
    Disconnected = 4,
    Idle = 5,
    Listen = 6,
    Reset = 7,
    Down = 8,
    Init = 9
}
