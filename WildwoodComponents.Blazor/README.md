# WildwoodComponents

A comprehensive Blazor component library for authentication, forms, AI chat, and UI components with built-in theming and multi-platform support.

## Features

- 🔐 **Authentication Components**: Complete authentication system with local and social providers
- 🤖 **AI Chat Components**: Interactive AI chat interfaces with session management
- 🎨 **Theme System**: Built-in theming with light/dark mode support
- 📱 **Multi-Platform**: Support for Blazor Server, WebAssembly, and MAUI
- 🛡️ **Error Handling**: Comprehensive error handling and logging
- 🔄 **Loading States**: Built-in loading indicators and state management
- 🌐 **Internationalization**: Multi-language support
- ♿ **Accessibility**: WCAG compliant components

## Quick Start

### Installation

Add a project reference to WildwoodComponents:

```xml
<ProjectReference Include="path\to\WildwoodComponents\WildwoodComponents.csproj" />
```

### Service Registration

```csharp
// In Program.cs (Blazor Server/WebAssembly) or MauiProgram.cs (MAUI)
builder.Services.AddWildwoodComponents("https://your-api-base-url");

// With additional configuration
builder.Services.AddWildwoodComponents(config =>
{
    config.BaseUrl = "https://your-api-base-url";
    config.ApiKey = "your-api-key";
    config.AppId = "your-app-id";
    config.EnableDetailedErrors = true;
});
```

### Basic Usage

```razor
@using WildwoodComponents.Components.Authentication

<AuthenticationComponent 
    AppId="your-app-id"
    OnAuthenticationSuccess="@HandleAuthSuccess"
    OnAuthenticationError="@HandleAuthError" />

@code {
    private async Task HandleAuthSuccess(AuthenticationResponse response)
    {
        // Handle successful authentication
        Console.WriteLine($"User authenticated: {response.User.Email}");
    }
    
    private async Task HandleAuthError(ComponentErrorEventArgs error)
    {
        // Handle authentication error
        Console.WriteLine($"Authentication failed: {error.Exception.Message}");
    }
}
```

## Components

### AuthenticationComponent

Complete authentication solution with support for:

- Local authentication (email/password)
- Social providers (Google, Facebook, Microsoft, etc.)
- Multi-factor authentication
- Email verification
- Password reset
- User registration
- Captcha support

**Key Features:**
- Inherits from `BaseWildwoodComponent` for consistent error handling
- Automatic loading state management
- Built-in theme support
- Comprehensive validation

**Parameters:**
- `AppId` (string): Application identifier
- `ShowPasswordField` (bool): Show/hide password field
- `ShowLicenseToken` (bool): Show/hide license token field
- `OnAuthenticationSuccess` (EventCallback): Success callback
- `OnAuthenticationError` (EventCallback): Error callback (now uses ComponentErrorEventArgs)

### AIChatComponent

Interactive AI chat interface with:

- Session management
- Multiple AI configurations
- Real-time typing indicators
- Message history
- File upload support
- Custom AI models

**Enhanced Features:**
- Inherits from `BaseWildwoodComponent`
- Automatic error handling and recovery
- Loading state management
- Theme-aware styling

### Registration & Subscription

`RegistrationAndSubscriptionComponent` is one component for everything a customer does with money:
the price list, signing up and buying, and managing what they bought. `View` picks the surface, and
with it the rest of the parameters.

```razor
@using WildwoodComponents.Blazor.Components.RegistrationSubscription

<RegistrationAndSubscriptionComponent View="RegistrationSubscriptionView.Pricing"
                                      AppId="@AppId" OnSelect="GoToSignup" />

<RegistrationAndSubscriptionComponent View="RegistrationSubscriptionView.Signup"
                                      AppId="@AppId" OnSignupComplete="Welcome" />

<RegistrationAndSubscriptionComponent View="RegistrationSubscriptionView.Manage"
                                      AppId="@AppId" OnEntitlementsChanged="RefreshGates" />
```

It is a switch and nothing more — no markup, no state, no service call of its own — so
`RegistrationSubscriptionPricing`, `RegistrationSubscriptionSignup` and
`RegistrationSubscriptionManage` can be mounted directly instead, with the same parameters minus
`View`. A landing page that only wants the price list should do exactly that.

