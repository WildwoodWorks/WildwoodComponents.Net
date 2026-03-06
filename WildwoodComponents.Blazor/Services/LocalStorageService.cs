using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace WildwoodComponents.Blazor.Services
{
    public class LocalStorageService : ILocalStorageService
    {
        private readonly IJSRuntime _jsRuntime;
        private readonly ILogger<LocalStorageService> _logger;
        private bool _isJsReady;

        public LocalStorageService(IJSRuntime jsRuntime, ILogger<LocalStorageService> logger)
        {
            _jsRuntime = jsRuntime;
            _logger = logger;
        }

        private async Task<bool> EnsureJsReadyAsync()
        {
            if (_isJsReady)
                return true;

            try
            {
                // Try a simple JS call to verify interop is ready
                await _jsRuntime.InvokeAsync<string>("eval", "''");
                _isJsReady = true;
                return true;
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("prerender") || ex.Message.Contains("JavaScript interop"))
            {
                _logger.LogDebug("JavaScript interop not yet available");
                return false;
            }
            catch (JSDisconnectedException)
            {
                _logger.LogDebug("JavaScript interop disconnected");
                return false;
            }
        }

        public async Task SetItemAsync<T>(string key, T value)
        {
            try
            {
                if (!await EnsureJsReadyAsync())
                {
                    _logger.LogWarning("Cannot set localStorage item {Key} - JS interop not ready", key);
                    return;
                }

                var json = System.Text.Json.JsonSerializer.Serialize(value);
                _logger.LogDebug("Setting localStorage item {Key}: {ValuePreview}", key, 
                    json.Length > 100 ? json[..100] + "..." : json);
                await _jsRuntime.InvokeVoidAsync("localStorage.setItem", key, json);
            }
            catch (JSDisconnectedException)
            {
                _logger.LogDebug("Cannot set localStorage - circuit disconnected");
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("prerender"))
            {
                _logger.LogDebug("Cannot access localStorage during prerender for key {Key}", key);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting localStorage item {Key}", key);
            }
        }

        public async Task<T?> GetItemAsync<T>(string key)
        {
            try
            {
                if (!await EnsureJsReadyAsync())
                {
                    _logger.LogWarning("Cannot get localStorage item {Key} - JS interop not ready", key);
                    return default;
                }

                var json = await _jsRuntime.InvokeAsync<string>("localStorage.getItem", key);
                
                if (string.IsNullOrEmpty(json))
                {
                    _logger.LogDebug("localStorage item {Key} not found or empty", key);
                    return default;
                }

                _logger.LogDebug("Retrieved localStorage item {Key}: {ValuePreview}", key, 
                    json.Length > 100 ? json[..100] + "..." : json);
                return System.Text.Json.JsonSerializer.Deserialize<T>(json);
            }
            catch (JSDisconnectedException)
            {
                _logger.LogDebug("Cannot get localStorage - circuit disconnected");
                return default;
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("prerender") || ex.Message.Contains("JavaScript interop"))
            {
                _logger.LogDebug("Cannot access localStorage during prerender for key {Key}", key);
                return default;
            }
            catch (System.Text.Json.JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to deserialize localStorage item {Key}, clearing invalid data", key);
                await RemoveItemAsync(key);
                return default;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting localStorage item {Key}", key);
                return default;
            }
        }

        public async Task RemoveItemAsync(string key)
        {
            try
            {
                if (!await EnsureJsReadyAsync())
                    return;

                _logger.LogDebug("Removing localStorage item {Key}", key);
                await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", key);
            }
            catch (JSDisconnectedException)
            {
                _logger.LogDebug("Cannot remove localStorage - circuit disconnected");
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("prerender"))
            {
                _logger.LogDebug("Cannot access localStorage during prerender for key {Key}", key);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing localStorage item {Key}", key);
            }
        }

        public async Task ClearAsync()
        {
            try
            {
                if (!await EnsureJsReadyAsync())
                    return;

                _logger.LogDebug("Clearing all localStorage items");
                await _jsRuntime.InvokeVoidAsync("localStorage.clear");
            }
            catch (JSDisconnectedException)
            {
                _logger.LogDebug("Cannot clear localStorage - circuit disconnected");
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("prerender"))
            {
                _logger.LogDebug("Cannot clear localStorage during prerender");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error clearing localStorage");
            }
        }
    }
}
