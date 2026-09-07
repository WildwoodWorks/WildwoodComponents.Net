using WildwoodComponents.Razor.Models;
using WildwoodComponents.Razor.Services;

namespace WildwoodComponents.Tests.TestHelpers;

/// <summary>
/// An <see cref="IWildwoodAuthService"/> that answers only the one call a ViewComponent makes while
/// rendering: <see cref="GetAuthConfigAsync"/>. Everything else throws, so a test that accidentally
/// drives a state-changing call fails loudly instead of quietly returning a default.
/// </summary>
public class FakeRazorAuthService : IWildwoodAuthService
{
    private readonly AuthConfigResponse? _config;

    /// <param name="config">The configuration to answer with; null models a failed read.</param>
    public FakeRazorAuthService(AuthConfigResponse? config)
    {
        _config = config;
    }

    public Task<AuthConfigResponse?> GetAuthConfigAsync() => Task.FromResult(_config);

    public Task<AuthResult> LoginAsync(LoginRequest request) => throw new NotSupportedException();

    public Task<AuthResult> RegisterAsync(RegisterRequest request) => throw new NotSupportedException();

    public Task LogoutAsync() => throw new NotSupportedException();

    public Task<AuthResult> RefreshTokenAsync() => throw new NotSupportedException();

    public Task<ApiResult> ForgotPasswordAsync(string email) => throw new NotSupportedException();

    public Task<ApiResult> ResetPasswordAsync(ResetPasswordRequest request) => throw new NotSupportedException();

    public Task<AuthResult> VerifyTwoFactorAsync(TwoFactorVerifyRequest request) => throw new NotSupportedException();
}