**The shell's parameter surface.** React discriminates its props union on `view`, so a prop
belonging to another view is a compile error there. Blazor has no discriminated-union parameters,
so the shell declares the **union** of the three views' parameters and forwards to the active view
only the ones that view has. A parameter set for a view that is not showing is **ignored** — never
an exception, never a render error. The tables below say which view each parameter reaches, and
`RegistrationAndSubscriptionShellTests` compares the shell against the three views by reflection on
every build, so a parameter added to a view later cannot become silently unreachable. Use the view
components directly when you want the compiler to catch a misplaced parameter.

Also shipped in the same folder, usable on their own: `ClosedNotice` (the "registration is closed"
panel), `PaymentModal` (the card modal the manage view brings), `PackGrid`, `PackPicker`,
`PlanGrid`, `TokenPlanSummary`, `OrderSummary`, `CatalogJsonLd`, and
`RegistrationSubscriptionLabels` for the copy.

#### Parameters every view shares

| Parameter | Description |
|---|---|
| `AppId` | The app whose catalog and settings to read. Required. |
| `Currency` | Overrides the currency the server quotes the catalog in. Display only; rarely needed. |
| `Labels` | Overrides for any of the component's strings; unset properties keep the shipped copy. |
| `ContactUrl` | Where "contact us" and enterprise plans point. |
| `CssClass`, `AdditionalAttributes` | Applied to the active view's root element, as on every component here. |
| `OnError` | `ComponentErrorEventArgs` whenever the component gives up on something. It never throws into your render. |

#### Pricing view — `View="RegistrationSubscriptionView.Pricing"`

What the app sells, at the price the server is quoting right now.

| Parameter | Default | Description |
|---|---|---|
| `ShowPlans` | `true` | Render the plan grid. |
| `ShowAddOns` | *view default* `false` | Render the pack grid. On the shell this is `bool?`; left null each view keeps its own answer (pricing off, manage on). |
| `OfferFreeTierChoice` | `true` | `false` hides every `IsFreeTier` plan. |
| `PackSelection` | `None` | `None` gives each pack its own call to action; `Multi` lets the visitor tick several and continue once. |
| `AddOnGroups` | `null` | Headings matched on each pack's catalog `Category`. An empty group is dropped; packs matching no group land in a trailing "More packs" group rather than vanishing. |
| `DescribeAddOn` | `null` | `RenderFragment<AppTierAddOnModel>` — host copy for one pack. Blazor's analog of React's `describeAddOn`: a template, because markup is what it returns. |
| `ShowBillingToggle` | `true` | Rendered only when some plan is actually priced by the year. |
| `DefaultBilling` | `Monthly` | Which side the toggle starts on. |
| `ShowFeatureComparison`, `ShowLimits` | `true` | Per-plan feature list and usage limits. |
| `HighlightTierId` | `null` | Marks one plan as the visitor's current choice (matched case-insensitively). |
| `IncludeJsonLd`, `JsonLdUrl` | `false`, `null` | Emit schema.org offers for the live catalog, optionally with a canonical URL. |
| `PreloadedCatalog` | `null` | A `PublicCatalog` the host already has. See *Dynamic pricing* below. |
| `LoadingFallback`, `ErrorFallback` | `null` | Replace the built-in placeholder and the "pricing is unavailable" panel. |
| `OnSelect` | — | `PricingSelection { TierId?, PricingId?, Billing, AddOnIds }`. A plan's call to action carries whatever packs are ticked, so one click can buy the whole basket; a pack-only selection carries no `TierId`. `AddOnIds` is in catalog order and empty rather than null. |

The plan grid is the platform's existing tier-card markup — the same `.ww-tier-grid` full of
`.ww-tier-card`, the same "Get Started" / "Subscribe" / "Switch to ..." calls to action — so a site
swapping `PricingDisplayComponent` for this keeps its locators. The view never navigates: a
selection goes to `OnSelect` and the host decides where it leads.

#### Signup view — `View="RegistrationSubscriptionView.Signup"`

The whole way in, in the order that keeps an account and its money consistent.

