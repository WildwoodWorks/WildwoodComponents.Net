# WildwoodComponents.Razor Claude Instructions

## Project Overview

WildwoodComponents.Razor is a **Razor Pages-native component library** providing ViewComponents for authentication, AI proxy, and more. It is the **Razor Pages sibling** of the Blazor-based **WildwoodComponents** library.

Both libraries expose the same features using their respective rendering models:
- **WildwoodComponents** → Blazor components (`.razor` + `.razor.cs`)
- **WildwoodComponents.Razor** → ASP.NET Core ViewComponents (`.cs` + `.cshtml` + `.js`)

## CRITICAL: Feature Parity Across All Platforms

WildwoodComponents exists across multiple platforms. **When a component is added or modified in ANY platform, all other platforms MUST be updated to match.**

### Platform Libraries
- **WildwoodComponents (Blazor)** - Blazor components (`../WildwoodComponents/`)
- **WildwoodComponents.Razor** - This project
- **@wildwood/core** - Pure TypeScript SDK (Wildwood.JS repo: `packages/wildwood-core/`)
- **@wildwood/react** - React hooks + components (Wildwood.JS repo: `packages/wildwood-react/`)
- **@wildwood/react-native** - React Native components (Wildwood.JS repo: `packages/wildwood-react-native/`)
- **@wildwood/node** - Node.js/Express middleware (Wildwood.JS repo: `packages/wildwood-node/`)

### Feature Parity Checklist

| Component | Blazor | Razor | @wildwood/core | @wildwood/react | @wildwood/react-native | @wildwood/node |
|-----------|--------|-------|----------------|-----------------|------------------------|----------------|
| Authentication | AuthenticationComponent | AuthenticationViewComponent | authService | AuthenticationComponent | AuthenticationComponent | authMiddleware |
| AI Chat | AIChatComponent | AIChatViewComponent | aiService | AIChatComponent | -- | -- |
| AI Flow | AIFlowComponent | AIFlowViewComponent | (via aiService) | -- | -- | -- |
| AI Proxy | AIProxyComponent | AIProxyViewComponent | (via aiService) | AIProxyComponent | -- | -- |
| Messaging | SecureMessagingComponent | SecureMessagingViewComponent | messagingService | SecureMessagingComponent | -- | -- |
| Payment | PaymentComponent | PaymentViewComponent | paymentService | PaymentComponent | -- | -- |
| Payment Form | PaymentFormComponent | PaymentFormViewComponent | (via paymentService) | PaymentFormComponent | -- | -- |
| Subscription | SubscriptionComponent | SubscriptionViewComponent | subscriptionService | SubscriptionComponent | -- | -- |
| Subscription Mgr | SubscriptionManagerComponent | SubscriptionManagerViewComponent | (via subscriptionService) | SubscriptionManagerComponent | -- | -- |
| Notifications | NotificationComponent | NotificationViewComponent | notificationService | NotificationComponent | -- | -- |
| Notification Toast | NotificationToastComponent | NotificationToastViewComponent | (via notificationService) | -- | -- | -- |
| 2FA Settings | TwoFactorSettingsComponent | TwoFactorSettingsViewComponent | twoFactorService | TwoFactorSettingsComponent | -- | -- |
| Token Registration | TokenRegistrationComponent | TokenRegistrationViewComponent | (via authService) | TokenRegistrationComponent | -- | -- |
| App Tier | AppTierComponent | AppTierViewComponent | appTierService | AppTierComponent | -- | -- |
| Pricing Display | PricingDisplayComponent | PricingDisplayViewComponent | (via appTierService) | -- | -- | -- |
| Usage Dashboard | UsageDashboardComponent | UsageDashboardViewComponent | (via appTierService) | -- | -- | -- |
| Overage Summary | OverageSummaryComponent | OverageSummaryViewComponent | (via appTierService) | -- | -- | -- |
| Disclaimer | DisclaimerComponent | DisclaimerViewComponent | disclaimerService | DisclaimerComponent | -- | -- |
| Signup + Sub | SignupWithSubscriptionComponent | SignupWithSubscriptionViewComponent | -- | -- | -- | -- |

*`--` = not yet implemented on that platform*

When adding a new component:
1. Create the ViewComponent in this project
2. Create the corresponding Blazor component in WildwoodComponents
3. Create/update the core service in @wildwood/core
4. Create the React component in @wildwood/react
5. Create the React Native component in @wildwood/react-native
6. Ensure all platforms use the same `--ww-*` CSS variables
7. Ensure all platforms call the same WildwoodAPI endpoints
8. Update the parity checklist in ALL CLAUDE.md files

### Sibling Project Locations
- **WildwoodComponents (Blazor)**: `../WildwoodComponents/`
- **Wildwood.JS monorepo**: `C:\Development\Wildwood.JS\Dev\`

## Code Organization

### ViewComponent Architecture

Each component consists of:
```
Components/
    Authentication/
        AuthenticationViewComponent.cs    # ViewComponent class (server-side logic)
        Views/Default.cshtml              # Razor markup (HTML rendering)
        authentication.js                 # Client-side interactivity (AJAX, state)
        authentication.css                # Styles using --ww-* variables
