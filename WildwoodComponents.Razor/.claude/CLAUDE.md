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
| AI Flow | AIFlowComponent | AIFlowViewComponent | aiFlowService | AIFlowComponent + useAIFlow | AIFlowComponent + useAIFlow | -- |
| AI Proxy | AIProxyComponent | AIProxyViewComponent | (via aiService) | AIProxyComponent | -- | -- |
| Feature Gate | FeatureGateComponent | (IWildwoodAppTierService.HasFeatureAsync) | (via appTierService) | FeatureGate + useFeatures | FeatureGate + useFeatures | -- |
| Messaging | SecureMessagingComponent | SecureMessagingViewComponent | messagingService | SecureMessagingComponent | -- | -- |
| Payment | PaymentComponent | PaymentViewComponent | paymentService | PaymentComponent | -- | -- |
| Payment Form | PaymentFormComponent | PaymentFormViewComponent | (via paymentService) | PaymentFormComponent | -- | -- |
| Notifications | NotificationComponent | NotificationViewComponent | notificationService | NotificationComponent | -- | -- |
| Notification Toast | NotificationToastComponent | NotificationToastViewComponent | (via notificationService) | -- | -- | -- |
| 2FA Settings | TwoFactorSettingsComponent | TwoFactorSettingsViewComponent | twoFactorService | TwoFactorSettingsComponent | -- | -- |
| Token Registration | TokenRegistrationComponent | TokenRegistrationViewComponent | (via authService) | TokenRegistrationComponent | -- | -- |
| App Tier | AppTierComponent | AppTierViewComponent | appTierService | AppTierComponent | -- | -- |
| Pricing Display | PricingDisplayComponent | PricingDisplayViewComponent | (via appTierService) | -- | -- | -- |
| Reg & Sub: pricing | RegistrationSubscriptionPricing | RegistrationSubscriptionPricingViewComponent | (via appTierService + catalog helpers) | RegistrationSubscriptionPricing | RegistrationSubscriptionPricing | -- |
| Reg & Sub: signup | RegistrationSubscriptionSignup | RegistrationSubscriptionSignupViewComponent | (via authService + appTierService) | RegistrationSubscriptionSignup | RegistrationSubscriptionSignup | -- |
| Reg & Sub: manage | RegistrationSubscriptionManage | RegistrationSubscriptionManageViewComponent | (via appTierService) | ManageView | ManageView | -- |
| Reg & Sub: shell | RegistrationAndSubscriptionComponent | RegistrationAndSubscriptionViewComponent | -- | RegistrationAndSubscriptionComponent | RegistrationAndSubscriptionComponent | -- |
| Usage Dashboard | UsageDashboardComponent | UsageDashboardViewComponent | (via appTierService) | -- | -- | -- |
| Overage Summary | OverageSummaryComponent | OverageSummaryViewComponent | (via appTierService) | -- | -- | -- |
| Disclaimer | DisclaimerComponent | DisclaimerViewComponent | disclaimerService | DisclaimerComponent | -- | -- |
| Feedback | FeedbackWidgetComponent | FeedbackWidgetViewComponent | feedbackService | FeedbackComponent | FeedbackComponent | -- |
| Signup + Sub | SignupWithSubscriptionComponent | SignupWithSubscriptionViewComponent | -- | -- | -- | -- |
| Campaign Attribution | AttributionBootstrap + IAttributionService (wildwood-attribution.js); AuthenticationService auto-attaches the payload to every registration path and claims after a provider-token sign-in (15-minute queue); AuthenticationComponent claims after a popup provider sign-in (IAttributionService.ClaimAsync) | AttributionViewComponent (attribution.js) + WildwoodAttributionProxyController claim for signed-in sessions | AttributionService (attribution engine) | useAttribution (WildwoodProvider starts capture) | useAttribution (provider captures deep links) | -- |

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
        RegistrationSubscription/   # Registration & Subscription pricing + signup + manage
                                    #   ViewComponents, and the RegistrationAndSubscription shell
                                    #   (a view="pricing|signup|manage" switch, nothing else)
                                    #   + RegistrationSubscriptionPricingDecisions (pure, testable)
                                    #   + RegistrationSubscriptionSignupDecisions (pure, testable)
                                    #   + RegistrationSubscriptionManageDecisions (pure, testable)
                                    #   + RegistrationAndSubscriptionShell (pure, testable)
        Security/                   # Two-Factor Settings ViewComponent
        Subscription/Admin/         # Subscription Admin ViewComponents (status, tiers, features, add-ons, limits, overrides)
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
        IWildwoodPublicCatalogService.cs + WildwoodPublicCatalogService.cs   # 60s catalog cache
        IWildwoodRegistrationService.cs + WildwoodRegistrationService.cs
        IWildwoodSessionManager.cs + WildwoodSessionManager.cs
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
        RegistrationSubscriptionPricingModels.cs
        SubscriptionModels.cs
        TokenRegistrationModels.cs
        TwoFactorSettingsModels.cs
        UsageModels.cs
    wwwroot/
        css/wildwood-razor-themes.css
        css/regsub.css              # Registration & Subscription: pricing, signup and manage
        js/regsub-pricing.js        # billing toggle, pack basket, ww-regsub-select
        js/regsub-machines.js       # signup + pack-checkout + plan-change reducers, ported
                                    #   table-identical from @wildwood/react-shared. Pure; load it
                                    #   FIRST, before every driver below.
                                    #   Covered by WildwoodComponents.Tests/Razor/js/regsub-machines.selftest.mjs,
                                    #   which RegSubMachineSelfTestRunnerTests runs through node.
        js/regsub-packcheckout.js   # SHARED driver: quote -> one card -> checkout -> 3DS walk.
                                    #   Used by the signup's pack step AND the manage view's picker.
        js/regsub-planchange.js     # SHARED driver: preview -> confirmation modal -> change ->
                                    #   3DS on the card on file -> complete the parked change.
                                    #   Used by the manage view AND subscription-admin.js. Carries
                                    #   the one wwFormatMoney copy those two need.
        js/regsub-signup.js         # the signup driver: pay-first, card-once pack checkout
        js/regsub-manage.js         # the manage driver: sections, plan-change notice, pack picker

    NEVER copy a sequence out of the two SHARED drivers into a view's own script. They are shared
    because they are the parts that move money - whether the server may park a change on a bank
    challenge, whether a parked change is ever completed, whether an authenticated pack is
    recorded - and a second copy is a second place for those to drift. A source guard fails the
    build if `confirmCardPayment`, `confirmCardSetup`, a reducer call or `SupportsPaymentAction`
    appears in regsub-signup.js, regsub-manage.js or subscription-admin.js.
    WildwoodComponents.Razor.csproj
```
