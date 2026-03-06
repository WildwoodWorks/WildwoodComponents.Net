using Microsoft.Extensions.Logging;
using System.Text.Json;
using WildwoodComponents.Blazor.Extensions;
using WildwoodComponents.Blazor.Models;
using WildwoodComponents.Shared.Utilities;

namespace WildwoodComponents.Blazor.Services;

/// <summary>
/// Manages session lifecycle including automatic token refresh, session expiry detection,
/// and app resume handling. Reusable across any app that uses WildwoodComponents.
/// </summary>
public interface IWildwoodSessionManager : IDisposable
{
    /// <summary>
    /// Whether the user is currently authenticated with a valid session.
    /// </summary>
    bool IsAuthenticated { get; }

    /// <summary>
    /// Whether the session manager has been initialized from storage.
    /// </summary>
    bool IsInitialized { get; }

    /// <summary>
    /// The current JWT access token, or null if not authenticated or session expired.
    /// </summary>
    string? AccessToken { get; }

    /// <summary>
    /// The current user's ID, or null if not authenticated.
    /// </summary>
    string? UserId { get; }

    /// <summary>
    /// The current user's email, or null if not authenticated.
    /// </summary>
    string? UserEmail { get; }

    /// <summary>
    /// The current authenticated user response, or null if not authenticated.
    /// </summary>
    AuthenticationResponse? CurrentUser { get; }

    /// <summary>
    /// Initialize the session manager from stored authentication data.
    /// Must be called after first render (requires JS interop for storage access).
    /// </summary>
    Task InitializeAsync();

    /// <summary>
    /// Store authentication data and start session monitoring.
    /// Call after successful login.
    /// </summary>
    Task<bool> LoginAsync(AuthenticationResponse authResponse);

    /// <summary>
    /// Clear all authentication data and stop session monitoring.
    /// </summary>
    Task LogoutAsync();

    /// <summary>
    /// Attempt to refresh the JWT token using the stored refresh token.
    /// Returns true if refresh succeeded, false if the session is truly expired.
    /// </summary>
    Task<bool> RefreshTokenAsync();

    /// <summary>
    /// Called when the app resumes from background. Proactively refreshes the token
    /// if it may have expired while the app was suspended.
    /// </summary>
    Task OnAppResumedAsync();

    /// <summary>
    /// Extend the session expiration (sliding expiration).
    /// Call on user activity to keep the session alive.
    /// </summary>
    Task TouchSessionAsync();

    /// <summary>
    /// Fired when the session has expired and token refresh has failed.
    /// The consuming app should redirect to the login page.
    /// </summary>
    event EventHandler? SessionExpired;

    /// <summary>
    /// Fired when the token has been refreshed successfully.
    /// The consuming app should update any cached tokens.
    /// </summary>
    event EventHandler<AuthenticationResponse>? TokenRefreshed;

    /// <summary>
    /// Fired when initialization from storage is complete.
    /// </summary>
    event EventHandler? SessionInitialized;
}

/// <summary>
/// Default implementation of IWildwoodSessionManager that orchestrates existing
/// WildwoodComponents services for session management.
/// Includes a proactive refresh timer that refreshes the JWT before it expires.
/// </summary>
public class WildwoodSessionManager : IWildwoodSessionManager
{
    private readonly IAuthenticationService _authService;
    private readonly IAIService? _aiService;
    private readonly ILocalStorageService _localStorage;
    private readonly ILogger<WildwoodSessionManager> _logger;
    private readonly WildwoodComponentsOptions _options;

    private AuthenticationResponse? _currentUser;
    private DateTime? _sessionExpiry;
    private Timer? _refreshTimer;
    private bool _isInitialized;
    private bool _disposed;

    // Concurrent refresh protection: semaphore + recent-refresh tracking
    private readonly SemaphoreSlim _refreshSemaphore = new(1, 1);
    private DateTime _lastRefreshTime = DateTime.MinValue;
    private static readonly TimeSpan RecentRefreshWindow = TimeSpan.FromSeconds(30);

    public bool IsAuthenticated => _currentUser != null
        && !string.IsNullOrEmpty(_currentUser.JwtToken)
        && !IsSessionExpired();

    public bool IsInitialized => _isInitialized;
    public string? AccessToken => IsSessionExpired() ? null : _currentUser?.JwtToken;
    public string? UserId => _currentUser?.UserId;
    public string? UserEmail => _currentUser?.Email;
    public AuthenticationResponse? CurrentUser => _currentUser;

    public event EventHandler? SessionExpired;
    public event EventHandler<AuthenticationResponse>? TokenRefreshed;
    public event EventHandler? SessionInitialized;

