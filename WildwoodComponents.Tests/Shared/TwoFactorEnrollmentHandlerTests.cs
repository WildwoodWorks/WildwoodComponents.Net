using System.Net;
using System.Net.Http.Headers;
using System.Text;
using WildwoodComponents.Shared.Http;

namespace WildwoodComponents.Tests.Shared;

/// <summary>
/// A 403 TWO_FACTOR_ENROLLMENT_REQUIRED from the API becomes an event naming the user whose request was
/// refused, and the response still reaches the caller intact.
/// </summary>
public class TwoFactorEnrollmentHandlerTests
{
    private sealed class Canned : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;
        public Canned(HttpStatusCode status, string body) { _status = status; _body = body; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(_status) { Content = new StringContent(_body, Encoding.UTF8, "application/json") });
    }

    private static string Jwt(string sub)
    {
        static string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{B64("{\"alg\":\"none\"}")}.{B64($"{{\"sub\":\"{sub}\",\"tfa_enroll_pending\":\"true\"}}")}.sig";
    }

    private static async Task<(HttpResponseMessage Response, List<TwoFactorEnrollmentRequiredEventArgs> Raised)> Send(HttpStatusCode status, string body)
    {
        var events = new WildwoodApiEvents();
        var raised = new List<TwoFactorEnrollmentRequiredEventArgs>();
        events.TwoFactorEnrollmentRequired += (_, e) => raised.Add(e);
        var invoker = new HttpMessageInvoker(new TwoFactorEnrollmentHandler(events) { InnerHandler = new Canned(status, body) });
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.test/api/users");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Jwt("user-7"));
        return (await invoker.SendAsync(request, CancellationToken.None), raised);
    }

    [Fact]
    public async Task EnrollmentRefusal_RaisesTheEvent_AndLeavesTheBodyReadable()
    {
        const string body = """{"error":"Two-factor authentication must be set up before using this application.","errorCode":"TWO_FACTOR_ENROLLMENT_REQUIRED"}""";

        var (response, raised) = await Send(HttpStatusCode.Forbidden, body);

        var e = Assert.Single(raised);
        Assert.Equal("user-7", e.UserId);
        Assert.Equal("https://api.test/api/users", e.RequestUri);
        Assert.StartsWith("Two-factor authentication must be set up", e.Message);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(body, await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, """{"error":"Forbidden"}""")]
    [InlineData(HttpStatusCode.Forbidden, "not json")]
    [InlineData(HttpStatusCode.Unauthorized, """{"errorCode":"TWO_FACTOR_ENROLLMENT_REQUIRED"}""")]
    [InlineData(HttpStatusCode.OK, """{"errorCode":"TWO_FACTOR_ENROLLMENT_REQUIRED"}""")]
    public async Task OtherResponses_RaiseNothing(HttpStatusCode status, string body)
    {
        var (_, raised) = await Send(status, body);

        Assert.Empty(raised);
    }

    [Fact]
    public void SubjectOf_IgnoresNonBearerAndGarbage()
    {
        Assert.Null(TwoFactorEnrollmentHandler.SubjectOf(null));
        Assert.Null(TwoFactorEnrollmentHandler.SubjectOf(new AuthenticationHeaderValue("Basic", "abc")));
        Assert.Null(TwoFactorEnrollmentHandler.SubjectOf(new AuthenticationHeaderValue("Bearer", "not-a-jwt")));
        Assert.Equal("u1", TwoFactorEnrollmentHandler.SubjectOf(new AuthenticationHeaderValue("Bearer", Jwt("u1"))));
    }
}
