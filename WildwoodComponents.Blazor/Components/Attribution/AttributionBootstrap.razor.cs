using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using WildwoodComponents.Blazor.Services;

namespace WildwoodComponents.Blazor.Components.Attribution
{
    /// <summary>
    /// Render-less component that starts Campaign Attribution capture. Add it once to the host layout, outside
    /// any authorization gate, so the landing URL of every entry page is read:
    /// <code>&lt;AttributionBootstrap AppId="@AppId" /&gt;</code>
    /// The registration components then attach the captured payload on their own. Does nothing when the
    /// attribution service is not registered or AppId is empty.
    /// </summary>
    public partial class AttributionBootstrap : ComponentBase
    {
        [Inject] private IServiceProvider Services { get; set; } = default!;

        /// <summary>The Wildwood app whose attribution config applies.</summary>
        [Parameter] public string AppId { get; set; } = string.Empty;

        /// <summary>Optional API base URL; defaults to the configured WildwoodComponents BaseUrl.</summary>
        [Parameter] public string? BaseUrl { get; set; }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (!firstRender || string.IsNullOrWhiteSpace(AppId)) return;

            var attribution = Services.GetService<IAttributionService>();
            if (attribution is null) return;

            // Never throws: the service degrades to null when interop is unavailable.
            await attribution.InitializeAsync(AppId, BaseUrl);
        }
    }
}