    public WildwoodSessionManager(
        IAuthenticationService authService,
        IAIService? aiService,
        ILocalStorageService localStorage,
        ILogger<WildwoodSessionManager> logger,
        WildwoodComponentsOptions options)
    {
        _authService = authService;
        _aiService = aiService;
        _localStorage = localStorage;
        _logger = logger;
        _options = options;

        // Subscribe to AI service auth failures for reactive token refresh (if AI service available)
        if (_aiService != null)
        {
            _aiService.AuthenticationFailed += OnAuthenticationFailed;
        }

        // Subscribe to auth service events to stay in sync
        _authService.OnAuthenticationChanged += OnAuthChanged;
        _authService.OnLogout += OnAuthLogout;
    }

    public async Task InitializeAsync()
    {
        if (_isInitialized)
            return;

        try
        {
            _logger.LogInformation("Initializing session manager from storage...");

            // Restore session expiry
            var expiryStr = await _localStorage.GetItemAsync<string>(SessionConstants.SessionExpiry);
            if (!string.IsNullOrEmpty(expiryStr) && DateTime.TryParse(expiryStr, out var expiry))
            {
                _sessionExpiry = expiry;
                _logger.LogInformation("Session expiry loaded: {Expiry}, now: {Now}", expiry, DateTime.UtcNow);

                if (IsSessionExpired())
                {
                    _logger.LogInformation("Stored session has expired, clearing storage");
                    await ClearStorageAsync();
                    _isInitialized = true;
                    SessionInitialized?.Invoke(this, EventArgs.Empty);
                    return;
                }
            }

            // Restore authentication data
            var storedAuth = await _localStorage.GetItemAsync<AuthenticationResponse>(SessionConstants.AuthData);
            if (storedAuth != null && !string.IsNullOrEmpty(storedAuth.JwtToken))
            {
                _currentUser = storedAuth;
                _logger.LogInformation("Restored session for user: {Email}", storedAuth.Email);

                // If no expiry was stored, set one now
                if (_sessionExpiry == null)
                {
                    await ExtendSessionAsync();
                }

                // Set the token on the AI service so API calls work
                _aiService.SetAuthToken(storedAuth.JwtToken);

                // If auto-refresh is enabled, proactively refresh the token
                // in case it expired while the app was closed, and start the refresh timer
                if (_options.EnableAutoTokenRefresh)
                {
                    var refreshed = await RefreshTokenAsync();
                    if (!refreshed)
                    {
                        _logger.LogWarning("Token refresh failed during initialization - session may be expired");
                        // Don't clear yet - the token might still be valid, let the first API call determine
                        // But still schedule based on current token's exp claim
                        ScheduleRefreshFromToken(storedAuth.JwtToken);
                    }
                    // If refreshed, OnAuthChanged will have been called which schedules the timer
                }
            }
            else
            {
                _logger.LogInformation("No stored session found");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing session manager");
        }
        finally
        {
            _isInitialized = true;
            SessionInitialized?.Invoke(this, EventArgs.Empty);
            _logger.LogInformation("Session manager initialized. IsAuthenticated: {IsAuth}", IsAuthenticated);
        }
    }

    public async Task<bool> LoginAsync(AuthenticationResponse authResponse)
    {
        if (authResponse == null || string.IsNullOrEmpty(authResponse.JwtToken))
        {
            _logger.LogWarning("Invalid authentication response - no JWT token");
            return false;
        }

        try
        {
            _currentUser = authResponse;
            _sessionExpiry = DateTime.UtcNow.AddMinutes(_options.SessionExpirationMinutes);

            // Persist to storage
            await _localStorage.SetItemAsync(SessionConstants.AuthData, authResponse);
            await _localStorage.SetItemAsync(SessionConstants.SessionExpiry, _sessionExpiry.Value.ToString("O"));

            // Set token on AI service
            _aiService.SetAuthToken(authResponse.JwtToken);

            _logger.LogInformation("User logged in: {Email} (session expires: {Expiry})",
                authResponse.Email, _sessionExpiry);

            // Schedule proactive token refresh based on JWT expiration
            if (_options.EnableAutoTokenRefresh)
            {
                ScheduleRefreshFromToken(authResponse.JwtToken);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during login");
            return false;
        }
    }

    public async Task LogoutAsync()
    {
        try
        {
            var email = _currentUser?.Email;
            StopRefreshTimer();
            await ClearStorageAsync();
            _logger.LogInformation("User logged out: {Email}", email);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during logout");
        }
    }

    public async Task<bool> RefreshTokenAsync()
    {
        if (!_options.EnableAutoTokenRefresh)
        {
            _logger.LogDebug("Token refresh is disabled");
            return false;
        }

        if (_currentUser == null || string.IsNullOrEmpty(_currentUser.RefreshToken))
        {
            _logger.LogWarning("No refresh token available");
            return false;
        }

        // Check if a refresh completed very recently (within 30 seconds)
        var timeSinceLastRefresh = DateTime.UtcNow - _lastRefreshTime;
        if (timeSinceLastRefresh < RecentRefreshWindow)
        {
            _logger.LogDebug(
                "Token was refreshed {Seconds:F0}s ago, skipping duplicate refresh",
                timeSinceLastRefresh.TotalSeconds);
            return true;
        }

        // Use semaphore to prevent concurrent refresh attempts
        var acquired = await _refreshSemaphore.WaitAsync(TimeSpan.FromSeconds(10));
        if (!acquired)
        {
            _logger.LogDebug("Could not acquire refresh semaphore within timeout");
            return false;
        }

        try
        {
            // Double-check after acquiring semaphore (another call may have refreshed)
            timeSinceLastRefresh = DateTime.UtcNow - _lastRefreshTime;
            if (timeSinceLastRefresh < RecentRefreshWindow)
            {
                _logger.LogDebug(
                    "Token was refreshed {Seconds:F0}s ago (after semaphore), skipping",
                    timeSinceLastRefresh.TotalSeconds);
                return true;
            }

            _logger.LogInformation("Attempting token refresh...");

            var refreshed = await _authService.RefreshTokenAsync();

            if (refreshed)
            {
                _lastRefreshTime = DateTime.UtcNow;
                _logger.LogInformation("Token refreshed successfully");
                // The OnAuthChanged handler will update _currentUser, storage,
                // AI service token, and reschedule the refresh timer
                return true;
            }

            _logger.LogWarning("Token refresh failed");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during token refresh");
            return false;
        }
        finally
        {
            _refreshSemaphore.Release();
        }
    }

    public async Task OnAppResumedAsync()
    {
        if (!_isInitialized || !_options.EnableAutoTokenRefresh)
            return;

        if (_currentUser == null)
            return;

        _logger.LogInformation("App resumed - checking session validity");

        // If session has expired entirely, fire SessionExpired
        if (IsSessionExpired())
        {
            _logger.LogInformation("Session expired while app was suspended");
            StopRefreshTimer();
            await ClearStorageAsync();
            SessionExpired?.Invoke(this, EventArgs.Empty);
            return;
        }

        // Proactively refresh the token in case the JWT expired while backgrounded
        var refreshed = await RefreshTokenAsync();
        if (!refreshed && _currentUser != null)
        {
            _logger.LogInformation("Proactive refresh failed, will rely on reactive refresh on next API call");
        }
    }

    public async Task TouchSessionAsync()
    {
        if (IsAuthenticated && _options.SlidingExpiration)
        {
            await ExtendSessionAsync();
        }
    }

    #region Proactive Refresh Timer

    /// <summary>
    /// Parses the JWT exp claim and schedules a refresh at 80% of the token's remaining lifetime.
    /// This ensures the token is refreshed BEFORE it expires, preventing 401 errors.
    /// </summary>
    private void ScheduleRefreshFromToken(string jwtToken)
    {
        try
        {
            var expirationUtc = TokenExpiryParser.GetJwtExpiration(jwtToken);
            if (expirationUtc == null)
            {
                _logger.LogWarning("Could not parse JWT expiration - using fallback refresh interval");
                // Fallback: refresh every 10 minutes
                ScheduleRefreshTimer(TimeSpan.FromMinutes(10));
                return;
            }

            var now = DateTime.UtcNow;
            var totalLifetime = expirationUtc.Value - now;

            if (totalLifetime.TotalSeconds <= 0)
            {
                // Token already expired - refresh immediately
                _logger.LogWarning("JWT already expired - triggering immediate refresh");
                ScheduleRefreshTimer(TimeSpan.FromSeconds(1));
                return;
            }

            // Refresh at 80% of remaining lifetime
            // e.g., 60 min token → refresh at 48 min; 15 min token → refresh at 12 min
            var refreshDelay = TimeSpan.FromSeconds(totalLifetime.TotalSeconds * 0.8);

            // Minimum 30 seconds, maximum 55 minutes
            if (refreshDelay.TotalSeconds < 30)
                refreshDelay = TimeSpan.FromSeconds(30);
            if (refreshDelay.TotalMinutes > 55)
                refreshDelay = TimeSpan.FromMinutes(55);

            _logger.LogInformation(
                "JWT expires at {Expiry} ({TotalMin:F1} min from now). Scheduling refresh in {RefreshMin:F1} min",
                expirationUtc.Value, totalLifetime.TotalMinutes, refreshDelay.TotalMinutes);

            ScheduleRefreshTimer(refreshDelay);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scheduling proactive token refresh");
            // Fallback
            ScheduleRefreshTimer(TimeSpan.FromMinutes(10));
        }
    }

    private void ScheduleRefreshTimer(TimeSpan delay)
    {
        StopRefreshTimer();

        _refreshTimer = new Timer(
            OnRefreshTimerElapsed,
            null,
            delay,
            Timeout.InfiniteTimeSpan); // One-shot timer, reschedules after each refresh
    }

    private void StopRefreshTimer()
    {
        _refreshTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        _refreshTimer?.Dispose();
        _refreshTimer = null;
    }

    private void OnRefreshTimerElapsed(object? state)
    {
        if (_disposed || _currentUser == null)
            return;

        _logger.LogInformation("Proactive refresh timer fired - refreshing token before expiration");
        _ = HandleProactiveRefreshAsync();
    }

    private async Task HandleProactiveRefreshAsync()
    {
        var refreshed = await RefreshTokenAsync();

        if (!refreshed)
        {
            _logger.LogWarning("Proactive token refresh failed");
            // Don't fire SessionExpired yet - the token might still be valid
            // The reactive handler (on 401) will fire SessionExpired if needed

            // Retry in 60 seconds
            if (!_disposed && _currentUser != null)
            {
                _logger.LogInformation("Scheduling retry in 60 seconds");
                ScheduleRefreshTimer(TimeSpan.FromSeconds(60));
            }
        }
        // If refreshed, OnAuthChanged will reschedule the timer with the new token
    }

    #endregion

    #region Private Methods

    private bool IsSessionExpired()
    {
        if (_sessionExpiry == null)
            return false;

        return DateTime.UtcNow > _sessionExpiry.Value;
    }

    private async Task ExtendSessionAsync()
    {
        _sessionExpiry = DateTime.UtcNow.AddMinutes(_options.SessionExpirationMinutes);
        try
        {
            await _localStorage.SetItemAsync(SessionConstants.SessionExpiry, _sessionExpiry.Value.ToString("O"));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist session expiry");
        }
    }

    private async Task ClearStorageAsync()
    {
        _currentUser = null;
        _sessionExpiry = null;
        try
        {
            await _localStorage.RemoveItemAsync(SessionConstants.AuthData);
            await _localStorage.RemoveItemAsync(SessionConstants.SessionExpiry);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clear session storage");
        }
    }

    #endregion

    #region Event Handlers

    private void OnAuthenticationFailed(object? sender, EventArgs e)
    {
        if (!_options.EnableAutoTokenRefresh)
        {
            _logger.LogWarning("Authentication failed - token refresh is disabled, firing SessionExpired");
            SessionExpired?.Invoke(this, EventArgs.Empty);
            return;
        }

        _logger.LogWarning("Authentication failed (401) - attempting reactive token refresh");

        // Fire and forget - we're on an event handler
        _ = HandleAuthFailureAsync();
    }

    private async Task HandleAuthFailureAsync()
    {
        var refreshed = await RefreshTokenAsync();

        if (refreshed)
        {
            _logger.LogInformation("Token refresh succeeded after auth failure");
            // Token was refreshed - the TokenRefreshed event was already fired by OnAuthChanged
        }
        else
        {
            _logger.LogWarning("Token refresh failed after auth failure - session expired");
            StopRefreshTimer();
            await ClearStorageAsync();
            SessionExpired?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnAuthChanged(AuthenticationResponse authResponse)
    {
        if (authResponse == null || string.IsNullOrEmpty(authResponse.JwtToken))
            return;

        _logger.LogInformation("Auth state changed - updating session for: {Email}", authResponse.Email);
        _currentUser = authResponse;

        // Update the AI service with the new token (if AI service available)
        _aiService?.SetAuthToken(authResponse.JwtToken);

        // Extend the session if sliding expiration is enabled
        if (_options.SlidingExpiration)
        {
            _ = ExtendSessionAsync();
        }

        // Persist the updated auth data
        _ = _localStorage.SetItemAsync(SessionConstants.AuthData, authResponse);

        // Reschedule the proactive refresh timer with the new token's expiration
        if (_options.EnableAutoTokenRefresh)
        {
            ScheduleRefreshFromToken(authResponse.JwtToken);
        }

        // Notify consumers
        TokenRefreshed?.Invoke(this, authResponse);
    }

    private void OnAuthLogout()
    {
        _logger.LogInformation("Auth service reported logout");
        StopRefreshTimer();
        _currentUser = null;
        _sessionExpiry = null;
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        StopRefreshTimer();
        _refreshSemaphore.Dispose();
        if (_aiService != null)
        {
            _aiService.AuthenticationFailed -= OnAuthenticationFailed;
        }
        _authService.OnAuthenticationChanged -= OnAuthChanged;
        _authService.OnLogout -= OnAuthLogout;
    }

    #endregion
}
