using Microsoft.AspNetCore.SignalR.Client;

namespace WildwoodComponentsTestSuiteBlazor.Services;

/// <summary>
/// Manages the SignalR connection to the ComponentDebugHub in WildwoodAPI.
/// Connects on login, routes incoming ReceiveDebugEvent messages to DebugEventService.
/// </summary>
public class DebugHubConnectionService : IAsyncDisposable
{
    private readonly IConfiguration _configuration;
    private readonly CircuitStateStorageService _storageService;
    private readonly IDebugEventService _debugEventService;
    private readonly ILogger<DebugHubConnectionService> _logger;

    private HubConnection? _hubConnection;

    public bool IsConnected => _hubConnection?.State == HubConnectionState.Connected;

    public DebugHubConnectionService(
        IConfiguration configuration,
        CircuitStateStorageService storageService,
        IDebugEventService debugEventService,
        ILogger<DebugHubConnectionService> logger)
    {
        _configuration = configuration;
        _storageService = storageService;
        _debugEventService = debugEventService;
        _logger = logger;
    }

    public async Task ConnectAsync()
    {
        if (_hubConnection != null)
        {
            await DisposeAsync();
        }

        var token = await _storageService.GetAuthTokenAsync();
        if (string.IsNullOrEmpty(token))
        {
            _logger.LogWarning("Cannot connect to debug hub: no auth token available");
            return;
        }

        var baseUrl = _configuration["WildwoodAPI:BaseUrl"] ?? "https://localhost:7046";
        var hubUrl = $"{baseUrl}/hubs/component-debug";

        _hubConnection = new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                options.AccessTokenProvider = () => Task.FromResult<string?>(token);
            })
            .WithAutomaticReconnect()
            .Build();

        _hubConnection.On<string, string>("ReceiveDebugEvent", (eventType, payload) =>
        {
            _debugEventService.EmitSignalRMessage(new SignalRMessage
            {
                HubName = "ComponentDebugHub",
                MethodName = eventType,
                Payload = payload,
                Direction = "Received"
            });
        });

        _hubConnection.Reconnecting += error =>
        {
            _logger.LogWarning(error, "Debug hub connection lost, reconnecting...");
            return Task.CompletedTask;
        };

        _hubConnection.Reconnected += connectionId =>
        {
            _logger.LogInformation("Debug hub reconnected with ID: {ConnectionId}", connectionId);
            return Task.CompletedTask;
        };

        _hubConnection.Closed += error =>
        {
            if (error != null)
                _logger.LogWarning(error, "Debug hub connection closed");
            return Task.CompletedTask;
        };

        try
        {
            await _hubConnection.StartAsync();
            _logger.LogInformation("Connected to ComponentDebugHub at {Url}", hubUrl);

            // Join a debug session using a unique session ID
            var sessionId = $"test-suite-{Environment.MachineName}";
            await _hubConnection.InvokeAsync("JoinDebugSession", sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to connect to ComponentDebugHub at {Url}. Hub features will be unavailable.", hubUrl);
        }
    }

    public async Task DisconnectAsync()
    {
        if (_hubConnection != null)
        {
            try
            {
                var sessionId = $"test-suite-{Environment.MachineName}";
                await _hubConnection.InvokeAsync("LeaveDebugSession", sessionId);
                await _hubConnection.StopAsync();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error during debug hub disconnect");
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_hubConnection != null)
        {
            await _hubConnection.DisposeAsync();
            _hubConnection = null;
        }
    }
}
