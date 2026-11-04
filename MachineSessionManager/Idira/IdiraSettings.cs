namespace MachineSessionManager.Idira;

public sealed class IdiraSettings
{
    public string BaseUrl { get; }

    public string AppId { get; }

    public string Safe { get; }

    public IdiraSettings(string baseUrl, string appId, string safe)
    {
        BaseUrl = baseUrl;
        AppId = appId;
        Safe = safe;
    }
}
