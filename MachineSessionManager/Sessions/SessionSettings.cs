namespace MachineSessionManager.Sessions;

public sealed class SessionSettings
{
    public SessionModeType ModeType { get; }

    public string UserName { get; }

    public IReadOnlyList<string> Machines { get; }

    public TimeSpan MachineTimeout { get; }

    public SessionSettings(SessionModeType mode, string userName, IEnumerable<string> machines, TimeSpan machineTimeout)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(userName);
        ArgumentNullException.ThrowIfNull(machines);

        // Task.WaitAsync uses a timer with this maximum supported duration.
        if (machineTimeout <= TimeSpan.Zero || machineTimeout > TimeSpan.FromMilliseconds(uint.MaxValue - 1))
        {
            throw new ArgumentOutOfRangeException(nameof(machineTimeout),
                "MachineTimeout must be positive and no greater than 49.17:02:47.294.");
        }

        var machineNames = machines.ToArray();
        if (machineNames.Length == 0 || machineNames.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Specify at least one nonempty machine name.", nameof(machines));
        }

        ModeType = mode;
        UserName = userName.Trim();
        MachineTimeout = machineTimeout;
        Machines = Array.AsReadOnly(machineNames
            .Select(machine => machine.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray());
    }
}