| Parameter | Default | Description |
|---|---|---|
| `PreSelectedTierId`, `PreSelectedPricingId` | `null` | The plan a pricing page already chose. Ignored when the app does not sell it. |
| `PreSelectedAddOnIds` | `null` | Packs a pricing page already chose. Re-resolved against the live catalog and capped at 25. |
| `RegistrationToken` | `null` | An invitation token from the signup link. |
| `PrefillEmail` | `null` | Pre-fills the username and email fields. |
| `PlanSelection` | `Choose` | `Skip` takes the app's default plan and leaves the plan step out. |
| `PackSelection` | `None` | `None` only removes the STEP where packs are picked — packs a link already chose are still bought. |
| `TokenMode` | `Auto` | `Auto` follows the app's live registration settings; `Required` is invite redemption. |
| `RequireBillingAddress` | `false` | Collect a billing address with the card. |
| `ReturnUrl` | `null` | Carried, never navigated to. |
| `AddOnGroups`, `DescribeAddOn` | `null` | As on the pricing view, for the pack step. |
| `RenderClosed` | `null` | `RenderFragment<RegistrationClosedContext>` replacing the built-in closed notice. |
| `PreloadedCatalog` | `null` | As on the pricing view. |
| `OnSignupComplete` | — | `SignupOutcome`; raised exactly once. |
| `OnAlreadySignedIn` | — | Raised once when the visitor turned out to have a session already. Latched at the first initialisation, so the sign-in this flow performs cannot trigger it. |
| `OnCancel` | — | The visitor backed out. |
| `OnEntitlementsChanged` | — | The new account's entitlements changed; see *Entitlement reasons*. |

**The order is pay-first.** The app's live registration settings decide what the form offers (open
sign-up, the optional "Have a registration token?" card, a required token, or the closed notice) —
there is no `RequireToken` / `AllowOpenRegistration` / `ShowOptionalTokenEntry` for a host to get
wrong. A registration token is validated and **what it grants is shown before anything is charged**.
The plan's card is taken **before** the account exists: a declined card then leaves nothing behind,
rather than an account sitting on a plan nobody paid for. Packs are bought **after** the login, as
the user, so the card taken a minute earlier is the saved card and nobody is asked for it twice. One
pack failing never stops the others.

**A token that carries a plan** renders "Your registration token includes" with the tier, packs and
features it grants, and skips the plan and payment steps: none of it reaches a checkout.
`TokenMode="SignupTokenMode.Required"` is invite redemption — the token is the only way in, there is
no plan and no pack step, and it overrides a closed configuration, because the server validates the
token itself.

**`SignupOutcome`** (`WildwoodComponents.Shared.Models`), raised once the account exists and
everything asked for has been granted or reported, after the app's feature gates have been
refreshed:

```csharp
public class SignupOutcome
{
    public string UserId { get; set; }
    public SignupOutcomeTier? Tier { get; set; }        // TierId, Name, PricingId?
    public List<SignupPackOutcome> Packs { get; set; }  // AddOnId, Name, Status, TrialEnd?, ErrorMessage?
    public SignupTokenGrant? TokenGrant { get; set; }   // TierId, PricingId?, AddOnIds, FeatureCodes
    public bool? PlanActivationPending { get; set; }
}
```

`Status` is one of `SignupPackStatuses`: `trialing`, `active`, `failed`, or `granted`. `granted` is a
pack a registration token (or an admin) set up — there is a subscription row but no payment
transaction behind it, so nothing was charged for it. Granted packs are listed first.

Registration refusals arrive at `OnError` with the server's own code (a 403 `RegistrationNotAllowed`,
say) as well as on screen, and "Try Again" resumes at the step that failed instead of registering the
same person twice.

#### Manage view — `View="RegistrationSubscriptionView.Manage"`

What a customer already pays for, and every way of changing it.

