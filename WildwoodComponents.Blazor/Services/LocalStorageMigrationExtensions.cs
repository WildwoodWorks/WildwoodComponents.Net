namespace WildwoodComponents.Blazor.Services
{
    /// <summary>
    /// One-time migration helpers for localStorage keys renamed to the <c>ww_</c> prefix.
    /// Lets an SDK upgrade pick up data stored under the legacy key without losing it.
    /// </summary>
    public static class LocalStorageMigrationExtensions
    {
        /// <summary>
        /// Reads <paramref name="key"/>; if empty, falls back to <paramref name="legacyKey"/>
        /// and, when found, migrates the value to the new key (and removes the legacy one).
        /// </summary>
        public static async Task<T?> GetItemWithMigrationAsync<T>(
            this ILocalStorageService storage, string key, string legacyKey)
        {
            var value = await storage.GetItemAsync<T>(key);
            if (value is not null)
            {
                return value;
            }

            var legacy = await storage.GetItemAsync<T>(legacyKey);
            if (legacy is not null)
            {
                await storage.SetItemAsync(key, legacy);
                await storage.RemoveItemAsync(legacyKey);
            }

            return legacy;
        }
    }
}
