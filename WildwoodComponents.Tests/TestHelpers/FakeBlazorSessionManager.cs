using WildwoodComponents.Blazor.Models;
using WildwoodComponents.Blazor.Services;

namespace WildwoodComponents.Tests.TestHelpers;

/// <summary>
/// Minimal in-memory <see cref="IWildwoodSessionManager"/> for Blazor signup tests: it remembers
/// the response it was logged in with and says whether there is a session, which is all the signup
/// flow asks of it.
/// </summary>
public class FakeBlazorSessionManager : IWildwoodSessionManager
{
    public FakeBlazorSessionManager(bool signedIn = false)
    {
        IsAuthenticated = signedIn;
    }

    /// <summary>How many times a signup stored a session here.</summary>
    public int LoginCalls { get; private set; }

    public bool IsAuthenticated { get; private set; }

    public bool IsInitialized => true;

    public string? AccessToken => CurrentUser?.JwtToken;

    public string? UserId => CurrentUser?.UserId;

    public string? UserEmail => CurrentUser?.Email;

    public AuthenticationResponse? CurrentUser { get; private set; }

    public event EventHandler? SessionExpired;

    public event EventHandler<AuthenticationResponse>? TokenRefreshed;

    public event EventHandler? SessionInitialized;

    public Task InitializeAsync()
    {
        SessionInitialized?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public Task<bool> LoginAsync(AuthenticationResponse authResponse)
    {
        LoginCalls++;
        CurrentUser = authResponse;
        IsAuthenticated = !string.IsNullOrEmpty(authResponse.JwtToken);
        return Task.FromResult(IsAuthenticated);
    }

    public Task LogoutAsync()
    {
        CurrentUser = null;
        IsAuthenticated = false;
        return Task.CompletedTask;
    }

    public Task<bool> RefreshTokenAsync()
    {
        if (CurrentUser is not null) TokenRefreshed?.Invoke(this, CurrentUser);
        return Task.FromResult(IsAuthenticated);
    }

    public Task OnAppResumedAsync() => Task.CompletedTask;

    public Task TouchSessionAsync() => Task.CompletedTask;

    public void NotifyAuthenticationFailure()
    {
        SessionExpired?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
