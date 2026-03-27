using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using WildwoodComponents.Blazor.Models;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Blazor.Services
{
    /// <summary>
    /// Interface for captcha service operations.
    /// </summary>
    public interface ICaptchaService
    {
        Task InitializeGoogleRecaptchaAsync(string siteKey, string containerId);
        Task<string?> GetGoogleRecaptchaResponseAsync();
        Task ResetGoogleRecaptchaAsync();
        Task InitializeHCaptchaAsync(string siteKey, string containerId);
        Task<string?> GetHCaptchaResponseAsync();
        Task ResetHCaptchaAsync();
    }

    /// <summary>
    /// Service for handling captcha operations.
    /// </summary>
    public class CaptchaService : ICaptchaService
    {
        private readonly IJSRuntime _jsRuntime;
        private readonly ILogger<CaptchaService> _logger;

        public CaptchaService(IJSRuntime jsRuntime, ILogger<CaptchaService> logger)
        {
            _jsRuntime = jsRuntime;
            _logger = logger;
        }

        public async Task InitializeGoogleRecaptchaAsync(string siteKey, string containerId)
        {
            try
            {
                await _jsRuntime.InvokeVoidAsync("wildwoodCaptcha.initGoogleRecaptcha", siteKey, containerId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing Google reCAPTCHA");
            }
        }

        public async Task<string?> GetGoogleRecaptchaResponseAsync()
        {
            try
            {
                return await _jsRuntime.InvokeAsync<string>("wildwoodCaptcha.getGoogleRecaptchaResponse");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting Google reCAPTCHA response");
                return null;
            }
        }

        public async Task ResetGoogleRecaptchaAsync()
        {
            try
            {
                await _jsRuntime.InvokeVoidAsync("wildwoodCaptcha.resetGoogleRecaptcha");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resetting Google reCAPTCHA");
            }
        }

        public async Task InitializeHCaptchaAsync(string siteKey, string containerId)
        {
            try
            {
                await _jsRuntime.InvokeVoidAsync("wildwoodCaptcha.initHCaptcha", siteKey, containerId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing hCaptcha");
            }
        }

        public async Task<string?> GetHCaptchaResponseAsync()
        {
            try
            {
                return await _jsRuntime.InvokeAsync<string>("wildwoodCaptcha.getHCaptchaResponse");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting hCaptcha response");
                return null;
            }
        }

        public async Task ResetHCaptchaAsync()
        {
            try
            {
                await _jsRuntime.InvokeVoidAsync("wildwoodCaptcha.resetHCaptcha");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resetting hCaptcha");
            }
        }
    }

    /// <summary>
    /// Interface for component theme service.
    /// </summary>
    public interface IComponentThemeService
    {
        Task<ComponentTheme> GetCurrentThemeAsync();
        Task SetThemeAsync(ComponentTheme theme);
        Task ResetThemeAsync();
        event EventHandler<ComponentTheme>? ThemeChanged;
    }

    /// <summary>
    /// Service for managing component themes.
    /// </summary>
    public class ComponentThemeService : IComponentThemeService
    {
        private ComponentTheme _currentTheme = new();
        private readonly ILocalStorageService _localStorage;

        public event EventHandler<ComponentTheme>? ThemeChanged;

        public ComponentThemeService(ILocalStorageService localStorage)
        {
            _localStorage = localStorage;
        }

        public async Task<ComponentTheme> GetCurrentThemeAsync()
        {
            var savedTheme = await _localStorage.GetItemAsync<ComponentTheme>("wildwood-theme");
            return savedTheme ?? _currentTheme;
        }

        public async Task SetThemeAsync(ComponentTheme theme)
        {
            _currentTheme = theme;
            await _localStorage.SetItemAsync("wildwood-theme", theme);
            ThemeChanged?.Invoke(this, theme);
        }

        public async Task ResetThemeAsync()
        {
            _currentTheme = new ComponentTheme();
            await _localStorage.SetItemAsync("wildwood-theme", _currentTheme);
            ThemeChanged?.Invoke(this, _currentTheme);
        }
    }
}

/// <summary>
/// Extension methods for component themes.
/// </summary>
public static class ComponentThemeExtensions
{
    public static string GetThemeClass(this WildwoodComponents.Shared.Models.ComponentTheme theme)
    {
        return "wildwood-theme-custom";
    }

    public static string GetCssVariables(this WildwoodComponents.Shared.Models.ComponentTheme theme)
    {
        return $@"
            --wildwood-primary-color: {theme.PrimaryColor};
            --wildwood-secondary-color: {theme.SecondaryColor};
            --wildwood-success-color: {theme.SuccessColor};
            --wildwood-warning-color: {theme.WarningColor};
            --wildwood-danger-color: {theme.DangerColor};
            --wildwood-info-color: {theme.InfoColor};
            --wildwood-light-color: {theme.LightColor};
            --wildwood-dark-color: {theme.DarkColor};
            --wildwood-font-family: {theme.FontFamily};
            --wildwood-border-radius: {theme.BorderRadius};
            --wildwood-box-shadow: {theme.BoxShadow};
        ";
    }
}
