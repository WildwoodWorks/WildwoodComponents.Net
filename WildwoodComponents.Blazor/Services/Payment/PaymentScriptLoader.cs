using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using WildwoodComponents.Blazor.Models;

namespace WildwoodComponents.Blazor.Services.Payment;

/// <summary>
/// Service responsible for dynamically loading and unloading payment provider scripts.
/// Uses reference counting to support multiple components using the same provider scripts concurrently.
/// </summary>
public class PaymentScriptLoader : IAsyncDisposable
{
    private readonly IJSRuntime _jsRuntime;
    private readonly ILogger<PaymentScriptLoader>? _logger;
    private readonly Dictionary<PaymentProviderType, IPaymentScriptProvider> _providers;
    private readonly Dictionary<PaymentProviderType, int> _referenceCount = new();
    private readonly HashSet<PaymentProviderType> _loadedScripts = new();
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private bool _baseScriptLoaded;

    /// <summary>
    /// JavaScript to inject a script element into the DOM.
    /// </summary>
    private const string InjectScriptJs = @"
        (function(scriptId, scriptContent) {
            if (document.getElementById(scriptId)) {
                return true; // Already loaded
            }
            const script = document.createElement('script');
            script.id = scriptId;
            script.type = 'text/javascript';
            script.textContent = scriptContent;
            document.head.appendChild(script);
            return true;
        })";

    /// <summary>
    /// JavaScript to inject an external script (CDN) into the DOM.
    /// First checks if the script is already available globally (e.g., Stripe).
    /// </summary>
    private const string InjectExternalScriptJs = @"
        (function(scriptId, scriptUrl) {
            return new Promise((resolve, reject) => {
                // Check if already loaded by ID
                if (document.getElementById(scriptId)) {
                    console.log('Script already loaded by ID:', scriptId);
                    resolve(true);
                    return;
                }
                
                // Check if Stripe is already globally available (loaded via static script tag)
                if (scriptUrl.includes('stripe.com') && typeof Stripe !== 'undefined') {
                    console.log('Stripe.js already available globally');
                    resolve(true);
                    return;
                }
                
                // Check if PayPal is already globally available
                if (scriptUrl.includes('paypal.com') && typeof paypal !== 'undefined') {
                    console.log('PayPal.js already available globally');
                    resolve(true);
                    return;
                }
                
                console.log('Loading external script:', scriptUrl);
                const script = document.createElement('script');
                script.id = scriptId;
                script.type = 'text/javascript';
                script.src = scriptUrl;
                script.async = true;
                script.onload = () => {
                    console.log('External script loaded:', scriptUrl);
                    resolve(true);
                };
                script.onerror = (e) => {
                    console.error('Failed to load external script:', scriptUrl, e);
                    reject(new Error('Failed to load: ' + scriptUrl));
                };
                document.head.appendChild(script);
            });
        })";

    /// <summary>
    /// JavaScript to remove a script element from the DOM.
    /// </summary>
    private const string RemoveScriptJs = @"
        (function(scriptId) {
            const script = document.getElementById(scriptId);
            if (script) {
                script.remove();
                return true;
            }
            return false;
        })";

    public PaymentScriptLoader(
        IJSRuntime jsRuntime,
        IEnumerable<IPaymentScriptProvider> providers,
        ILogger<PaymentScriptLoader>? logger = null)
    {
        _jsRuntime = jsRuntime;
        _logger = logger;
        _providers = new Dictionary<PaymentProviderType, IPaymentScriptProvider>();
        
        foreach (var provider in providers)
        {
            _providers[provider.ProviderType] = provider;
        }
    }

