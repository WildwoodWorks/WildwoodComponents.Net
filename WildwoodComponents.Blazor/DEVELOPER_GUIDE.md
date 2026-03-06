# WildwoodComponents Developer Guide

## Quick Integration Guide

### 1. Service Registration (One Line!)

**Before (Complex):**
```csharp
// 300+ lines of reflection-based service registration...
var wildwoodAssembly = AppDomain.CurrentDomain.GetAssemblies()...
// Complex type discovery and manual service binding
```

**After (Simple):**
```csharp
// Single line registration
builder.Services.AddWildwoodComponents("https://your-api-url");

// Or with configuration
builder.Services.AddWildwoodComponents(config =>
{
    config.BaseUrl = "https://your-api-url";
    config.EnableDetailedErrors = true;
});
```

### 2. Component Usage Patterns

#### Creating Custom Components

```razor
@using WildwoodComponents.Components.Base
@inherits BaseWildwoodComponent

<div class="@GetRootCssClasses()">
    @if (IsLoading && ShowLoadingStates)
    {
        <LoadingSpinner />
    }
    else if (HasError)
    {
        <ErrorDisplay Message="@ErrorMessage" />
    }
    else
    {
        <!-- Your component content -->
        <button @onclick="HandleActionAsync" disabled="@IsLoading">
            @if (IsLoading)
            {
                <span class="spinner-border spinner-border-sm me-2"></span>
            }
            Perform Action
        </button>
    }
</div>

@code {
    [Parameter] public string? ActionText { get; set; }
    [Parameter] public EventCallback<string> OnActionComplete { get; set; }
    
    protected override async Task OnComponentInitializedAsync()
    {
        // Automatic error handling and loading states
        await ExecuteAsync(async () =>
        {
            // Your initialization code
            await InitializeDataAsync();
        }, "Initializing component");
    }
    
    private async Task HandleActionAsync()
    {
        // Automatic error handling, loading states, and logging
        var result = await ExecuteAsync(async () =>
        {
            var apiResult = await CallSomeApiAsync();
            await OnActionComplete.InvokeAsync(apiResult);
            return apiResult;
        }, "Performing action");
        
        // Result is automatically handled - success or failure
    }
    
    private async Task<string> CallSomeApiAsync()
    {
        // Your API call logic
        await Task.Delay(2000); // Simulate API call
        return "Success!";
    }
}
```

#### Using Enhanced Authentication Component

```razor
@using WildwoodComponents.Components.Authentication

<AuthenticationComponent 
    AppId="your-app-id"
    ShowPasswordField="true"
    ShowLicenseToken="false"
    OnAuthenticationSuccess="@HandleAuthSuccess"
    OnAuthenticationError="@HandleAuthError"
    CssClass="auth-container"
    ShowLoadingStates="true" />

@code {
    private async Task HandleAuthSuccess(AuthenticationResponse response)
    {
        Logger.LogInformation("User authenticated: {Email}", response.User.Email);
        // Navigate to dashboard or handle success
    }
    
    private async Task HandleAuthError(ComponentErrorEventArgs error)
    {
        Logger.LogError(error.Exception, "Authentication failed in {Component}: {Context}", 
            error.ComponentType, error.Context);
        // Show user-friendly error message
        await ShowToastAsync($"Authentication failed: {error.UserMessage}", "error");
    }
}
```

### 3. Error Handling Patterns

#### Component Error Events

```csharp
// Use ComponentErrorEventArgs instead of string
[Parameter] public EventCallback<ComponentErrorEventArgs> OnError { get; set; }

// In error handling
private async Task HandleComponentError(ComponentErrorEventArgs args)
{
    // Rich error information available
    Logger.LogError(args.Exception, 
        "Error in {ComponentType} during {Context}: {Message}", 
        args.ComponentType, args.Context, args.Exception.Message);
    
    // User-friendly message
    await ShowUserNotificationAsync(args.UserMessage ?? "An error occurred");
}
```

#### Safe Async Operations

```csharp
// Pattern 1: With return value
private async Task<T> LoadDataAsync<T>()
{
    return await ExecuteAsync(async () =>
    {
        var data = await ApiService.GetDataAsync<T>();
        return data;
    }, "Loading data");
}

// Pattern 2: Void operations
private async Task SaveDataAsync()
{
    await ExecuteAsync(async () =>
    {
        await ApiService.SaveDataAsync(currentData);
        await ShowSuccessMessageAsync("Data saved successfully");
    }, "Saving data");
}

// Pattern 3: With custom error handling
private async Task ComplexOperationAsync()
{
    await ExecuteAsync(async () =>
    {
        await Step1Async();
        await Step2Async();
        await Step3Async();
    }, "Complex operation", async (ex) =>
    {
        // Custom error handler
        Logger.LogError(ex, "Complex operation failed at step");
        await RollbackChangesAsync();
    });
}
```

### 4. JavaScript Interop

