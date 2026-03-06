# WildwoodComponents Claude Instructions

## Project Overview

WildwoodComponents is a comprehensive Blazor component library featuring:
- **BaseWildwoodComponent** architecture for automatic error handling, loading states, and theme management
- Authentication, forms, AI chat, secure messaging, notifications, payments, and subscription components
- Built-in theming system with multi-platform support (Web, MAUI, Android, iOS, macOS, Windows)

## CRITICAL: Feature Parity Across All Platforms

WildwoodComponents exists across multiple platforms. **When a component is added or modified in ANY platform, all other platforms MUST be updated to match.**

### Platform Libraries
- **WildwoodComponents (Blazor)** - This project
- **WildwoodComponents.Razor** - Razor Pages ViewComponents (`../WildwoodComponents.Razor/`)
- **@wildwood/core** - Pure TypeScript SDK (Wildwood.JS repo: `packages/wildwood-core/`)
- **@wildwood/react** - React hooks + components (Wildwood.JS repo: `packages/wildwood-react/`)
- **@wildwood/react-native** - React Native components (Wildwood.JS repo: `packages/wildwood-react-native/`)
- **@wildwood/node** - Node.js/Express middleware (Wildwood.JS repo: `packages/wildwood-node/`)

### Feature Parity Checklist

| Component | Blazor | Razor | @wildwood/core | @wildwood/react | @wildwood/react-native | @wildwood/node |
|-----------|--------|-------|----------------|-----------------|------------------------|----------------|
| Authentication | AuthenticationComponent | AuthenticationViewComponent | authService | AuthenticationComponent | AuthenticationComponent | authMiddleware |
| AI Chat | AIChatComponent | -- | aiService | AIChatComponent | -- | -- |
| AI Proxy | AIProxyComponent | AIProxyViewComponent | (via aiService) | AIProxyComponent | -- | -- |
| Messaging | SecureMessagingComponent | -- | messagingService | SecureMessagingComponent | -- | -- |
| Payment | PaymentComponent | PaymentViewComponent | paymentService | PaymentComponent | -- | -- |
| Payment Form | PaymentFormComponent | -- | (via paymentService) | PaymentFormComponent | -- | -- |
| Subscription | SubscriptionComponent | -- | subscriptionService | SubscriptionComponent | -- | -- |
| Subscription Mgr | SubscriptionManagerComponent | -- | (via subscriptionService) | SubscriptionManagerComponent | -- | -- |
| Notifications | NotificationComponent | -- | notificationService | NotificationComponent | -- | -- |
| 2FA Settings | TwoFactorSettingsComponent | -- | twoFactorService | TwoFactorSettingsComponent | -- | -- |
| Token Registration | TokenRegistrationComponent | TokenRegistrationViewComponent | (via authService) | TokenRegistrationComponent | -- | -- |
| App Tier | AppTierComponent | AppTierViewComponent | appTierService | AppTierComponent | -- | -- |
| Disclaimer | DisclaimerComponent | -- | disclaimerService | DisclaimerComponent | -- | -- |

*`--` = not yet implemented on that platform*

When adding a new component:
1. Create the component in this project (Blazor)
2. Create the corresponding ViewComponent in WildwoodComponents.Razor
3. Create/update the core service in @wildwood/core
4. Create the React component in @wildwood/react
5. Create the React Native component in @wildwood/react-native
6. Ensure all platforms use the same `--ww-*` CSS variables
7. Ensure all platforms call the same WildwoodAPI endpoints
8. Update the parity checklist in ALL CLAUDE.md files

### Sibling Project Locations
- **WildwoodComponents.Razor**: `../WildwoodComponents.Razor/`
- **Wildwood.JS monorepo**: `C:\Development\Wildwood.JS\Dev\`

## Code Organization

### Partial Class Rule (CRITICAL)
**When a class file exceeds 900 lines, it MUST be separated into partial classes:**

- Split by logical responsibility (e.g., `MyService.cs`, `MyService.Validation.cs`, `MyService.Helpers.cs`)
- Use descriptive suffixes that indicate the partial's purpose
- Keep the primary file (`ClassName.cs`) with core functionality and class declaration
- Move related methods, properties, and nested types to appropriately named partials

**Example structure for a large component:**
```
Components/
    PaymentComponent.razor              # Primary component markup
    PaymentComponent.razor.cs           # Core code-behind, lifecycle, primary methods
    PaymentComponent.Validation.cs      # Validation logic (partial class)
    PaymentComponent.EventHandlers.cs   # Event handler methods (partial class)