    /// <summary>
    /// Loads the script for the specified payment provider if not already loaded.
    /// Increments reference count for the provider.
    /// </summary>
    /// <param name="providerType">The payment provider type to load.</param>
    /// <returns>True if the script is loaded and ready, false otherwise.</returns>
    public async Task<bool> LoadAsync(PaymentProviderType providerType)
    {
        await _loadLock.WaitAsync();
        try
        {
            // Increment reference count
            if (!_referenceCount.TryGetValue(providerType, out var count))
            {
                count = 0;
            }
            _referenceCount[providerType] = count + 1;

            // If already loaded, just return
            if (_loadedScripts.Contains(providerType))
            {
                _logger?.LogDebug("Script for {Provider} already loaded, ref count now {Count}", 
                    providerType, _referenceCount[providerType]);
                return true;
            }

            // Check if we have a provider for this type
            if (!_providers.TryGetValue(providerType, out var provider))
            {
                _logger?.LogWarning("No script provider registered for {Provider}", providerType);
                return false;
            }

            // Load base script first if not loaded
            if (!_baseScriptLoaded)
            {
                await LoadBaseScriptAsync();
                _baseScriptLoaded = true;
            }

            // Load external scripts (CDN) first
            var externalUrls = provider.GetExternalScriptUrls();
            for (int i = 0; i < externalUrls.Count; i++)
            {
                var url = externalUrls[i];
                var externalScriptId = $"ww-payment-external-{providerType.ToString().ToLowerInvariant()}-{i}";
                try
                {
                    await _jsRuntime.InvokeAsync<bool>("eval", $"({InjectExternalScriptJs})('{externalScriptId}', '{url}')");
                    _logger?.LogDebug("Loaded external script for {Provider}: {Url}", providerType, url);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to load external script for {Provider}: {Url}", providerType, url);
                    return false;
                }
            }

            // Load the embedded provider script
            var scriptContent = provider.GetScriptContent();
            if (string.IsNullOrEmpty(scriptContent))
            {
                _logger?.LogWarning("Empty script content for {Provider}", providerType);
                return false;
            }

            try
            {
                // Escape the script content for JavaScript string
                var escapedContent = EscapeForJavaScript(scriptContent);
                await _jsRuntime.InvokeAsync<bool>("eval", $"({InjectScriptJs})('{provider.ScriptElementId}', {escapedContent})");
                
                _loadedScripts.Add(providerType);
                _logger?.LogInformation("Loaded payment script for {Provider}", providerType);
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to inject script for {Provider}", providerType);
                return false;
            }
        }
        finally
        {
            _loadLock.Release();
        }
    }

