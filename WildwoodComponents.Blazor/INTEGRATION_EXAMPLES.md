# WildwoodComponents Integration Examples

## Example Project Structures

### Blazor Server Integration

```
MyBlazorApp/
├── Program.cs                          # Service registration
├── Pages/
│   ├── _Host.cshtml                   # Root page
│   ├── Authentication.razor           # Auth page using WildwoodComponents
│   └── Dashboard.razor                # Protected dashboard
├── Components/
│   ├── Custom/
│   │   └── MyCustomComponent.razor    # Custom component inheriting BaseWildwoodComponent
│   └── Shared/
│       ├── MainLayout.razor
│       └── NavMenu.razor
└── appsettings.json                   # Configuration
```

**Program.cs:**
```csharp
var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();

// Enhanced WildwoodComponents registration (one line!)
builder.Services.AddWildwoodComponents(config =>
{
    config.BaseUrl = builder.Configuration.GetConnectionString("WildwoodAPI") ?? "https://localhost:5291";
    config.EnableDetailedErrors = builder.Environment.IsDevelopment();
    config.EnableRetry = true;
    config.MaxRetryAttempts = 3;
});

var app = builder.Build();

// Configure pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();
```

**Authentication.razor:**
```razor
@page "/auth"
@using WildwoodComponents.Components.Authentication

<PageTitle>Sign In</PageTitle>

<div class="container mt-5">
    <div class="row justify-content-center">
        <div class="col-md-6">
            <div class="card">
                <div class="card-header">
                    <h3 class="text-center">Welcome</h3>
                </div>
                <div class="card-body">
                    <AuthenticationComponent 
                        AppId="@AppConfiguration.AppId"
                        ShowPasswordField="true"
                        ShowLicenseToken="false"
                        OnAuthenticationSuccess="@HandleAuthSuccess"
                        OnAuthenticationError="@HandleAuthError"
                        CssClass="auth-component"
                        ShowLoadingStates="true" />
                </div>
            </div>
        </div>
    </div>
</div>

@code {
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [Inject] private ILogger<Authentication> Logger { get; set; } = default!;
    
    private async Task HandleAuthSuccess(AuthenticationResponse response)
    {
        Logger.LogInformation("User {Email} authenticated successfully", response.User.Email);
        
        // Store authentication token
        await StoreAuthTokenAsync(response.Token);
        
        // Navigate to dashboard
        Navigation.NavigateTo("/dashboard");
    }
    
    private async Task HandleAuthError(ComponentErrorEventArgs error)
    {
        Logger.LogError(error.Exception, "Authentication failed: {Context}", error.Context);
        
        // Show user-friendly error
        await ShowToastAsync(error.UserMessage ?? "Authentication failed. Please try again.", "error");
    }
    
    private async Task StoreAuthTokenAsync(string token)
    {
        // Store token in local storage or secure storage
        await LocalStorage.SetItemAsync("auth_token", token);
    }
    
    private async Task ShowToastAsync(string message, string type)
    {
        // Show toast notification (implement based on your UI library)
        await JSRuntime.InvokeVoidAsync("showToast", message, type);
    }
}
```

### .NET MAUI Integration

```
MyMauiApp/
├── MauiProgram.cs                     # Service registration
├── MainPage.xaml                     # MAUI page with BlazorWebView
├── Components/
│   ├── Pages/
│   │   ├── Index.razor               # Home page
│   │   └── Profile.razor             # User profile
│   └── Shared/
│       ├── MainLayout.razor
│       └── NavMenu.razor
├── Platforms/
│   ├── Android/
│   ├── iOS/
│   ├── MacCatalyst/
│   └── Windows/
└── wwwroot/
    └── index.html                    # Blazor app entry point
```

**MauiProgram.cs:**
```csharp
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

        // Add Blazor services
        builder.Services.AddMauiBlazorWebView();

        // Enhanced WildwoodComponents registration
        builder.Services.AddWildwoodComponents(config =>
        {
            config.BaseUrl = "https://your-production-api.com";
            config.EnableDetailedErrors = true; // Safe for mobile apps
            config.RequestTimeoutSeconds = 30;
            config.EnableCaching = true;
            config.CacheDurationMinutes = 10;
        });

        // Platform-specific services
#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
```

