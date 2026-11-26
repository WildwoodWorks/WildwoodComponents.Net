using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace WildwoodComponents.Blazor.Services
{
    /// <summary>
    /// Loads the library's webauthn.js (which defines window.webauthnRegister / webauthnAuthenticate /
    /// webauthnIsSupported) on demand. Hosts were expected to add a script tag for it and most never did,
    /// so the passkey button silently never appeared. A dynamic import runs the script once per page
    /// (the browser caches the module by URL); a host that also loaded it classically is unaffected.
    /// </summary>
    public static class WebAuthnScript
    {
        public const string RelativePath = "_content/WildwoodComponents.Blazor/js/webauthn.js";

        /// <returns>False when the script could not be loaded (the caller treats WebAuthn as unsupported).</returns>
        public static async Task<bool> EnsureLoadedAsync(IJSRuntime js, NavigationManager navigation)
        {
            try
            {
                // Resolve against the app's base URI so a host under a path base still finds it.
                var url = new Uri(new Uri(navigation.BaseUri), RelativePath).ToString();
                await js.InvokeAsync<IJSObjectReference>("import", url);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
