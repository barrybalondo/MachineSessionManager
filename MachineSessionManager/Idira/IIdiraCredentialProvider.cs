namespace MachineSessionManager.Idira;

public interface IIdiraCredentialProvider
{
    Task<string> GetPasswordAsync(string userName, CancellationToken cancellationToken = default);
}