| Parameter | Default | Description |
|---|---|---|
| `Layout` | `Tabs` | `Tabs` shows one panel at a time; `Stacked` puts every section down the page under its own heading. |
| `Sections` | `null` (all) | Picks and orders `Subscription`, `Plans`, `Features`, `AddOns`, `Usage`, `Overrides`. |
| `ShowStatusAboveTabs` | `false` | Lifts the subscription card out of the section list and above the tab bar. |
| `IsAdmin` | `false` | Unlocks the overrides panel and usage editing. |
| `UserId`, `CompanyId` | `null` | Whose subscription to manage; neither means the signed-in user's own. Such a change never collects a card. |
| `AllowCancel` | `true` | Whether cancelling the subscription and its packs is offered at all. |
| `ShowAddOns` | *view default* `true` | Whether the packs section is offered. |
| `AllowPackSelfService` | `false` | Offers "Add packs" and the component's own card-once pack checkout. |
| `OnMergeUsage` | `null` | `Func<List<AppTierLimitStatusModel>, UserTierSubscriptionModel?, Task<List<AppTierLimitStatusModel>>>` — overlay your own real-time usage before the limits render. The same seam `UsageDashboardComponent` offers. |
| `OnPaymentRequired` | `null` | `Func<PaymentRequiredArgs, Task<string?>>`. Optional and rarely wanted: the view has its own card modal and uses it when this is left out. A handler passed here wins, and a null or empty answer abandons the change. |
| `OnSubscriptionChanged` | — | Raised after every mutation that changed what the subscription is. |
| `OnEntitlementsChanged` | — | See *Entitlement reasons*. |
| `ReturnUrl` | `null` | Where a redirect-flow payment provider should come back to. Carried, never navigated to. |

A plan change goes preview → confirm (in **every** layout) → a card if one is needed → change → 3-D
Secure → completion. A prorated charge the bank wants to see is confirmed with the app's own Stripe
publishable key, and the parked change is then completed by its `PendingChangeId`, retried while the
server keeps answering `processing`. A pack is cancelled at the end of the period it is paid up to,
after being told so, and a scheduled cancellation can be taken back; a pack nobody paid for reads
"Included with your registration", shows no renewal date and offers no Reactivate.

**`SubscriptionAdminComponent` behaviour change.** It now runs its plan change through the same
`PlanChangeDriver` this view uses, so the two cannot drift. A change that needed a card and had no
`OnPaymentRequired` handler used to fail with "Payment is required for this tier change. Wire the
OnPaymentRequired callback to collect payment."; it now opens the component's **own** payment modal
instead. A host that passes `OnPaymentRequired` keeps exactly the old behaviour. Two other things
came with the shared flow: a prorated charge the bank wants to see is now authenticated and the
parked change completed (it used to be refused outright), and the confirmation modal renders in
every display mode rather than only the tabbed one.

#### Entitlement reasons

`OnEntitlementsChanged` carries one of `EntitlementsChangedReasons`
(`WildwoodComponents.Shared.Models`), whose whole vocabulary is `signup`, `tierChange`, `addOn`,
`cancel`, `reactivate`, `manual` — the same strings the JS client emits as `entitlementsChanged`.
The signup view reports `signup`, so a host switching on the reason must handle it or it will drop
the refresh that matters most: the one right after an account is created. The callback is
**additive** — the views already invalidate the shared entitlement cache, so a host that only needs
that can keep ignoring it.

#### Dynamic pricing

Every price on every view comes off the **live** catalog or the account's own subscription. There is
no fallback price, no remembered price and no "from" price anywhere in the folder, and a source
guard in the test suite greps for one:

- while the catalog loads, a skeleton with no numbers in it (replaceable with `LoadingFallback`);
- when the catalog cannot be read, "Pricing is unavailable right now" and a Retry — never a stale
  figure (replaceable with `ErrorFallback`), reported to `OnError` with
  `PricingViewDecisions.CatalogErrorCode` (`catalog_unavailable`) as the `Context`;
- every catalog-driven amount is formatted with `FormatHelpers.FormatMoney(amount, currency)`, so a
  currency outside the SDK's small symbol table (CHF, SEK, ...) renders as itself rather than
  falling back to a dollar sign;
- `IncludeJsonLd` publishes the same live prices as schema.org offers, leaving out an item with no
  pricing option and a tier whose operator turned `ShowPrice` off rather than publishing a zero.

`PreloadedCatalog` is the Blazor analog of React's `initialCatalog`: give the pricing or signup view
a `PublicCatalog` the host already read — during a server prerender, or shared with another surface
on the page — and it renders real prices on the first paint and asks the server for nothing. It is a
price list with a timestamp, not a cache: rebuild it whenever the page is rebuilt, because a seeded
catalog is served for the shared 60-second TTL before anything refetches.

#### Stripe instances

