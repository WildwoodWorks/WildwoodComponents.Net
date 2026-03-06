using Microsoft.AspNetCore.Components.Authorization;
using WildwoodComponents.Blazor.Extensions;
using WildwoodComponents.Blazor.Services;
using WildwoodComponentsTestSuiteBlazor.Components;
using WildwoodComponentsTestSuiteBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

// Add Blazor Server services
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Authentication + Authorization
// Cookie scheme satisfies the middleware during SSR; actual auth uses AuthenticationStateProvider
builder.Services.AddAuthentication("BlazorServer")
    .AddCookie("BlazorServer", options => options.LoginPath = "/login");
builder.Services.AddAuthorizationCore();
builder.Services.AddCascadingAuthenticationState();

// Register WildwoodComponents services (reads BaseUrl, AppId, etc. from config)
builder.Services.AddWildwoodComponents(builder.Configuration, "WildwoodAPI");

// Circuit-state storage (overrides the default ILocalStorageService from WildwoodComponents)
builder.Services.AddScoped<CircuitStateStorageService>();
builder.Services.AddScoped<ILocalStorageService>(sp => sp.GetRequiredService<CircuitStateStorageService>());

// Debug infrastructure
builder.Services.AddScoped<IDebugEventService, DebugEventService>();
builder.Services.AddScoped<ComponentLifecycleTracker>();
builder.Services.AddTransient<DebugHttpInterceptor>();

// Intercept ALL HttpClient traffic from WildwoodComponents services
builder.Services.ConfigureHttpClientDefaults(clientBuilder =>
{
    clientBuilder.AddHttpMessageHandler<DebugHttpInterceptor>();
});

// Named HttpClient for our own auth calls
var apiBaseUrl = builder.Configuration["WildwoodAPI:BaseUrl"] ?? "https://localhost:7046";
builder.Services.AddHttpClient("WildwoodAPI", client =>
{
    client.BaseAddress = new Uri(apiBaseUrl);
});

// Authentication services
builder.Services.AddScoped<TestSuiteAuthService>();
builder.Services.AddScoped<AuthenticationStateProvider, TestSuiteAuthStateProvider>();

// SignalR debug hub connection
builder.Services.AddScoped<DebugHubConnectionService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
