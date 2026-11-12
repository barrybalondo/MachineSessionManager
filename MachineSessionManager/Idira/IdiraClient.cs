using System.Net.Http.Json;

namespace MachineSessionManager.Idira;

// https://docs.cyberark.com/credential-providers/latest/en/content/ccp/calling-the-web-service-using-rest.htm
public sealed class IdiraClient : IIdiraCredentialProvider
{
    private readonly HttpClient _httpClient;

    private readonly IdiraSettings _settings;

    public IdiraClient(HttpClient httpClient, IdiraSettings settings)
    {
        _httpClient = httpClient;
        _settings = settings;
    }

    public async Task<string> GetPasswordAsync(string userName, CancellationToken cancellationToken = default)
    {
        var queryString = $"AppID={_settings.AppId}&Safe={_settings.Safe}&UserName={userName}";
        var url = $"{_settings.BaseUrl}/AIMWebService/api/Accounts?{queryString}";

        using var response = await _httpClient.GetAsync(url, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var message = await response.Content.ReadAsStringAsync(cancellationToken);

            throw new HttpRequestException(
                $"Idira CCP request failed with HTTP {(int)response.StatusCode}: {message}",
                null,
                response.StatusCode);
        }

        var result = await response.Content.ReadFromJsonAsync<IdiraCredentialResponse>(
            cancellationToken: cancellationToken);

        if (string.IsNullOrEmpty(result?.Content))
        {
            throw new InvalidOperationException("Idira CCP returned no password in Content.");
        }

        return result.Content;
    }
}
