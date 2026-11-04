namespace MachineSessionManager.Idira;

public sealed class IdiraCredentialResponse
{
    public string Content { get; }

    public IdiraCredentialResponse(string content)
    {
        Content = content;
    }
}
