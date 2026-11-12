using System.Net;
using System.Text;
using System.Text.Json;
using MachineSessionManager.Idira;
using Xunit;

namespace MachineSessionManager.Tests;

public sealed class IdiraClientTests
{
    private const string Password = "test-secret-DO-NOT-LOG";

    [Theory]
    [InlineData("https://ccp.example.com", "svc_sessions@company.com")]
    [InlineData("https://ccp.example.com", "DOMAIN\\svc_sessions")]
    [InlineData("https://ccp.example.com/proxy", "svc_sessions@company.com")]
    public async Task UsesDocumentedGetEndpointAndExactSafeAndUserNameLookup(string baseUrl, string userName)
    {
        using var httpClient = new HttpClient(new StubHandler(
            (request, _) =>
            {
                Assert.Equal(HttpMethod.Get, request.Method);
                Assert.Equal(baseUrl + "/AIMWebService/api/Accounts", request.RequestUri!.GetLeftPart(UriPartial.Path));

                var query = request.RequestUri.Query.TrimStart('?').Split('&')
                    .Select(item => item.Split('=', 2))
                    .ToDictionary(parts => parts[0], parts => Uri.UnescapeDataString(parts[1]));

                Assert.Equal(3, query.Count);
                Assert.Equal("MachineSessionManager", query["AppID"]);
                Assert.Equal("Service Accounts", query["Safe"]);
                Assert.Equal(userName, query["UserName"]);
                Assert.Null(request.Content);
                Assert.Null(request.Headers.Authorization);

                return Task.FromResult(Response(
                    HttpStatusCode.OK,
                    JsonSerializer.Serialize(new { Content = Password, UserName = userName })));
            }));

        var client = new IdiraClient(
            httpClient,
            new IdiraSettings(baseUrl, "MachineSessionManager", "Service Accounts"));

        Assert.Equal(Password, await client.GetPasswordAsync(userName));
    }

    [Fact]
    public async Task AcceptsContentWithoutOptionalAccountPropertiesAndPreservesPasswordWhitespace()
    {
        using var httpClient = ClientReturning(
            HttpStatusCode.OK,
            JsonSerializer.Serialize(new { Content = " password " }));

        Assert.Equal(" password ", await CreateClient(httpClient).GetPasswordAsync("svc@company.com"));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("{\"Content\":null}")]
    [InlineData("{\"Content\":\"\"}")]
    public async Task MissingContentIsRejected(string json)
    {
        using var httpClient = ClientReturning(HttpStatusCode.OK, json);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => CreateClient(httpClient).GetPasswordAsync("svc@company.com"));

        Assert.Contains("no password", error.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Redirect)]
    public async Task HttpFailuresIncludeTheProviderErrorAndStatus(HttpStatusCode status)
    {
        var body = JsonSerializer.Serialize(new { ErrorCode = "APPAP227E", ErrorMsg = "Too many objects match the query." });

        using var httpClient = ClientReturning(status, body);

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => CreateClient(httpClient).GetPasswordAsync("svc@company.com"));

        Assert.Equal(status, error.StatusCode);
        Assert.Contains(body, error.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task NonJsonHttpFailuresStillReportTheHttpFailure(HttpStatusCode status)
    {
        const string body = "<html>CCP service unavailable</html>";

        using var httpClient = ClientReturning(status, body);

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => CreateClient(httpClient).GetPasswordAsync("svc@company.com"));

        Assert.Contains($"HTTP {(int)status}", error.Message);
        Assert.Contains(body, error.Message);
    }

    [Fact]
    public async Task MalformedJsonPreservesTheParsingError()
    {
        using var httpClient = ClientReturning(HttpStatusCode.OK, "{\"Content\":invalid}");

        await Assert.ThrowsAnyAsync<JsonException>(() => CreateClient(httpClient).GetPasswordAsync("svc@company.com"));
    }

    [Fact]
    public async Task TransportErrorsArePreserved()
    {
        var failure = new HttpRequestException("The remote name could not be resolved.");

        using var httpClient = new HttpClient(
            new StubHandler((_, _) => Task.FromException<HttpResponseMessage>(failure)));

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => CreateClient(httpClient).GetPasswordAsync("svc@company.com"));

        Assert.Same(failure, error);
    }

    [Fact]
    public async Task CallerCancellationIsPreserved()
    {
        using var cancellation = new CancellationTokenSource();
        using var httpClient = new HttpClient(new StubHandler(
            (_, _) =>
            {
                cancellation.Cancel();

                return Task.FromCanceled<HttpResponseMessage>(cancellation.Token);
            }));

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () =>
                CreateClient(httpClient).GetPasswordAsync("svc@company.com", cancellation.Token));

        Assert.Equal(cancellation.Token, error.CancellationToken);
    }

    private static IdiraClient CreateClient(HttpClient httpClient)
    {
        return new IdiraClient(
            httpClient,
            new IdiraSettings("https://ccp.example.com", "MachineSessionManager", "ServiceAccounts"));
    }

    private static HttpClient ClientReturning(HttpStatusCode status, string json)
    {
        return new HttpClient(new StubHandler((_, _) => Task.FromResult(Response(status, json))));
    }

    private static HttpResponseMessage Response(HttpStatusCode status, string json)
    {
        return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _send;

        public StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        {
            _send = send;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return _send(request, cancellationToken);
        }
    }
}
