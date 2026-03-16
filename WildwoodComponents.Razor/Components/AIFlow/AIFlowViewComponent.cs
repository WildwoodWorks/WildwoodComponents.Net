using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Razor.Models;
using WildwoodComponents.Razor.Services;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Razor.Components.AIFlow;

/// <summary>
/// ViewComponent that renders an AI flow execution interface.
/// Users can select flows, provide inputs, and monitor execution progress.
/// Razor Pages equivalent of WildwoodComponents.Blazor AIFlowComponent.
/// </summary>
public class AIFlowViewComponent : ViewComponent
{
    private readonly IWildwoodAIFlowService _flowService;
    private readonly ILogger<AIFlowViewComponent> _logger;

    public AIFlowViewComponent(IWildwoodAIFlowService flowService, ILogger<AIFlowViewComponent> logger)
    {
        _flowService = flowService;
        _logger = logger;
    }

    public async Task<IViewComponentResult> InvokeAsync(
        string appId,
        string? flowId = null,
        string proxyBaseUrl = "/api/wildwood-ai-flow")
    {
        var flows = new List<FlowDefinitionDto>();
        FlowDefinitionDto? selectedFlow = null;

        try
        {
            flows = await _flowService.GetFlowDefinitionsAsync(appId);

            if (!string.IsNullOrEmpty(flowId))
            {
                selectedFlow = await _flowService.GetFlowDefinitionAsync(appId, flowId);
            }

            // Parse InputSchemaJson into InputFields for each flow definition
            foreach (var flow in flows)
                ParseInputSchema(flow);
            if (selectedFlow != null)
                ParseInputSchema(selectedFlow);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load AI flow definitions for app {AppId}", appId);
        }

        var model = new AIFlowViewModel
        {
            AppId = appId,
            FlowId = flowId,
            ProxyBaseUrl = proxyBaseUrl.TrimEnd('/'),
            AvailableFlows = flows,
            SelectedFlow = selectedFlow
        };

        return View(model);
    }

    /// <summary>
    /// Parses InputSchemaJson into strongly-typed InputFields list.
    /// Mirrors the Blazor AIFlowComponent.ParseInputSchema logic.
    /// </summary>
    private void ParseInputSchema(FlowDefinitionDto flow)
    {
        if (string.IsNullOrEmpty(flow.InputSchemaJson) || flow.InputFields.Count > 0)
            return;

        try
        {
            using var doc = JsonDocument.Parse(flow.InputSchemaJson);
            var root = doc.RootElement;

            if (root.TryGetProperty("properties", out var properties))
            {
                var requiredFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (root.TryGetProperty("required", out var requiredArray) && requiredArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in requiredArray.EnumerateArray())
                        requiredFields.Add(item.GetString() ?? "");
                }

                foreach (var prop in properties.EnumerateObject())
                {
                    var field = new FlowInputField
                    {
                        Name = prop.Name,
                        Required = requiredFields.Contains(prop.Name)
                    };

                    if (prop.Value.TryGetProperty("title", out var title))
                        field.Label = title.GetString() ?? prop.Name;
                    if (prop.Value.TryGetProperty("description", out var desc))
                        field.Placeholder = desc.GetString();
                    if (prop.Value.TryGetProperty("default", out var def))
                        field.DefaultValue = def.ToString();
                    if (prop.Value.TryGetProperty("type", out var type))
                        field.Type = MapSchemaTypeToInputType(type.GetString() ?? "string");

                    flow.InputFields.Add(field);
                }
            }
        }
        catch (Exception ex)
        {
            // Invalid schema JSON - leave InputFields empty
            _logger.LogDebug(ex, "Failed to parse InputSchemaJson for flow {FlowName}", flow.Name ?? "Unknown");
        }
    }

    private static string MapSchemaTypeToInputType(string schemaType)
    {
        return schemaType.ToLowerInvariant() switch
        {
            "integer" or "number" => "number",
            "boolean" => "checkbox",
            _ => "text"
        };
    }
}
