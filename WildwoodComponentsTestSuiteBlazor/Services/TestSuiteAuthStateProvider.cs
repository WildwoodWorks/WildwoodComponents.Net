using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace WildwoodComponentsTestSuiteBlazor.Services;

/// <summary>
/// Custom AuthenticationStateProvider that reads JWT from circuit state storage
/// and extracts claims for Blazor authorization.
/// </summary>
public class TestSuiteAuthStateProvider : AuthenticationStateProvider
{
    private readonly CircuitStateStorageService _storageService;
    private ClaimsPrincipal _currentUser = new(new ClaimsIdentity());

    public TestSuiteAuthStateProvider(CircuitStateStorageService storageService)
    {
        _storageService = storageService;
        _storageService.OnChanged += () =>
        {
            _ = Task.Run(async () =>
            {
                await RefreshAuthState();
            });
        };
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var token = await _storageService.GetAuthTokenAsync();

        if (string.IsNullOrEmpty(token))
        {
            _currentUser = new ClaimsPrincipal(new ClaimsIdentity());
            return new AuthenticationState(_currentUser);
        }

        try
        {
            var handler = new JwtSecurityTokenHandler();
            var jwt = handler.ReadJwtToken(token);

            // Check expiration
            if (jwt.ValidTo < DateTime.UtcNow)
            {
                _currentUser = new ClaimsPrincipal(new ClaimsIdentity());
                return new AuthenticationState(_currentUser);
            }

            var claims = jwt.Claims.ToList();

            // Ensure we have a Name claim for display
            if (!claims.Any(c => c.Type == ClaimTypes.Name))
            {
                var emailClaim = claims.FirstOrDefault(c =>
                    c.Type == "email" || c.Type == ClaimTypes.Email);
                if (emailClaim != null)
                    claims.Add(new Claim(ClaimTypes.Name, emailClaim.Value));
            }

            var identity = new ClaimsIdentity(claims, "jwt");
            _currentUser = new ClaimsPrincipal(identity);
        }
        catch
        {
            _currentUser = new ClaimsPrincipal(new ClaimsIdentity());
        }

        return new AuthenticationState(_currentUser);
    }

    public async Task RefreshAuthState()
    {
        var state = await GetAuthenticationStateAsync();
        NotifyAuthenticationStateChanged(Task.FromResult(state));
    }

    public void NotifyStateChanged()
    {
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }
}