### Custom Component Example

**MyCustomComponent.razor:**
```razor
@using WildwoodComponents.Components.Base
@inherits BaseWildwoodComponent
@inject ILogger<MyCustomComponent> Logger

<div class="@GetRootCssClasses()">
    <div class="card">
        <div class="card-header d-flex justify-content-between align-items-center">
            <h5 class="mb-0">@Title</h5>
            @if (IsLoading && ShowLoadingStates)
            {
                <div class="spinner-border spinner-border-sm" role="status">
                    <span class="visually-hidden">Loading...</span>
                </div>
            }
        </div>
        
        <div class="card-body">
            @if (!string.IsNullOrEmpty(ErrorMessage) && EnableErrorHandling)
            {
                <div class="alert alert-danger alert-dismissible" role="alert">
                    <i class="bi bi-exclamation-triangle-fill me-2"></i>
                    @ErrorMessage
                    <button type="button" class="btn-close" @onclick="ClearErrorAsync"></button>
                </div>
            }
            else if (!IsLoading)
            {
                @if (Items?.Any() == true)
                {
                    <div class="list-group">
                        @foreach (var item in Items)
                        {
                            <div class="list-group-item d-flex justify-content-between align-items-center">
                                <span>@item.Name</span>
                                <button class="btn btn-sm btn-outline-primary" 
                                        @onclick="() => HandleItemActionAsync(item)"
                                        disabled="@IsLoading">
                                    Action
                                </button>
                            </div>
                        }
                    </div>
                }
                else
                {
                    <div class="text-muted text-center py-4">
                        <i class="bi bi-inbox fs-1"></i>
                        <p class="mt-2">No items available</p>
                        <button class="btn btn-primary" @onclick="LoadDataAsync" disabled="@IsLoading">
                            @if (IsLoading)
                            {
                                <span class="spinner-border spinner-border-sm me-2"></span>
                            }
                            Refresh
                        </button>
                    </div>
                }
            }
        </div>
    </div>
</div>

@code {
    [Parameter] public string Title { get; set; } = "My Component";
    [Parameter] public EventCallback<MyItem> OnItemAction { get; set; }
    [Parameter] public EventCallback<List<MyItem>> OnDataLoaded { get; set; }
    
    private List<MyItem>? Items { get; set; }
    
    protected override async Task OnComponentInitializedAsync()
    {
        // Automatic error handling and loading states
        await ExecuteAsync(async () =>
        {
            Items = await LoadInitialDataAsync();
            await OnDataLoaded.InvokeAsync(Items);
        }, "Loading initial data");
    }
    
    private async Task LoadDataAsync()
    {
        // ExecuteAsync provides automatic error handling and loading states
        Items = await ExecuteAsync(async () =>
        {
            var data = await ApiService.GetItemsAsync();
            Logger.LogInformation("Loaded {Count} items", data.Count);
            return data;
        }, "Refreshing data");
        
        if (Items != null)
        {
            await OnDataLoaded.InvokeAsync(Items);
        }
    }
    
    private async Task HandleItemActionAsync(MyItem item)
    {
        await ExecuteAsync(async () =>
        {
            await ApiService.ProcessItemAsync(item.Id);
            await OnItemAction.InvokeAsync(item);
            await LoadDataAsync(); // Refresh data
        }, $"Processing item {item.Name}");
    }
    
    private async Task<List<MyItem>> LoadInitialDataAsync()
    {
        // Simulate API call
        await Task.Delay(1000);
        return new List<MyItem>
        {
            new MyItem { Id = 1, Name = "Item 1" },
            new MyItem { Id = 2, Name = "Item 2" },
            new MyItem { Id = 3, Name = "Item 3" }
        };
    }
    
    public class MyItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }
}
```

