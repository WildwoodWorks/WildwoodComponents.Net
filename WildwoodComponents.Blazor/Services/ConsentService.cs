using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Blazor.Services
{
    /// <summary>
    /// Blazor consent service. Loads the consent engine via JS isolation and forwards calls to it.
    /// The engine (wwwroot/js/wildwood-consent.js) owns cookie/GPC/decision/injection.
    /// </summary>
    public class ConsentService : IConsentService, IAsyncDisposable
    {
        private const string ModulePath = "./_content/WildwoodComponents.Blazor/js/wildwood-consent.js";

        private readonly IJSRuntime _js;
        private readonly ILogger<ConsentService> _logger;
        private readonly string _baseUrl;
        private IJSObjectReference? _module;

        public ConsentService(IJSRuntime js, ILogger<ConsentService> logger, string baseUrl)
        {
            _js = js;
            _logger = logger;
            _baseUrl = (baseUrl ?? string.Empty).TrimEnd('/');
        }

        private async Task<IJSObjectReference> GetModuleAsync()
        {
            return _module ??= await _js.InvokeAsync<IJSObjectReference>("import", ModulePath);
        }

        public async Task<ConsentInitResult> InitializeAsync(string appId, string? baseUrlOverride = null)
        {
            try
            {
                var module = await GetModuleAsync();
                var baseUrl = string.IsNullOrEmpty(baseUrlOverride) ? _baseUrl : baseUrlOverride!.TrimEnd('/');
                return await module.InvokeAsync<ConsentInitResult>("initialize", baseUrl, appId, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize consent for app {AppId}", appId);
                return new ConsentInitResult { ShouldShowBanner = false };
            }
        }

        public async Task<ConsentStateModel?> AcceptAllAsync() => await InvokeStateAsync("acceptAll");

        public async Task<ConsentStateModel?> RejectAllAsync() => await InvokeStateAsync("rejectAll");

        public async Task<ConsentStateModel?> SetCategoriesAsync(Dictionary<string, bool> selection)
        {
            try
            {
                var module = await GetModuleAsync();
                return await module.InvokeAsync<ConsentStateModel>("setCategories", selection);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to set consent categories");
                return null;
            }
        }

        public async Task<ConsentStateModel?> WithdrawAsync() => await InvokeStateAsync("withdraw");

        public async Task TrapFocusAsync(Microsoft.AspNetCore.Components.ElementReference element)
        {
            try
            {
                var module = await GetModuleAsync();
                await module.InvokeVoidAsync("trapFocus", element);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Consent focus trap failed");
            }
        }

        public async Task ReleaseFocusAsync()
        {
            try
            {
                var module = await GetModuleAsync();
                await module.InvokeVoidAsync("releaseFocus");
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Consent focus release failed");
            }
        }

        private async Task<ConsentStateModel?> InvokeStateAsync(string fn)
        {
            try
            {
                var module = await GetModuleAsync();
                return await module.InvokeAsync<ConsentStateModel>(fn);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Consent engine call '{Fn}' failed", fn);
                return null;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_module is not null)
            {
                try { await _module.DisposeAsync(); }
                catch (JSDisconnectedException) { /* circuit gone */ }
            }
        }
    }
}
