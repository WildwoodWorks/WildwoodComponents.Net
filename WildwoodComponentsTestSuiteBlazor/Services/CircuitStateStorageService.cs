using System.Text.Json;
using WildwoodComponents.Blazor.Services;

namespace WildwoodComponentsTestSuiteBlazor.Services;

/// <summary>
/// In-memory implementation of ILocalStorageService for Blazor Server circuits.
/// Stores data in the circuit state (per-connection) rather than browser localStorage.
/// </summary>
public class CircuitStateStorageService : ILocalStorageService
{
    private readonly Dictionary<string, string> _store = new();

    public event Action? OnChanged;

    public Task<T?> GetItemAsync<T>(string key)
    {
        if (_store.TryGetValue(key, out var value))
        {
            if (typeof(T) == typeof(string))
            {
                return Task.FromResult((T?)(object?)value);
            }
            try
            {
                var result = JsonSerializer.Deserialize<T>(value);
                return Task.FromResult(result);
            }
            catch
            {
                return Task.FromResult(default(T));
            }
        }
        return Task.FromResult(default(T));
    }

    public Task SetItemAsync<T>(string key, T value)
    {
        if (value is string strValue)
        {
            _store[key] = strValue;
        }
        else
        {
            _store[key] = JsonSerializer.Serialize(value);
        }
        OnChanged?.Invoke();
        return Task.CompletedTask;
    }

    public Task RemoveItemAsync(string key)
    {
        _store.Remove(key);
        OnChanged?.Invoke();
        return Task.CompletedTask;
    }

    public Task ClearAsync()
    {
        _store.Clear();
        OnChanged?.Invoke();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Convenience string-based GetItemAsync for callers that work with raw strings.
    /// </summary>
    public Task<string?> GetItemAsync(string key)
    {
        _store.TryGetValue(key, out var value);
        return Task.FromResult(value);
    }

    /// <summary>
    /// Convenience string-based SetItemAsync for callers that work with raw strings.
    /// </summary>
    public Task SetItemAsync(string key, string value)
    {
        _store[key] = value;
        OnChanged?.Invoke();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Convenience method to store the JWT token in the standard key used by WildwoodComponents.
    /// </summary>
    public async Task SetAuthTokenAsync(string token)
    {
        await SetItemAsync("wildwood_auth_token", token);
    }

    /// <summary>
    /// Convenience method to get the JWT token from the standard key.
    /// </summary>
    public async Task<string?> GetAuthTokenAsync()
    {
        return await GetItemAsync<string>("wildwood_auth_token");
    }
}
