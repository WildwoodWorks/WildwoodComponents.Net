# WildwoodComponents.Net

## Overview

.NET component library providing authentication, AI, messaging, payments, subscriptions, notifications, and more for both Blazor and Razor Pages applications. All components talk to the same WildwoodAPI backend as the JS sibling project (WildwoodComponents.JS).

Published as three NuGet packages: **WildwoodComponents.Blazor**, **WildwoodComponents.Razor**, **WildwoodComponents.Shared**.

## Solution Structure

Solution file: `WildwoodComponents.Net.slnx`

| Project | Purpose |
|---------|---------|
| **WildwoodComponents.Shared** | Internal shared library — models, DTOs, utilities. Consumed by Blazor and Razor. |
| **WildwoodComponents.Blazor** | Blazor interactive components (`.razor` + `.razor.cs`) |
| **WildwoodComponents.Razor** | Razor Pages ViewComponents (`.cs` + `.cshtml` + `.js`) |
| **WildwoodComponentsTestSuiteBlazor** | Blazor test harness app with test pages for each component |

### Dependency Graph

```
WildwoodComponents.Shared
  ├──► WildwoodComponents.Blazor
  └──► WildwoodComponents.Razor
         WildwoodComponentsTestSuiteBlazor ──► WildwoodComponents.Blazor
```

## Build & Run

```bash
cd C:\Development\WildwoodComponents.Net\Dev
dotnet build                                                # Build all projects
dotnet run --project WildwoodComponentsTestSuiteBlazor      # Run test suite
```

## WildwoodComponents.Shared

Internal shared library — not intended for direct consumption by end users.

- **Models/**: `AppTierModels`, `WildwoodAuthModels`, `WildwoodAIModels`, `PaymentProviderModels`, `DisclaimerModels`, `FlowModels`, `TwoFactorSettingsModels`
- **Utilities/**: `FormatHelpers`, `TokenExpiryParser`, `SessionConstants`

## WildwoodComponents.Blazor

### File Structure

```
WildwoodComponents.Blazor/
    Components/
        AI/                  # AIChatComponent, AIFlowComponent, AIProxyComponent
        AppTier/             # AppTierComponent
        Authentication/      # AuthenticationComponent
        Base/                # BaseWildwoodComponent (all components inherit from this)
        Disclaimer/          # DisclaimerComponent
        Messaging/           # SecureMessagingComponent
        Notifications/       # NotificationComponent, NotificationToastComponent
        Payment/             # PaymentComponent, PaymentFormComponent
        Pricing/             # PricingDisplayComponent
        Registration/        # TokenRegistrationComponent, SignupWithSubscriptionComponent
        Security/            # TwoFactorSettingsComponent
        Subscription/        # SubscriptionComponent, SubscriptionManagerComponent, SubscriptionAdminComponent
        Usage/               # UsageDashboardComponent, OverageSummaryComponent
    Services/                # AuthenticationService, AIService, PaymentService, etc.
    Extensions/              # ServiceCollectionExtensions (AddWildwoodComponents)
    Scripts/                 # JS interop scripts
    wwwroot/css/             # wildwood-themes.css (--ww-* CSS variables)
```

### Key Patterns

- **BaseWildwoodComponent**: All components inherit from this. Provides automatic error handling, loading state management, theme management, and safe JS interop.
- **Code-behind separation**: `.razor` files contain markup only (no `@code` blocks). All C# logic goes in `.razor.cs` code-behind files.
- **Partial class rule**: Files exceeding 900 lines must be split into partial classes by responsibility (e.g., `PaymentComponent.Validation.cs`, `PaymentComponent.EventHandlers.cs`).
- **No LINQ**: Never use LINQ methods — they cause iOS/MAUI runtime failures. Use `foreach` loops instead.

### Service Registration

```csharp
builder.Services.AddWildwoodComponents(options =>
{
    options.BaseUrl = "https://api.example.com";
    options.ApiKey = "your-api-key";
});
```

## WildwoodComponents.Razor

### File Structure

```
WildwoodComponents.Razor/
    Components/
        AIChat/              # AIChatViewComponent
        AIFlow/              # AIFlowViewComponent
        AIProxy/             # AIProxyViewComponent
        AppTier/             # AppTierViewComponent, PricingDisplayViewComponent
        Authentication/      # AuthenticationViewComponent
        Disclaimer/          # DisclaimerViewComponent
        Messaging/           # SecureMessagingViewComponent
        Notification/        # NotificationViewComponent, NotificationToastViewComponent
        Payment/             # PaymentViewComponent, PaymentFormViewComponent
        Registration/        # TokenRegistrationViewComponent, SignupWithSubscriptionViewComponent
        Security/            # TwoFactorSettingsViewComponent
        Subscription/        # SubscriptionViewComponent, SubscriptionManagerViewComponent
        Usage/               # UsageDashboardViewComponent, OverageSummaryViewComponent
    Services/                # Server-side HTTP services (IHttpClientFactory, named client "WildwoodAPI")
    Models/                  # Razor-specific request/response models
    Extensions/              # ServiceCollectionExtensions (AddWildwoodComponentsRazor)
    Middleware/              # Cookie auth helpers
    Helpers/                 # Auth helpers
    Authentication/          # Auth infrastructure
    wwwroot/css/             # wildwood-razor-themes.css
```

### ViewComponent Architecture

Each component consists of:
- `.cs` class — server-side logic (ViewComponent)
- `Views/Default.cshtml` — Razor markup
- `.js` file — client-side interactivity (AJAX, state)
- `.css` file — styles using `--ww-*` variables

### Key Differences from Blazor

| Aspect | Blazor | Razor |
|--------|--------|-------|
| Rendering | Blazor component tree | Server-rendered HTML + JS |
| Interactivity | C# event handlers + SignalR | JavaScript AJAX calls |
| Token storage | Browser localStorage | Server-side session |
| DI approach | Reflection-based registration | Named HttpClient factory |

### Service Registration

```csharp
builder.Services.AddWildwoodComponentsRazor(options =>
{
    options.BaseUrl = "https://api.example.com";
    options.ApiKey = "your-api-key";
    options.AppId = "your-app-id";
});
```

### Usage in Razor Pages

```html
<vc:authentication app-id="my-app" />
```

## CSS Theming

Both Blazor and Razor use the same `--ww-*` CSS custom property system. A consuming app's theme override works for both libraries.

- Always use `--ww-*` variables, never hardcode colors
- Always provide fallback values: `var(--ww-primary, #0d6efd)`
- Three built-in themes: `woodland-warm` (default), `cool-blue`, `fall-colors`
- Activated via `data-theme` attribute on `<body>`

## Feature Parity

This project is one half of the WildwoodComponents ecosystem. The JS sibling (`C:\Development\WildwoodComponents.JS\Dev`) implements the same components for React, React Native, and Node.js. Both projects call the same WildwoodAPI backend.

When a component is added or modified in either project, the other must be updated to match. The sync workspace (`C:\Development\WildwoodComponents.Sync`) coordinates cross-project parity.

## Related Projects

| Project | Path |
|---------|------|
| WildwoodComponents.JS | `C:\Development\WildwoodComponents.JS\Dev` |
| WildwoodComponents.Sync | `C:\Development\WildwoodComponents.Sync` |
| WildwoodAPI (backend) | `C:\Development\WildwoodAPI\Dev\WildwoodAPI` |