    /// <summary>
    /// Releases a reference to the specified payment provider script.
    /// Removes the script from DOM when reference count reaches zero.
    /// </summary>
    /// <param name="providerType">The payment provider type to release.</param>
    public async Task ReleaseAsync(PaymentProviderType providerType)
    {
        await _loadLock.WaitAsync();
        try
        {
            if (!_referenceCount.TryGetValue(providerType, out var count) || count <= 0)
            {
                return;
            }

            count--;
            _referenceCount[providerType] = count;

            _logger?.LogDebug("Released reference to {Provider}, ref count now {Count}", providerType, count);

            // Only remove script when no more references
            if (count <= 0 && _loadedScripts.Contains(providerType))
            {
                if (_providers.TryGetValue(providerType, out var provider))
                {
                    try
                    {
                        await _jsRuntime.InvokeAsync<bool>("eval", $"({RemoveScriptJs})('{provider.ScriptElementId}')");
                        
                        // Also remove external scripts
                        var externalUrls = provider.GetExternalScriptUrls();
                        for (int i = 0; i < externalUrls.Count; i++)
                        {
                            var externalScriptId = $"ww-payment-external-{providerType.ToString().ToLowerInvariant()}-{i}";
                            await _jsRuntime.InvokeAsync<bool>("eval", $"({RemoveScriptJs})('{externalScriptId}')");
                        }

                        _logger?.LogInformation("Removed payment script for {Provider}", providerType);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "Failed to remove script for {Provider}", providerType);
                    }
                }

                _loadedScripts.Remove(providerType);
                _referenceCount.Remove(providerType);
            }
        }
        finally
        {
            _loadLock.Release();
        }
    }

    /// <summary>
    /// Loads scripts for multiple payment providers.
    /// </summary>
    /// <param name="providerTypes">The provider types to load.</param>
    /// <returns>Dictionary of provider types and their load success status.</returns>
    public async Task<Dictionary<PaymentProviderType, bool>> LoadMultipleAsync(IEnumerable<PaymentProviderType> providerTypes)
    {
        var results = new Dictionary<PaymentProviderType, bool>();
        foreach (var providerType in providerTypes)
        {
            results[providerType] = await LoadAsync(providerType);
        }
        return results;
    }

    /// <summary>
    /// Releases references to multiple payment provider scripts.
    /// </summary>
    /// <param name="providerTypes">The provider types to release.</param>
    public async Task ReleaseMultipleAsync(IEnumerable<PaymentProviderType> providerTypes)
    {
        foreach (var providerType in providerTypes)
        {
            await ReleaseAsync(providerType);
        }
    }

    /// <summary>
    /// Checks if a script for the specified provider is currently loaded.
    /// </summary>
    public bool IsLoaded(PaymentProviderType providerType)
    {
        return _loadedScripts.Contains(providerType);
    }

    /// <summary>
    /// Gets the current reference count for a provider.
    /// </summary>
    public int GetReferenceCount(PaymentProviderType providerType)
    {
        return _referenceCount.TryGetValue(providerType, out var count) ? count : 0;
    }

    private async Task LoadBaseScriptAsync()
    {
        const string baseScriptId = "ww-payment-base";
        
        // Load the full PaymentScriptBase.js from embedded resources
        var baseScriptContent = ReadEmbeddedResource("WildwoodComponents.Blazor.Scripts.PaymentScriptBase.js");
        
        if (string.IsNullOrEmpty(baseScriptContent))
        {
            _logger?.LogError("Failed to read PaymentScriptBase.js from embedded resources");
            // Fall back to minimal initialization
            baseScriptContent = @"
                window.wildwoodPayment = window.wildwoodPayment || {};
                window.wildwoodPayment._initialized = true;
                window.wildwoodPayment._loadedProviders = window.wildwoodPayment._loadedProviders || {};
                window.wildwoodPayment._dotNetRefs = window.wildwoodPayment._dotNetRefs || {};
                window.wildwoodPayment.storeDotNetRef = function(key, ref) { this._dotNetRefs[key] = ref; };
                window.wildwoodPayment.getDotNetRef = function(key) { return this._dotNetRefs[key] || null; };
                window.wildwoodPayment.removeDotNetRef = function(key) { delete this._dotNetRefs[key]; };
                window.wildwoodPayment.registerProvider = function(name) { this._loadedProviders[name] = true; };
                window.wildwoodPayment.invokeDotNet = async function(key, method, ...args) {
                    var ref = this.getDotNetRef(key);
                    if (ref) { try { return await ref.invokeMethodAsync(method, ...args); } catch(e) { console.warn('invokeDotNet failed:', e); } }
                    return null;
                };
                console.log('WildwoodPayment: Base script initialized (fallback)');
            ";
        }

        try
        {
            var escapedContent = EscapeForJavaScript(baseScriptContent);
            await _jsRuntime.InvokeAsync<bool>("eval", $"({InjectScriptJs})('{baseScriptId}', {escapedContent})");
            _logger?.LogDebug("Base payment script loaded");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to load base payment script");
        }
    }

    /// <summary>
    /// Reads an embedded resource from the assembly.
    /// </summary>
    private string? ReadEmbeddedResource(string resourceName)
    {
        var assembly = typeof(PaymentScriptLoader).Assembly;
        
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            // Try alternative resource name formats
            var availableResources = assembly.GetManifestResourceNames();
            _logger?.LogDebug("Available resources: {Resources}", string.Join(", ", availableResources));
            
            // Look for a matching resource
            var matchingResource = availableResources.FirstOrDefault(r => r.EndsWith("PaymentScriptBase.js"));
            if (matchingResource != null)
            {
                using var altStream = assembly.GetManifestResourceStream(matchingResource);
                if (altStream != null)
                {
                    using var altReader = new StreamReader(altStream);
                    return altReader.ReadToEnd();
                }
            }
            
            return null;
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string EscapeForJavaScript(string content)
    {
        // Use template literal (backticks) for multi-line strings
        // Escape backticks and ${} in the content
        var escaped = content
            .Replace("\\", "\\\\")
            .Replace("`", "\\`")
            .Replace("${", "\\${");
        return $"`{escaped}`";
    }

    public async ValueTask DisposeAsync()
    {
        // Release all loaded scripts
        var loadedTypes = _loadedScripts.ToList();
        foreach (var providerType in loadedTypes)
        {
            // Force remove by setting ref count to 1
            _referenceCount[providerType] = 1;
            await ReleaseAsync(providerType);
        }

        _loadLock.Dispose();
    }
}
