using Microsoft.AspNetCore.Mvc.ViewComponents;
using Microsoft.Extensions.Logging.Abstractions;
using WildwoodComponents.Razor.Components.Security;
using WildwoodComponents.Razor.Models;
using WildwoodComponents.Razor.Services;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Tests.Razor;

/// <summary>
/// Razor parity with Blazor's TwoFactorSettingsComponent "app-not-enforcing" notice, and with the
/// chat components that follow the session id the server names.
/// </summary>
public class RazorTwoFactorNoticeAndChatTests
{
    private sealed class StubTwoFactorService : IWildwoodTwoFactorSettingsService
    {
        private readonly TwoFactorConfiguration? _configuration;
        public StubTwoFactorService(TwoFactorConfiguration? configuration) => _configuration = configuration;

        public Task<TwoFactorConfiguration?> GetConfigurationAsync() => Task.FromResult(_configuration);
        public Task<TwoFactorUserStatus?> GetStatusAsync() => Task.FromResult<TwoFactorUserStatus?>(new TwoFactorUserStatus());
        public Task<List<TwoFactorCredential>> GetCredentialsAsync() => Task.FromResult(new List<TwoFactorCredential>());
        public Task<bool> SetPrimaryCredentialAsync(string credentialId) => Task.FromResult(true);
        public Task<bool> RemoveCredentialAsync(string credentialId) => Task.FromResult(true);
        public Task<EmailEnrollmentResult?> EnrollEmailAsync(string? email = null) => Task.FromResult<EmailEnrollmentResult?>(null);
        public Task<bool> VerifyEmailEnrollmentAsync(string credentialId, string code) => Task.FromResult(true);
        public Task<AuthenticatorEnrollmentResult?> BeginAuthenticatorEnrollmentAsync(string? friendlyName = null) => Task.FromResult<AuthenticatorEnrollmentResult?>(null);
        public Task<bool> CompleteAuthenticatorEnrollmentAsync(string credentialId, string code) => Task.FromResult(true);
        public Task<RecoveryCodeInfo?> GetRecoveryCodeInfoAsync() => Task.FromResult<RecoveryCodeInfo?>(null);
        public Task<RegenerateRecoveryCodesResult?> RegenerateRecoveryCodesAsync() => Task.FromResult<RegenerateRecoveryCodesResult?>(null);
        public Task<List<TrustedDevice>> GetTrustedDevicesAsync() => Task.FromResult(new List<TrustedDevice>());
        public Task<bool> RevokeTrustedDeviceAsync(string deviceId) => Task.FromResult(true);
        public Task<int> RevokeAllTrustedDevicesAsync() => Task.FromResult(0);
    }

    private static async Task<TwoFactorSettingsViewModel> Render(TwoFactorConfiguration? configuration)
    {
        var component = new TwoFactorSettingsViewComponent(
            new StubTwoFactorService(configuration), NullLogger<TwoFactorSettingsViewComponent>.Instance);
        var view = Assert.IsType<ViewViewComponentResult>(await component.InvokeAsync());
        return Assert.IsType<TwoFactorSettingsViewModel>(view.ViewData!.Model);
    }

    [Fact]
    public async Task An_app_with_no_two_factor_provider_does_not_enforce_it()
    {
        Assert.True((await Render(new TwoFactorConfiguration { IsEnabled = false })).AppDoesNotEnforce);
    }

    [Fact]
    public async Task An_enforcing_app_or_an_unreadable_configuration_shows_no_notice()
    {
        Assert.False((await Render(new TwoFactorConfiguration { IsEnabled = true })).AppDoesNotEnforce);
        Assert.False((await Render(null)).AppDoesNotEnforce);
    }

    [Fact]
    public async Task The_service_reads_the_configuration_for_its_app()
    {
        var handler = new WildwoodComponents.Tests.TestHelpers.FakeHttpMessageHandler()
            .WhenOk("twofactor/configuration/app-1", "{\"isEnabled\":false,\"isRequired\":false}");
        var service = new WildwoodTwoFactorSettingsService(
            handler.CreateClient("https://api.test/api/"),
            new WildwoodComponents.Tests.TestHelpers.FakeSessionManager(null),
            NullLogger<WildwoodTwoFactorSettingsService>.Instance,
            "app-1");

        var configuration = await service.GetConfigurationAsync();

        Assert.NotNull(configuration);
        Assert.False(configuration!.IsEnabled);
    }

    [Fact]
    public async Task Without_an_app_id_the_service_reads_nothing()
    {
        var handler = new WildwoodComponents.Tests.TestHelpers.FakeHttpMessageHandler();
        var service = new WildwoodTwoFactorSettingsService(
            handler.CreateClient("https://api.test/api/"),
            new WildwoodComponents.Tests.TestHelpers.FakeSessionManager(null),
            NullLogger<WildwoodTwoFactorSettingsService>.Instance);

        Assert.Null(await service.GetConfigurationAsync());
        Assert.Empty(handler.Requests);
    }

    /// <summary>Razor's chat adopts a session id the server names whenever it differs.</summary>
    [Fact]
    public void Razor_chat_follows_the_servers_session()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && dir.GetFiles("*.slnx").Length == 0 && dir.GetFiles("*.sln").Length == 0) dir = dir.Parent;
        var source = File.ReadAllText(Path.Combine(dir!.FullName, "WildwoodComponents.Razor", "wwwroot", "js", "ai-chat.js"));

        Assert.Contains("if (result.sessionId && result.sessionId !== currentSessionId)", source);
        Assert.DoesNotContain("if (result.sessionId && !currentSessionId)", source);
    }
}
