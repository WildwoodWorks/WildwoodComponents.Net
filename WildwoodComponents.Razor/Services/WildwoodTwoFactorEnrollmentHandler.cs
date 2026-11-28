using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using WildwoodComponents.Shared.Http;

namespace WildwoodComponents.Razor.Services;

/// <summary>
/// Watches the WildwoodAPI client's responses for the "2FA enrollment required" refusal (403 +
/// errorCode TWO_FACTOR_ENROLLMENT_REQUIRED) and records it on the CURRENT request's session, so a
/// session the API has restricted is known to be restricted even when no sign-in response said so
/// (JS: the http client's refusal hook marking the session, source <c>apiRefusal</c>).
/// </summary>
/// <remarks>
/// <para>
/// Razor is server-rendered: every user shares the same pooled handler, and the only thing that
/// says whose request this is, is the ambient <see cref="IHttpContextAccessor.HttpContext"/>. The
/// refusal is recorded only when the refused request carried THAT session's own token — the same
/// "this user's refusal only" rule the Blazor session manager applies to the app-wide event.
/// </para>
/// <para>
/// It also raises <see cref="IWildwoodApiEvents.TwoFactorEnrollmentRequired"/>, the same app-wide
/// event Blazor's handler raises, for a host that wants to log or route on it. The response itself
/// passes through untouched (its body is buffered so callers can still read it).
/// </para>
/// </remarks>
public sealed class WildwoodTwoFactorEnrollmentHandler : DelegatingHandler
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IWildwoodApiEvents _events;

    public WildwoodTwoFactorEnrollmentHandler(IHttpContextAccessor httpContextAccessor, IWildwoodApiEvents events)
    {
        _httpContextAccessor = httpContextAccessor;
        _events = events;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Forbidden || response.Content == null)
            return response;

        await response.Content.LoadIntoBufferAsync();
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!TwoFactorEnrollmentHandler.TryReadEnrollmentRefusal(body, out var message))
            return response;

        var refusedUser = TwoFactorEnrollmentHandler.SubjectOf(request.Headers.Authorization);
        _events.RaiseTwoFactorEnrollmentRequired(new TwoFactorEnrollmentRequiredEventArgs
        {
            UserId = refusedUser,
            RequestUri = request.RequestUri?.ToString(),
            Message = message
        });

        RecordOnCurrentSession(refusedUser);
        return response;
    }

    private void RecordOnCurrentSession(string? refusedUser)
    {
        if (refusedUser == null) return;

        var session = _httpContextAccessor.HttpContext?.RequestServices.GetService<IWildwoodSessionManager>();
        var token = session?.GetAccessToken();
        if (session == null || string.IsNullOrEmpty(token)) return;

        var sessionUser = TwoFactorEnrollmentHandler.SubjectOf(
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token));
        if (string.Equals(sessionUser, refusedUser, StringComparison.OrdinalIgnoreCase))
        {
            session.SetTwoFactorEnrollmentPending(true);
        }
    }
}