Two card surfaces can be on one page at once (the signup view's `CardSetupForm` next to
`PaymentComponent`, or the manage view's modal over a mounted form), and the browser script used to
keep exactly one Stripe instance and one card element. Every card surface in this folder now names
its own **instance key**, so each owns and disposes the instance it created and none of them touches
`PaymentComponent`'s default instance. The 3-D Secure adapter (`StripePaymentActions`) does the same:
given a publishable key it creates a bare instance under its own key when no card form is mounted,
and drops it on disposal. Nothing about `PaymentComponent`'s own usage changes.

#### Test hooks

The root of every view carries `data-ww-view="pricing" | "signup" | "manage"`. The signup view's step
container carries `data-ww-step`: `loading`, `closed`, `register`, `token`, `plan`, `packs`,
`payment`, `creating`, `disclaimers`, `packCheckout`, `success`, `failed`. The manage view's root
carries the plan change's step: `idle`, `previewing`, `confirm`, `collectingPayment`, `changing`,
`authenticating`, `completing`, `done`, `failed`. Packs carry `data-ww-pack="<addOnId>"` and pack
groups `data-ww-group="<groupId>"` (the trailing catch-all group is `more`); in the manage view,
`data-ww-section="<section>"` is on each panel in the stacked layout and on each tab button in the
tabbed one. The copy and class locators live sites' end-to-end suites already use are deliberately
unchanged.

A harness for all four surfaces — the three views plus the invite preset — is in the test-suite app
at `/test/registration-subscription`, with `?view=`, `?appId=`, `?token=` (or `?invite=`) and
`?email=` query parameters, the same ones the React suite reads.

#### Deprecations

`PricingDisplayComponent`, `SignupWithSubscriptionComponent` and `AppTierComponent` are marked
`[Obsolete]` — a **warning**, not an error, in step with the JS package (541e446). They are still
registered, still compile, still render, and behave exactly as before; nothing has been removed, so
a site can move one page at a time. `TokenRegistrationComponent` is **not** deprecated: it is still
the right thing for a bare token form with no plan or pack around it, and the signup view mounts it
internally.

| Deprecated | Replacement | Recipe |
|---|---|---|
| `PricingDisplayComponent` | `View="RegistrationSubscriptionView.Pricing"` | Same tier-card grid off the live catalog. `PreloadedTiers` becomes `PreloadedCatalog`, `OnSelectTier` becomes `OnSelect` (which carries the pricing option and any ticked packs as well as the tier). Add `ShowAddOns` + `PackSelection="PricingPackSelection.Multi"` for a packs section, and `IncludeJsonLd` for offers. |
| `SignupWithSubscriptionComponent` | `View="RegistrationSubscriptionView.Signup"` | Drop the `RequireToken` / `AllowOpenRegistration` / `ShowOptionalTokenEntry` parameters and whatever fetched the authentication configuration to compute them — the view reads the live settings itself, closed notice included. `OnComplete` becomes `OnSignupComplete`, with a structured `SignupOutcome` instead of a bare user id. |
| `AppTierComponent` | `View="RegistrationSubscriptionView.Manage"` | The view runs a plan change through preview → confirm → its own card modal → 3-D Secure → completion. **Known bug the deprecated component keeps**: its payment step passes no `PricingModelId` (and no `IsSubscription`) to `PaymentComponent`, so a paid plan is charged **once** instead of starting the plan's recurring subscription and its free trial. That is left exactly as it is — fixing it would change what existing hosts charge — which is the main reason not to use it for anything priced. |

An invite page is the signup view with `TokenMode="SignupTokenMode.Required"`,
`PlanSelection="SignupPlanSelection.Skip"` and `PackSelection="PricingPackSelection.None"`.

In the Razor package, `ViewHelpers.GetCurrencySymbol` and `ViewHelpers.FormatAmount` carry the same
kind of `[Obsolete]` warning, pointing at `ViewHelpers.FormatMoney`. Both still behave exactly as
they always did, for host `.cshtml` that calls them.

### BaseWildwoodComponent

Shared base class providing:

- **Theme Management**: Automatic theme loading and change handling
- **Error Handling**: Consistent error handling with logging and event callbacks
- **Loading States**: Built-in loading state management with visual indicators
- **JavaScript Interop**: Safe JS invocation with error handling
- **Lifecycle Management**: Enhanced component lifecycle with virtual methods

**Common Parameters (available on all components):**
- `CssClass` (string): Additional CSS classes
- `AdditionalAttributes` (Dictionary): HTML attributes
- `ShowLoadingStates` (bool): Enable/disable loading indicators
- `EnableErrorHandling` (bool): Enable/disable automatic error display
- `OnError` (EventCallback): Error event callback
- `OnLoadingStateChanged` (EventCallback): Loading state change callback

## Advanced Usage

### Creating Custom Components

```csharp
@using WildwoodComponents.Components.Base
@inherits BaseWildwoodComponent

<div class="@GetRootCssClasses()">
    @if (IsLoading && ShowLoadingStates)
    {
        <div class="loading-spinner">Loading...</div>
    }
    else if (!string.IsNullOrEmpty(ErrorMessage))
    {
        <div class="alert alert-danger">@ErrorMessage</div>
    }
    else
    {
        <!-- Your component content -->
    }
</div>

@code {
    [Parameter] public string? CustomParameter { get; set; }
    
    protected override async Task OnComponentInitializedAsync()
    {
        await ExecuteAsync(async () =>
        {
            // Your initialization code with automatic error handling
        }, "Initializing component");
    }
    
    private async Task HandleAction()
    {
        await ExecuteAsync(async () =>
        {
            // Your async operation with automatic error handling and loading states
        }, "Performing action");
    }
}
```

### Error Handling

Components provide comprehensive error handling through the base class:

```csharp
// Error event callback uses ComponentErrorEventArgs
[Parameter] public EventCallback<ComponentErrorEventArgs> OnError { get; set; }

// In your component
<AuthenticationComponent OnError="@HandleComponentError" />

@code {
    private async Task HandleComponentError(ComponentErrorEventArgs args)
    {
        Logger.LogError(args.Exception, "Component error in {ComponentType}: {Context}", 
            args.ComponentType, args.Context);
    }
}
```

### Safe Async Operations

The base component provides safe async execution:

```csharp
// With return value
var result = await ExecuteAsync(async () =>
{
    return await SomeApiCall();
}, "API operation");

// Without return value
await ExecuteAsync(async () =>
{
    await SomeOperation();
}, "Background operation");
```

### JavaScript Interop

Safe JavaScript calls with automatic error handling:

```csharp
// With return value
var result = await InvokeJSAsync<string>("someFunction", param1, param2);

// Void calls
await InvokeJSVoidAsync("someFunction", param1, param2);
```

## Configuration

### ServiceCollectionExtensions

The `AddWildwoodComponents` extension method provides simplified service registration:

```csharp
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddWildwoodComponents(this IServiceCollection services, string baseUrl);
    public static IServiceCollection AddWildwoodComponents(this IServiceCollection services, Action<WildwoodComponentsOptions> configureOptions);
}

public class WildwoodComponentsOptions
{
    public string BaseUrl { get; set; } = "https://localhost:5291";
    public string? ApiKey { get; set; }
    public string? AppId { get; set; }
    public bool EnableDetailedErrors { get; set; } = true;
    public int RequestTimeoutSeconds { get; set; } = 30;
    public bool EnableRetry { get; set; } = true;
    public int MaxRetryAttempts { get; set; } = 3;
    public bool EnableCaching { get; set; } = true;
    public int CacheDurationMinutes { get; set; } = 10;
}
```

## Platform Support

### Blazor Server

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddWildwoodComponents("https://api.example.com");

var app = builder.Build();
```

### Blazor WebAssembly

```csharp
// Program.cs
var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddWildwoodComponents("https://api.example.com");

await builder.Build().RunAsync();
```

### .NET MAUI

```csharp
// MauiProgram.cs
public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

        builder.Services.AddMauiBlazorWebView();
        builder.Services.AddWildwoodComponents("https://api.example.com");

        return builder.Build();
    }
}
```

## Component Architecture Benefits

### Before vs After Enhancement

**Before (Complex Integration):**
- Manual service registration with reflection
- Custom error handling in each component
- Inconsistent loading states
- Repetitive theme management
- No base class structure

**After (Enhanced Architecture):**
- One-line service registration
- Automatic error handling through base class
- Consistent loading states across all components
- Centralized theme management
- Shared functionality through inheritance

### Key Improvements

1. **95% Code Reduction**: From 300+ lines to 10 lines for integration
2. **Consistent API**: All components follow the same patterns
3. **Automatic Error Handling**: No need for manual try-catch blocks
4. **Built-in Loading States**: Automatic loading indicators
5. **Theme Management**: Centralized theme handling
6. **Better Maintainability**: Shared base class for common functionality

## Migration Guide

### From Previous Versions

1. **Update Service Registration**: Replace complex registration with `AddWildwoodComponents()`
2. **Update Error Handling**: Use `ComponentErrorEventArgs` instead of string messages
3. **Update Component Usage**: Components now have automatic loading states and error handling
4. **Remove Manual Error Handling**: Base class handles errors automatically

### Breaking Changes

- `OnError` callback now uses `ComponentErrorEventArgs` instead of string
- Components now automatically handle loading states
- Service registration pattern has changed
- Theme management is now automatic

### Deprecations in 1.3.0

Nothing was removed and nothing changed behaviour. `PricingDisplayComponent`,
`SignupWithSubscriptionComponent` and `AppTierComponent` now carry `[Obsolete]` **warnings**
pointing at `RegistrationAndSubscriptionComponent`'s three views. See
[Registration & Subscription → Deprecations](#deprecations) for a recipe per component.

The one behaviour change in the same release is `SubscriptionAdminComponent`'s: a plan change that
needs a card and has no `OnPaymentRequired` handler now opens the component's own payment modal
instead of failing with "Wire the OnPaymentRequired callback to collect payment." A host that
passes a handler is unaffected.

### Breaking Changes in 1.2.0

- **`DisclaimerComponent`, `AIProxyComponent` and `AppTierComponent` no longer redeclare `OnError`.**
  They had hidden the inherited parameter with `new`, which gave each type two `[Parameter]`
  properties named `onerror` — a combination Blazor rejects at render time, so those components
  could not be rendered at all (on Blazor Server the exception terminated the circuit). They now use
  the inherited `EventCallback<ComponentErrorEventArgs> OnError`, in line with the contract above.
  Handlers bound to them must take `ComponentErrorEventArgs`; the message previously delivered as
  the `string` payload is `args.Exception.Message`.
- **`IDisclaimerService` gains `void SetAuthToken(string? token)`.** Both acceptance endpoints are
  `[Authorize]`, and the service had no way to send a bearer token, so every acceptance returned 401.
  Callers set the current user's JWT before accepting. This breaks external *implementers* of the
  interface, not consumers of it.

## Troubleshooting

### Common Issues

1. **Service Registration Failures**
   - Ensure `AddWildwoodComponents()` is called before `Build()`
   - Verify HttpClient is available in DI container
   - Check WildwoodAPI server accessibility

2. **Component Loading Issues**
   - Include proper using directives: `@using WildwoodComponents.Components.Base`
   - Ensure components inherit from `BaseWildwoodComponent`
   - Verify component parameters

3. **Error Handling Issues**
   - Update error callbacks to use `ComponentErrorEventArgs`
   - Enable detailed errors in development: `EnableDetailedErrors = true`

### Debug Mode

Enable detailed error logging:

```csharp
builder.Services.AddWildwoodComponents(config =>
{
    config.BaseUrl = "https://api.example.com";
    config.EnableDetailedErrors = true;
});
```

## Best Practices

1. **Component Development**
   - Always inherit from `BaseWildwoodComponent`
   - Use `ExecuteAsync` for async operations
   - Leverage automatic error handling and loading states

2. **Error Handling**
   - Implement error callbacks for critical operations
   - Use detailed error messages during development
   - Provide user-friendly messages in production

3. **Performance**
   - Enable caching for configuration calls
   - Use loading states for better UX
   - Implement proper disposal in custom components

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Support

- 📖 [Documentation](https://docs.wildwoodcomponents.com)
- 🐛 [Issue Tracker](https://github.com/wildwood/WildwoodComponents/issues)
- 💬 [Discussions](https://github.com/wildwood/WildwoodComponents/discussions)
- 📧 [Email Support](mailto:support@wildwoodcomponents.com)

## Changelog

### v1.0.0 (Enhanced Architecture)
- Added `BaseWildwoodComponent` for shared functionality
- Implemented `ServiceCollectionExtensions` for simplified registration
- Enhanced `AuthenticationComponent` with automatic error handling
- Updated `AIChatComponent` architecture
- Comprehensive error handling and logging
- Automatic loading state management
- Built-in theme support
- Multi-platform compatibility
- Improved developer experience with 95% code reduction
