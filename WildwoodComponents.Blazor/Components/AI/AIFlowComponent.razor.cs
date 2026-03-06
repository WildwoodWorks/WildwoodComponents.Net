using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Blazor.Components.Base;
using WildwoodComponents.Blazor.Models;
using WildwoodComponents.Blazor.Services;

namespace WildwoodComponents.Blazor.Components.AI;

/// <summary>
/// AI Flow execution component for end users.
/// Displays a flow's input form, executes the flow, and shows real-time step progress and output.
/// </summary>
/// <remarks>
/// Split into partial files:
/// - AIFlowComponent.razor.cs - Core: Parameters, fields, lifecycle
/// - AIFlowComponent.Execution.cs - Execution and polling logic
/// - AIFlowComponent.Display.cs - UI helper methods
/// </remarks>
public partial class AIFlowComponent : BaseWildwoodComponent
{
    [Inject] private IAIFlowService FlowService { get; set; } = default!;

    #region Parameters

    [Parameter] public string? AppId { get; set; }
    [Parameter] public string? FlowId { get; set; }
    [Parameter] public string? AuthToken { get; set; }
    [Parameter] public string? ApiBaseUrl { get; set; }
    [Parameter] public EventCallback<FlowExecution> OnExecutionCompleted { get; set; }
    [Parameter] public EventCallback<FlowExecution> OnExecutionFailed { get; set; }
    [Parameter] public EventCallback OnAuthenticationFailed { get; set; }

    #endregion

    #region Private Fields

    private FlowDefinition? CurrentFlow;
    private FlowExecution? CurrentExecution;
    private List<FlowInputField> InputFields = new();
    private Dictionary<string, string> InputValues = new();
    private bool IsExecuting = false;
    private System.Threading.CancellationTokenSource? _pollCts;

    #endregion

    #region Lifecycle

    protected override async Task OnComponentInitializedAsync()
    {
        if (!string.IsNullOrEmpty(AuthToken))
            FlowService.SetAuthToken(AuthToken);

        if (!string.IsNullOrEmpty(ApiBaseUrl))
            FlowService.SetApiBaseUrl(ApiBaseUrl);

        FlowService.AuthenticationFailed += OnServiceAuthFailed;

        if (!string.IsNullOrEmpty(FlowId) && !string.IsNullOrEmpty(AppId))
        {
            await LoadFlowDefinitionAsync();
        }
    }

    protected override async Task OnParametersSetAsync()
    {
        if (!string.IsNullOrEmpty(AuthToken))
            FlowService.SetAuthToken(AuthToken);

        if (!string.IsNullOrEmpty(ApiBaseUrl))
            FlowService.SetApiBaseUrl(ApiBaseUrl);

        await base.OnParametersSetAsync();
    }

    private async void OnServiceAuthFailed(object? sender, EventArgs e)
    {
        if (OnAuthenticationFailed.HasDelegate)
        {
            await InvokeAsync(async () => await OnAuthenticationFailed.InvokeAsync());
        }
    }

    #endregion

    #region Load Flow

    private async Task LoadFlowDefinitionAsync()
    {
        if (string.IsNullOrEmpty(FlowId)) return;

        CurrentFlow = await FlowService.GetFlowDefinitionAsync(AppId!, FlowId);
        if (CurrentFlow != null)
        {
            ParseInputSchema(CurrentFlow.InputSchemaJson);
        }
    }

    private void ParseInputSchema(string? inputSchemaJson)
    {
        InputFields.Clear();
        InputValues.Clear();

        if (string.IsNullOrEmpty(inputSchemaJson)) return;

        try
        {
            using var doc = JsonDocument.Parse(inputSchemaJson);
            if (doc.RootElement.TryGetProperty("fields", out var fieldsElement))
            {
                foreach (var fieldEl in fieldsElement.EnumerateArray())
                {
                    var field = new FlowInputField
                    {
                        Name = fieldEl.GetProperty("name").GetString() ?? "",
                        Type = MapSchemaTypeToInputType(fieldEl.TryGetProperty("type", out var t) ? t.GetString() ?? "string" : "string"),
                        Required = fieldEl.TryGetProperty("required", out var r) && r.GetBoolean(),
                        Label = fieldEl.TryGetProperty("label", out var l) ? l.GetString() ?? "" : "",
                        Placeholder = fieldEl.TryGetProperty("placeholder", out var p) ? p.GetString() : null,
                        DefaultValue = fieldEl.TryGetProperty("default", out var d) ? d.GetString() : null
                    };

                    if (string.IsNullOrEmpty(field.Label))
                        field.Label = field.Name;

                    InputFields.Add(field);
                    InputValues[field.Name] = field.DefaultValue ?? "";
                }
            }
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "Failed to parse flow input schema");
        }
    }

    private static string MapSchemaTypeToInputType(string schemaType)
    {
        return schemaType.ToLowerInvariant() switch
        {
            "string" or "text" => "text",
            "number" or "integer" or "int" => "number",
            "email" => "email",
            "url" => "url",
            "date" => "date",
            "datetime" => "datetime-local",
            "textarea" => "textarea",
            "boolean" or "bool" => "checkbox",
            _ => "text"
        };
    }

    #endregion

    #region IDisposable

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _pollCts?.Cancel();
            _pollCts?.Dispose();
            FlowService.AuthenticationFailed -= OnServiceAuthFailed;
        }
        base.Dispose(disposing);
    }

    #endregion
}