```

**Naming convention:**
- Primary: `ClassName.cs` or `ComponentName.razor.cs`
- Partials: `ClassName.{Responsibility}.cs`

**When to split:**
- File exceeds 900 lines
- Clear logical groupings exist (validation, helpers, event handlers, API calls, etc.)
- Multiple developers frequently edit different parts of the same class

### Code-Behind Separation Rule (CRITICAL)
**All Blazor components MUST use code-behind files:**

- **Never** put C# code directly in `.razor` files using `@code { }` blocks
- **Always** create a separate `.razor.cs` code-behind file
- Keep `.razor` files clean with only HTML/Razor markup and minimal inline expressions

**Component structure:**
```
Components/
    AuthenticationComponent.razor      # Markup only - no @code blocks
    AuthenticationComponent.razor.cs   # All C# code, properties, methods
    AuthenticationComponent.razor.css  # Component-scoped styles (optional)
```

**Code-behind file structure:**
```csharp
// AuthenticationComponent.razor.cs
namespace WildwoodComponents.Components.Authentication;

public partial class AuthenticationComponent : BaseWildwoodComponent
{
    [Inject] private IAuthService AuthService { get; set; } = default!;

    [Parameter] public EventCallback<AuthenticationResponse> OnSuccess { get; set; }
    [Parameter] public EventCallback<ComponentErrorEventArgs> OnError { get; set; }

