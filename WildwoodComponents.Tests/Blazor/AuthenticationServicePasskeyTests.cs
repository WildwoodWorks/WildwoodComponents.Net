using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using WildwoodComponents.Blazor.Services;
using WildwoodComponents.Tests.TestHelpers;

namespace WildwoodComponents.Tests.Blazor;

/// <summary>
/// The API accepts a passkey assertion only against the challenge it issued, identified by the options'
/// sessionId. Without it the server answers "Passkey sign-in expired" to every Blazor passkey sign-in.
/// </summary>
public class AuthenticationServicePasskeyTests
{
    [Fact]
    public async Task Verify_SendsTheSessionIdFromTheOptions()
    {
        var handler = new FakeHttpMessageHandler()
            .WhenOk("webauthn/authenticate/options", """{"challenge":"abc","rpId":"example.com","sessionId":"session-42"}""")
            .When("webauthn/authenticate", HttpStatusCode.Unauthorized, """{"error":"AuthenticationFailed","message":"nope"}""");
        var service = new AuthenticationService(handler.CreateClient("https://api.test/"), new FakeLocalStorageService(),
            NullLogger<AuthenticationService>.Instance);

        await service.GetPasskeyAuthenticationOptionsAsync("app-1");
        var credential = JsonDocument.Parse("""
            {"id":"cred","rawId":"cred","type":"public-key",
             "response":{"clientDataJSON":"c","authenticatorData":"a","signature":"s","userHandle":"u"}}
            """).RootElement;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.VerifyPasskeyAuthenticationAsync("app-1", credential));

        var verify = handler.Requests.Single(r => r.Url.EndsWith("webauthn/authenticate", StringComparison.OrdinalIgnoreCase));
        using var body = JsonDocument.Parse(verify.Body!);
        Assert.Equal("session-42", (body.RootElement.TryGetProperty("sessionId", out var s) ? s : body.RootElement.GetProperty("SessionId")).GetString());
    }

    private static string ClientData(string challenge) =>
        Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(
                $$"""{"type":"webauthn.get","challenge":"{{challenge}}","origin":"https://example.com"}"""))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    [Fact]
    public void ConcurrentSignIns_EachFindTheirOwnSession()
    {
        // Two sign-ins in flight on one service: the second options call must not hijack the first assertion.
        var sessions = new PasskeyChallengeSessions();
        sessions.Remember(JsonDocument.Parse("""{"challenge":"Y2hhbGxlbmdlLWE","sessionId":"session-a"}""").RootElement);
        sessions.Remember(JsonDocument.Parse("""{"challenge":"Y2hhbGxlbmdlLWI","sessionId":"session-b"}""").RootElement);

        Assert.Equal("session-a", sessions.Take(ClientData("Y2hhbGxlbmdlLWE")));
        Assert.Equal("session-b", sessions.Take(ClientData("Y2hhbGxlbmdlLWI")));
    }

    [Fact]
    public void UnreadableClientData_FallsBackToTheLatestSession()
    {
        var sessions = new PasskeyChallengeSessions();
        sessions.Remember(JsonDocument.Parse("""{"challenge":"Y2hhbGxlbmdlLWE","sessionId":"session-a"}""").RootElement);

        Assert.Equal("session-a", sessions.Take("not-base64!"));
        Assert.Null(sessions.Take("not-base64!"));
    }
}
