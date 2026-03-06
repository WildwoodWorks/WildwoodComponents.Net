using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Blazor.Models;

namespace WildwoodComponents.Blazor.Components.AI;

/// <summary>
/// Partial class containing execution and polling logic for AIFlowComponent.
/// </summary>
public partial class AIFlowComponent
{
    private async Task ExecuteFlowAsync()
    {
        if (string.IsNullOrEmpty(FlowId) || string.IsNullOrEmpty(AppId) || IsExecuting) return;

        IsExecuting = true;
        CurrentExecution = null;
        StateHasChanged();

        try
        {
            // Build input JSON from form values
            string? inputJson = null;
            if (InputValues.Count > 0)
            {
                inputJson = JsonSerializer.Serialize(InputValues);
            }

            // Start execution
            CurrentExecution = await FlowService.ExecuteFlowAsync(AppId!, FlowId, inputJson);
            StateHasChanged();

            // Poll for status if running
            if (CurrentExecution.Status == "running" || CurrentExecution.Status == "pending")
            {
                await PollExecutionStatusAsync(CurrentExecution.Id);
            }
            else if (CurrentExecution.Status == "completed")
            {
                if (OnExecutionCompleted.HasDelegate)
                    await OnExecutionCompleted.InvokeAsync(CurrentExecution);
            }
            else if (CurrentExecution.Status == "failed")
            {
                if (OnExecutionFailed.HasDelegate)
                    await OnExecutionFailed.InvokeAsync(CurrentExecution);
            }
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Error executing flow {FlowId}", FlowId);
            CurrentExecution = new FlowExecution
            {
                Status = "failed",
                ErrorMessage = ex.Message
            };

            if (OnExecutionFailed.HasDelegate)
                await OnExecutionFailed.InvokeAsync(CurrentExecution);
        }
        finally
        {
            IsExecuting = false;
            StateHasChanged();
        }
    }

    private async Task PollExecutionStatusAsync(string executionId)
    {
        _pollCts?.Cancel();
        _pollCts = new CancellationTokenSource();
        var token = _pollCts.Token;

        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(1500, token);

                var status = await FlowService.GetExecutionStatusAsync(AppId!, executionId);
                if (status == null) break;

                CurrentExecution = status;
                await InvokeAsync(StateHasChanged);

                if (status.Status == "completed")
                {
                    if (OnExecutionCompleted.HasDelegate)
                        await InvokeAsync(async () => await OnExecutionCompleted.InvokeAsync(status));
                    break;
                }
                else if (status.Status == "failed" || status.Status == "cancelled" || status.Status == "timed_out")
                {
                    if (OnExecutionFailed.HasDelegate)
                        await InvokeAsync(async () => await OnExecutionFailed.InvokeAsync(status));
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when cancelled
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Error polling execution status for {ExecutionId}", executionId);
        }
    }

    private async Task CancelExecutionAsync()
    {
        if (CurrentExecution == null) return;

        _pollCts?.Cancel();
        await FlowService.CancelExecutionAsync(AppId!, CurrentExecution.Id);

        CurrentExecution.Status = "cancelled";
        IsExecuting = false;
        StateHasChanged();
    }
}