## Configuration Examples

### appsettings.json
```json
{
  "ConnectionStrings": {
    "WildwoodAPI": "https://api.wildwoodcomponents.com"
  },
  "WildwoodComponents": {
    "AppId": "your-app-id",
    "ApiKey": "your-api-key",
    "EnableDetailedErrors": true,
    "RequestTimeoutSeconds": 30,
    "EnableRetry": true,
    "MaxRetryAttempts": 3,
    "EnableCaching": true,
    "CacheDurationMinutes": 10
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "WildwoodComponents": "Debug",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```

### Environment-Specific Configuration

**appsettings.Development.json:**
```json
{
  "ConnectionStrings": {
    "WildwoodAPI": "https://localhost:5291"
  },
  "WildwoodComponents": {
    "EnableDetailedErrors": true,
    "RequestTimeoutSeconds": 60
  },
  "Logging": {
    "LogLevel": {
      "WildwoodComponents": "Trace"
    }
  }
}
```

**appsettings.Production.json:**
```json
{
  "WildwoodComponents": {
    "EnableDetailedErrors": false,
    "EnableRetry": true,
    "MaxRetryAttempts": 3,
    "EnableCaching": true,
    "CacheDurationMinutes": 30
  },
  "Logging": {
    "LogLevel": {
      "WildwoodComponents": "Warning"
    }
  }
}
```

## Best Practices Implementation

### Error Handling Pattern
```csharp
// Global error handler
public class GlobalErrorHandler : IComponentErrorHandler
{
    private readonly ILogger<GlobalErrorHandler> _logger;
    private readonly IToastService _toastService;
    
    public GlobalErrorHandler(ILogger<GlobalErrorHandler> logger, IToastService toastService)
    {
        _logger = logger;
        _toastService = toastService;
    }
    
    public async Task HandleErrorAsync(ComponentErrorEventArgs error)
    {
        _logger.LogError(error.Exception, 
            "Component error in {ComponentType}: {Context}", 
            error.ComponentType, error.Context);
        
        // Show user-friendly notification
        await _toastService.ShowErrorAsync(error.UserMessage ?? "An unexpected error occurred");
    }
}

// Register in Program.cs
builder.Services.AddSingleton<IComponentErrorHandler, GlobalErrorHandler>();
```

### Theme Configuration
```csharp
// Custom theme service
public class CustomThemeService : IThemeService
{
    public async Task SetThemeAsync(string themeName)
    {
        await JSRuntime.InvokeVoidAsync("setTheme", themeName);
        await LocalStorage.SetItemAsync("preferred-theme", themeName);
    }
    
    public async Task<string> GetCurrentThemeAsync()
    {
        return await LocalStorage.GetItemAsync<string>("preferred-theme") ?? "light";
    }
}
```

### Testing Configuration
```csharp
// Unit test setup
[TestFixture]
public class WildwoodComponentsTests
{
    private TestContext _testContext = default!;
    
    [SetUp]
    public void Setup()
    {
        _testContext = new TestContext();
        
        // Register WildwoodComponents for testing
        _testContext.Services.AddWildwoodComponents(config =>
        {
            config.BaseUrl = "https://test-api.example.com";
            config.EnableDetailedErrors = true;
        });
        
        // Mock services
        _testContext.Services.AddSingleton<IApiService, MockApiService>();
    }
    
    [Test]
    public void AuthenticationComponent_RendersCorrectly()
    {
        // Arrange
        var component = _testContext.RenderComponent<AuthenticationComponent>(parameters => parameters
            .Add(p => p.AppId, "test-app-id")
            .Add(p => p.ShowPasswordField, true));
        
        // Assert
        Assert.IsNotNull(component.Find("form"));
        Assert.IsNotNull(component.Find("input[type='email']"));
        Assert.IsNotNull(component.Find("input[type='password']"));
    }
}
```

This enhanced architecture provides a much cleaner, more maintainable, and developer-friendly experience while maintaining all the powerful features of WildwoodComponents!
