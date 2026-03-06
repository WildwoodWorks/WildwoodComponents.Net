using System.Reflection;
using WildwoodComponents.Blazor.Models;

namespace WildwoodComponents.Blazor.Services.Payment;

/// <summary>
/// Base class for payment script providers that handles embedded resource reading.
/// </summary>
public abstract class PaymentScriptProviderBase : IPaymentScriptProvider
{
    private string? _cachedScriptContent;

    /// <inheritdoc />
    public abstract PaymentProviderType ProviderType { get; }

    /// <inheritdoc />
    public virtual string ScriptElementId => $"ww-payment-{ProviderType.ToString().ToLowerInvariant()}";

    /// <summary>
    /// Gets the embedded resource name for the script file.
    /// Override to specify a different resource name.
    /// </summary>
    protected abstract string EmbeddedResourceName { get; }

    /// <summary>
    /// Gets the assembly containing the embedded resource.
    /// Override if the resource is in a different assembly.
    /// </summary>
    protected virtual Assembly ResourceAssembly => typeof(PaymentScriptProviderBase).Assembly;

    /// <inheritdoc />
    public virtual IReadOnlyList<string> GetExternalScriptUrls()
    {
        // Override in derived classes to return CDN URLs
        return Array.Empty<string>();
    }

    /// <inheritdoc />
    public string GetScriptContent()
    {
        // Return cached content if available
        if (_cachedScriptContent != null)
        {
            return _cachedScriptContent;
        }

        try
        {
            _cachedScriptContent = ReadEmbeddedResource();
            return _cachedScriptContent ?? string.Empty;
        }
        catch (Exception ex)
        {
            // Log error but don't throw - return empty string
            System.Diagnostics.Debug.WriteLine($"Failed to read embedded resource {EmbeddedResourceName}: {ex.Message}");
            return string.Empty;
        }
    }

    /// <summary>
    /// Reads the embedded resource content as a string.
    /// </summary>
    protected virtual string? ReadEmbeddedResource()
    {
        var assembly = ResourceAssembly;
        var resourceName = EmbeddedResourceName;

        // Try to find the resource with the exact name first
        using var stream = assembly.GetManifestResourceStream(resourceName);
        
        if (stream == null)
        {
            // Try to find with assembly namespace prefix
            var assemblyName = assembly.GetName().Name;
            var alternativeResourceName = $"{assemblyName}.{resourceName}";
            
            using var altStream = assembly.GetManifestResourceStream(alternativeResourceName);
            if (altStream == null)
            {
                // List available resources for debugging
                var availableResources = assembly.GetManifestResourceNames();
                System.Diagnostics.Debug.WriteLine($"Available resources: {string.Join(", ", availableResources)}");
                System.Diagnostics.Debug.WriteLine($"Could not find resource: {resourceName} or {alternativeResourceName}");
                return null;
            }
            
            using var altReader = new StreamReader(altStream);
            return altReader.ReadToEnd();
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Gets all available embedded resource names in the assembly.
    /// Useful for debugging resource loading issues.
    /// </summary>
    protected string[] GetAvailableResourceNames()
    {
        return ResourceAssembly.GetManifestResourceNames();
    }
}