```

### Partial Class Rule (CRITICAL)
**When a class file exceeds 900 lines, it MUST be separated into partial classes:**
- Split by logical responsibility (e.g., `MyService.cs`, `MyService.Validation.cs`)
- Use descriptive suffixes that indicate the partial's purpose
- Keep the primary file with core functionality

### Code-Behind Separation Rule (CRITICAL)
- ViewComponent `.cshtml` files: markup only, no `@functions` blocks
- All C# logic goes in the ViewComponent `.cs` class
- JavaScript files handle client-side interactivity

### Service Layer
Services mirror the WildwoodComponents service interfaces but without Blazor dependencies:
- No `IJSRuntime` (use JavaScript files instead)
- No `ComponentBase` or `IComponent`
- Use `IHttpContextAccessor` for session/token storage (not browser localStorage)
- Use `IHttpClientFactory` with named client "WildwoodAPI"

## CSS Theming

### Same Variable System as WildwoodComponents
Both libraries use identical `--ww-*` CSS custom properties. A consuming app's theme override works for both.

Key variables: `--ww-primary`, `--ww-bg-primary`, `--ww-text-primary`, `--ww-border-color`, `--ww-shadow`, etc.

See `wwwroot/css/wildwood-razor-themes.css` for the full variable list.

### Component Styling Guidelines
1. **Always use `--ww-*` variables** -- never hardcode colors
2. **Always provide fallback values** -- `var(--ww-primary, #0d6efd)`
3. **Use component-scoped selectors** -- `.ww-auth-component .form-control`
4. **Support consuming app overrides** -- consuming apps include their override CSS after the theme file

## Service Registration

```csharp
// Simple
builder.Services.AddWildwoodComponentsRazor("https://api.example.com");

// From configuration
builder.Services.AddWildwoodComponentsRazor(builder.Configuration);

// With options
builder.Services.AddWildwoodComponentsRazor(options =>
{
    options.BaseUrl = "https://api.example.com";
    options.ApiKey = "your-api-key";
    options.AppId = "your-app-id";
});
```

## Usage in Razor Pages

```html
<!-- Tag Helper syntax (recommended) -->
<vc:authentication app-id="my-app" />

<!-- Or Component.InvokeAsync -->
@await Component.InvokeAsync("Authentication", new { appId = "my-app" })
```

## Key Differences from WildwoodComponents (Blazor)

| Aspect | WildwoodComponents.Razor | WildwoodComponents |
|--------|------------------------|-------------------|
| Rendering | Server-rendered HTML + JS | Blazor component tree |
| Interactivity | JavaScript AJAX calls | C# event handlers + SignalR |
| State management | HttpContext session | Browser localStorage |
| Token storage | Server-side session | Browser localStorage |
| DI approach | Named HttpClient factory | Reflection-based registration |
| Component model | ViewComponent | ComponentBase |

## File Structure

```
WildwoodComponents.Razor/
    .claude/CLAUDE.md               # This file
    Components/
        AIChat/                     # AI Chat ViewComponent
        AIFlow/                     # AI Flow ViewComponent
        AIProxy/                    # AI Proxy ViewComponent
        AppTier/                    # App Tier + Pricing Display ViewComponents
        Authentication/             # Auth ViewComponent
        Disclaimer/                 # Disclaimer ViewComponent
        Messaging/                  # Secure Messaging ViewComponent
        Notification/               # Notification + Toast ViewComponents
        Payment/                    # Payment + PaymentForm ViewComponents
        Registration/               # Token Registration + Signup ViewComponents
        Security/                   # Two-Factor Settings ViewComponent
        Subscription/               # Subscription + Manager ViewComponents
        Usage/                      # Usage Dashboard + Overage Summary ViewComponents
    Extensions/
        ServiceCollectionExtensions.cs
    Services/
        IWildwoodAIChatService.cs + WildwoodAIChatService.cs
        IWildwoodAIFlowService.cs + WildwoodAIFlowService.cs
        IWildwoodAIProxyService.cs + WildwoodAIProxyService.cs
        IWildwoodAppTierService.cs + WildwoodAppTierService.cs
        IWildwoodAuthService.cs + WildwoodAuthService.cs
        IWildwoodDisclaimerService.cs + WildwoodDisclaimerService.cs
        IWildwoodMessagingService.cs + WildwoodMessagingService.cs
        IWildwoodPaymentService.cs + WildwoodPaymentService.cs
        IWildwoodRegistrationService.cs + WildwoodRegistrationService.cs
        IWildwoodSessionManager.cs + WildwoodSessionManager.cs
        IWildwoodSubscriptionService.cs + WildwoodSubscriptionService.cs
        IWildwoodTwoFactorSettingsService.cs + WildwoodTwoFactorSettingsService.cs
    Models/
        AIChatModels.cs
        AIFlowModels.cs
        AIProxyModels.cs
        AppTierModels.cs
        AuthenticationModels.cs
        DisclaimerModels.cs
        MessagingModels.cs
        NotificationModels.cs
        PaymentModels.cs
        SubscriptionModels.cs
        TokenRegistrationModels.cs
        TwoFactorSettingsModels.cs
        UsageModels.cs
    wwwroot/
        css/wildwood-razor-themes.css
    WildwoodComponents.Razor.csproj
```