```csharp
// Safe JavaScript calls with automatic error handling
protected override async Task OnAfterRenderAsync(bool firstRender)
{
    if (firstRender)
    {
        // With return value
        var result = await InvokeJSAsync<string>("initializeComponent", elementId, options);
        
        // Void calls
        await InvokeJSVoidAsync("setupEventListeners", elementId);
    }
}

// JavaScript modules
private async Task LoadJavaScriptModuleAsync()
{
    await InvokeJSVoidAsync("import", "./Components/MyComponent.razor.js");
}
```

### 5. Theme Integration

```csharp
// Components automatically get theme support
protected override async Task OnComponentInitializedAsync()
{
    // Theme is automatically loaded and managed
    // Subscribe to theme changes if needed
    ThemeService.ThemeChanged += OnThemeChangedAsync;
}

protected virtual async Task OnThemeChangedAsync(string newTheme)
{
    // React to theme changes
    await InvokeAsync(StateHasChanged);
}

public override void Dispose()
{
    ThemeService.ThemeChanged -= OnThemeChangedAsync;
    base.Dispose();
}
```

### 6. Advanced Patterns

#### Component Lifecycle

```csharp
public class MyAdvancedComponent : BaseWildwoodComponent
{
    protected override async Task OnComponentInitializedAsync()
    {
        // Called automatically with error handling
        await LoadInitialDataAsync();
    }
    
    protected override async Task OnParametersSetAsync()
    {
        // Handle parameter changes
        if (ParameterChanged)
        {
            await RefreshDataAsync();
        }
    }
    
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await InitializeJavaScriptAsync();
        }
    }
    
    protected override bool ShouldRender()
    {
        // Optimize rendering
        return !IsLoading && !HasError;
    }
}
```

#### Custom Loading States

```csharp
private async Task CustomLoadingOperationAsync()
{
    // Start custom loading state
    await SetLoadingAsync(true, "Processing your request...");
    
    try
    {
        await LongRunningOperationAsync();
        
        // Update loading message
        await SetLoadingAsync(true, "Finalizing...");
        
        await FinalizationStepAsync();
    }
    finally
    {
        await SetLoadingAsync(false);
    }
}
```

## Migration Checklist

### From Previous WildwoodComponents

- [ ] Replace complex service registration with `AddWildwoodComponents()`
- [ ] Update error callback parameters from `string` to `ComponentErrorEventArgs`
- [ ] Remove manual error handling code (now automatic)
- [ ] Remove manual loading state management (now automatic)
- [ ] Update component inheritance to use `BaseWildwoodComponent`
- [ ] Replace direct property access with base class methods
- [ ] Update JavaScript interop to use safe base class methods

### Testing Enhanced Components

```csharp
[Test]
public async Task Component_HandlesErrorsGracefully()
{
    // Arrange
    var component = RenderComponent<MyComponent>();
    
    // Act - simulate error condition
    await component.InvokeAsync(() => component.Instance.TriggerErrorAsync());
    
    // Assert - error is handled automatically
    Assert.False(component.Instance.HasError); // Base class handled it
    Assert.NotNull(component.Find(".error-message")); // Error displayed
}

[Test]
public async Task Component_ShowsLoadingStates()
{
    // Arrange
    var component = RenderComponent<MyComponent>();
    
    // Act
    var loadingTask = component.InvokeAsync(() => component.Instance.LoadDataAsync());
    
    // Assert - loading state shown
    Assert.True(component.Instance.IsLoading);
    Assert.NotNull(component.Find(".loading-spinner"));
    
    await loadingTask;
    
    // Assert - loading state cleared
    Assert.False(component.Instance.IsLoading);
}
```

## Performance Tips

1. **Use ExecuteAsync for all async operations** - provides automatic error handling and loading states
2. **Leverage automatic loading states** - reduces boilerplate and improves UX
3. **Enable caching in configuration** - reduces API calls for configuration data
4. **Use proper disposal patterns** - base class handles most cleanup automatically
5. **Implement ShouldRender() when needed** - optimize rendering in data-heavy components

## Common Pitfalls

1. **Don't mix error handling patterns** - use either base class automatic handling or custom, not both
2. **Don't manually manage loading states** - let base class handle them for consistency
3. **Remember to await ExecuteAsync** - it's an async method that handles errors
4. **Use ComponentErrorEventArgs** - string error callbacks are deprecated
5. **Don't override error properties directly** - use SetErrorAsync() and ClearErrorAsync() methods

## Best Practices

1. **Always inherit from BaseWildwoodComponent** for new components
2. **Use ExecuteAsync for all async operations** that could fail
3. **Implement proper disposal** when subscribing to events
4. **Follow consistent naming conventions** for error handling and loading states
5. **Provide meaningful operation context** in ExecuteAsync calls for better logging
6. **Use theme-aware CSS classes** for consistent styling across components
7. **Implement accessibility features** using base class helper methods