    private LoginModel _loginModel = new();
    private bool _isSubmitting;

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        // Component initialization
    }

    private async Task HandleLogin()
    {
        // Login logic
    }
}
```

**Benefits:**
- Clear separation of concerns (markup vs logic)
- Better IDE support and IntelliSense
- Easier unit testing of component logic
- Consistent with BaseWildwoodComponent architecture
- Improved code organization and maintainability

## Documentation Maintenance Rule (CRITICAL)

**When WildwoodComponents are modified, the developer documentation in WildwoodAdmin MUST be updated to reflect the changes:**

- **Component parameter/event changes**: Update the corresponding component reference page in `WildwoodAdmin/Pages/Docs/Component*.cshtml` with accurate parameter tables, event tables, and code examples
- **New components**: Create a new documentation page following the existing component reference template structure (Overview, Basic Usage, Parameters table, Events table, Examples, Styling, Related Components)
- **Service changes** (AuthenticationService, WildwoodSessionManager, AIService, etc.): Update the relevant Getting Started and Authentication & Session docs
- **Breaking changes**: Clearly document migration paths and mark deprecated items
- **New features**: Document with code examples in the appropriate section
- **Model/DTO changes**: Update API reference pages if the changes affect request/response shapes

**Documentation file locations in WildwoodAdmin:**
- `WildwoodAdmin/Pages/Docs/` - All documentation pages
- `WildwoodAdmin/Pages/Docs/Component*.cshtml` - Component reference pages
- `WildwoodAdmin/Pages/Docs/Auth*.cshtml`, `SessionManagement.cshtml`, `LoginFlow.cshtml` - Auth/session docs
- `WildwoodAdmin/Pages/Docs/GettingStarted.cshtml`, `Installation.cshtml`, `ServiceRegistration.cshtml` - Setup docs
- `WildwoodAdmin/Services/DocsNavigationService.cs` - Sidebar navigation structure (update if adding new pages)

## CSS Theming Architecture

### Core Concept: CSS Custom Properties (Variables)

WildwoodComponents uses a **two-layer theming system**:

1. **Generic `--ww-*` variables** - Defined in WildwoodComponents, used by all components
2. **App-specific overrides** - Consuming apps map their theme variables to `--ww-*` variables

### How It Works

```
WildwoodComponents (--ww-*)  ->  App Override CSS  ->  App Theme (--myapp-*)
```

**Example flow:**
1. `TokenRegistrationComponent` uses `var(--ww-primary, #0d6efd)` for button color
2. AICoach includes `wildwood-theme-override.css` which sets `--ww-primary: var(--aicoach-primary)`
3. AICoach's theme defines `--aicoach-primary: #6366F1`
4. Result: Button renders with AICoach's purple instead of default blue

### Theme Files in WildwoodComponents

```
WildwoodComponents/
    wwwroot/
        css/
            wildwood-themes.css    # Default theme with --ww-* variables
```

### Built-in Themes

The default `wildwood-themes.css` includes three themes:
- **woodland-warm** (default) - Earth-tone yellows and oranges
- **cool-blue** - Earth-tone blues and dark grays
- **fall-colors** - Earth-tone red, orange, and yellow

Themes are activated via `data-theme` attribute:
```html
<body data-theme="cool-blue">
```

### CSS Variable Naming Convention

All WildwoodComponents CSS variables use the `--ww-` prefix:

| Category | Variables |
|----------|-----------|
| **Primary Colors** | `--ww-primary`, `--ww-primary-dark`, `--ww-primary-light` |
| **Status Colors** | `--ww-success`, `--ww-danger`, `--ww-warning`, `--ww-info` |
| **Backgrounds** | `--ww-bg-primary`, `--ww-bg-secondary`, `--ww-bg-tertiary` |
| **Text** | `--ww-text-primary`, `--ww-text-secondary`, `--ww-text-muted`, `--ww-text-inverse` |
| **Forms** | `--ww-input-bg`, `--ww-focus-ring`, `--ww-focus-border` |
| **Alerts** | `--ww-info-bg`, `--ww-danger-bg`, `--ww-success-bg`, `--ww-warning-bg` |
| **Borders** | `--ww-border-color`, `--ww-border-radius`, `--ww-border-radius-sm` |
| **Shadows** | `--ww-shadow`, `--ww-shadow-sm`, `--ww-shadow-lg` |
| **Buttons** | `--ww-btn-primary-text`, `--ww-btn-success-text` |

### How Consuming Apps Override Themes

#### Method 1: Direct Variable Override

Create a CSS file that maps `--ww-*` to your app's variables:

```css
/* my-app-wildwood-override.css */
:root {
    /* Map WildwoodComponents variables to your app's theme */
    --ww-primary: var(--myapp-primary, #6366F1);
    --ww-primary-dark: var(--myapp-primary-dark, #4F46E5);
    --ww-bg-secondary: var(--myapp-bg-secondary, #1E293B);
    --ww-text-primary: var(--myapp-text-primary, #F8FAFC);
    /* ... etc */
}
```

Include this file AFTER your theme CSS:
```html
<link rel="stylesheet" href="my-app-theme.css" />
<link rel="stylesheet" href="my-app-wildwood-override.css" />
```

#### Method 2: CSS Import

```css
/* my-app-theme.css */
@import url("_content/WildwoodComponents/css/wildwood-themes.css");
@import url("my-app-wildwood-override.css");
```

### Component Styling Guidelines

When creating or modifying WildwoodComponents:

1. **Always use `--ww-*` variables** - Never hardcode colors
   ```css
   /* Correct */
   background-color: var(--ww-bg-secondary, #f8f9fa);

   /* Wrong */
   background-color: #f8f9fa;
   ```

2. **Always provide fallback values** - For standalone use without theme override
   ```css
   color: var(--ww-text-primary, #212529);
   ```

3. **Use component-scoped selectors** - Prevent style leakage
   ```css
   .registration-token-component .form-control {
       background-color: var(--ww-input-bg, #ffffff);
   }
   ```

4. **Support both light and dark themes** - Choose fallbacks that work for light theme (Bootstrap defaults)

### Example: TokenRegistrationComponent Styling

```css
.token-validation-section {
    background: var(--ww-bg-secondary, #f8f9fa);
    border: 1px solid var(--ww-border-color, #dee2e6);
    border-radius: var(--ww-border-radius, 0.5rem);
}

.token-validation-section h3 {
    color: var(--ww-text-primary, #212529);
}

.registration-token-component .btn-primary {
    background: var(--ww-primary, #0d6efd);
    border-color: var(--ww-primary, #0d6efd);
    color: var(--ww-btn-primary-text, white);
}
```

### Adding New Theme Variables

When a component needs a new style property:

1. Check if an existing `--ww-*` variable applies
2. If not, add to `wildwood-themes.css` with a clear name
3. Document in this file's variable table
4. Use Bootstrap-like defaults for fallback values

### iOS/MAUI Compatibility

**CRITICAL**: Never use LINQ methods in WildwoodComponents - they cause iOS runtime failures.

```csharp
// DO NOT USE
var item = collection.FirstOrDefault(x => x.Id == id);

// USE INSTEAD
foreach (var item in collection)
{
    if (item.Id == id) return item;
}
```

See the main AICoach copilot-instructions.md for detailed iOS compatibility patterns.

## Component Development

### Base Component Architecture

All WildwoodComponents should inherit from `BaseWildwoodComponent`:

```csharp
@inherits BaseWildwoodComponent

@code {
    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        // Component initialization
    }
}
```

Benefits:
- Automatic error handling with logging and event callbacks
- Built-in loading state management
- Centralized theme management
- Safe JavaScript interop with error handling

### Error Handling

Use `ComponentErrorEventArgs` for error callbacks:

```csharp
[Parameter] public EventCallback<ComponentErrorEventArgs> OnError { get; set; }
```

### Service Registration

Apps register WildwoodComponents services via:

```csharp
builder.Services.AddWildwoodComponents(options =>
{
    options.BaseUrl = "https://api.example.com";
    options.ApiKey = "your-api-key";
});
```

## File Structure

```
WildwoodComponents/
    Components/
        Authentication/
        Forms/
        Registration/
            TokenRegistrationComponent.razor
        ...
    Extensions/
        ServiceCollectionExtensions.cs
    Services/
    wwwroot/
        css/
            wildwood-themes.css    # Default themes
        js/
    WildwoodComponents.csproj
```

## Testing

- Test components on Windows first (fastest iteration)
- Verify on iOS simulator before release
- Check that theme overrides work correctly
- Test with and without consuming app theme overrides
