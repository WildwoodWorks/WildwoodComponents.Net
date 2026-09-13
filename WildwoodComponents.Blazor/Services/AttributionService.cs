using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Blazor.Services
{
    /// <summary>
    /// JS-isolation shim over the browser attribution engine (mirrors ConsentService). The engine module is
    /// shared by the page, so every scope sees the same captured touches.
    /// </summary>
    public class AttributionService : IAttributionService, IAsyncDisposable
    {
        private const string ModulePath = "./_content/WildwoodComponents.Blazor/js/wildwood-attribution.js";

        private readonly IJSRuntime _js;
        private readonly ILogger<AttributionService> _logger;
        private readonly string _baseUrl;
        private IJSObjectReference? _module;

        public AttributionService(IJSRuntime js, ILogger<AttributionService> logger, string baseUrl)
        {
            _js = js;
            _logger = logger;
            _baseUrl = (baseUrl ?? string.Empty).TrimEnd('/');
        }

        private async Task<IJSObjectReference> GetModuleAsync()
        {
            return _module ??= await _js.InvokeAsync<IJSObjectReference>("import", ModulePath);
        }

        public async Task<AttributionStateModel?> InitializeAsync(string appId, string? baseUrlOverride = null)
        {
            if (string.IsNullOrWhiteSpace(appId)) return null;
            var baseUrl = string.IsNullOrEmpty(baseUrlOverride) ? _baseUrl : baseUrlOverride!.TrimEnd('/');
            return await InvokeAsync<AttributionStateModel>("initialize", baseUrl, appId);
        }

        public Task<AttributionTouchModel?> CaptureUrlAsync(string url, string? referrer = null)
            => InvokeAsync<AttributionTouchModel>("captureUrl", url, referrer);

        public Task<AttributionPayloadModel?> GetForRegistrationAsync()
            => InvokeAsync<AttributionPayloadModel>("getForRegistration");

        public async Task ClearAsync()
        {
            try
            {
                var module = await GetModuleAsync();
                await module.InvokeVoidAsync("clear");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Attribution engine call 'clear' failed");
            }
        }

        public Task<AttributionStateModel?> GetStateAsync() => InvokeAsync<AttributionStateModel>("getState");

        private async Task<T?> InvokeAsync<T>(string fn, params object?[] args) where T : class
        {
            try
            {
                var module = await GetModuleAsync();
                return await module.InvokeAsync<T?>(fn, args);
            }
            catch (Exception ex)
            {
                // Prerendering, a disconnected circuit or a blocked module: attribution is best-effort.
                _logger.LogWarning(ex, "Attribution engine call '{Fn}' failed", fn);
                return null;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_module is null) return;
            try
            {
                await _module.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
                // The circuit is already gone; nothing to release.
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Disposing the attribution module failed");
            }
            _module = null;
        }
    }
}
