using System.Text.Json;
using WildwoodComponents.Blazor.Services;

namespace WildwoodComponents.Tests.TestHelpers;

/// <summary>
/// In-memory <see cref="ILocalStorageService"/> for Blazor service tests.
/// Values are JSON round-tripped exactly as <c>LocalStorageService</c> does — a plain
/// <see cref="JsonSerializer"/> call with no options — so a test observes the same shape the
/// browser would hold, including the <c>[JsonPropertyName]</c> overrides on the models.
/// </summary>
public class FakeLocalStorageService : ILocalStorageService
{
    private readonly Dictionary<string, string> _items = new(StringComparer.Ordinal);

    /// <summary>The raw JSON currently stored, keyed as the browser would key it.</summary>
    public IReadOnlyDictionary<string, string> Items => _items;

    public Task SetItemAsync<T>(string key, T value)
    {
        _items[key] = JsonSerializer.Serialize(value);
        return Task.CompletedTask;
    }

    public Task<T?> GetItemAsync<T>(string key)
    {
        if (!_items.TryGetValue(key, out var json) || string.IsNullOrEmpty(json))
        {
            return Task.FromResult<T?>(default);
        }

        return Task.FromResult(JsonSerializer.Deserialize<T>(json));
    }

    public Task RemoveItemAsync(string key)
    {
        _items.Remove(key);
        return Task.CompletedTask;
    }

    public Task ClearAsync()
    {
        _items.Clear();
        return Task.CompletedTask;
    }
}
