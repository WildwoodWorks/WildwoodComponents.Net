using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using WildwoodComponents.Blazor.Services;
using WildwoodComponents.Blazor.Models;

namespace WildwoodComponents.Blazor.Components.Base
{
    /// <summary>
    /// Base component that provides common functionality for all WildwoodComponents.
    /// This class handles theme management, error handling, and provides common services.
    /// </summary>
    public abstract class BaseWildwoodComponent : ComponentBase, IDisposable
    {
        #region Injected Services
        
        [Inject] protected IComponentThemeService? ThemeService { get; set; }
        [Inject] protected ILocalStorageService? LocalStorage { get; set; }
        [Inject] protected IJSRuntime JSRuntime { get; set; } = default!;
        [Inject] protected ILogger<BaseWildwoodComponent>? Logger { get; set; }
        
        #endregion

        #region Common Parameters
        
        /// <summary>
        /// CSS class to apply to the component's root element.
        /// </summary>
        [Parameter] public string? CssClass { get; set; }
        
        /// <summary>
        /// Additional attributes to apply to the component's root element.
        /// </summary>
        [Parameter(CaptureUnmatchedValues = true)] 
        public Dictionary<string, object>? AdditionalAttributes { get; set; }
        
        /// <summary>
        /// Whether to show loading states for async operations.
        /// </summary>
        [Parameter] public bool ShowLoadingStates { get; set; } = true;
        
        /// <summary>
        /// Whether to enable automatic error handling and display.
        /// </summary>
        [Parameter] public bool EnableErrorHandling { get; set; } = true;
        
        /// <summary>
        /// Event callback for when an error occurs in the component.
        /// </summary>
        [Parameter] public EventCallback<ComponentErrorEventArgs> OnError { get; set; }
        
        /// <summary>
        /// Event callback for when the component loading state changes.
        /// </summary>
        [Parameter] public EventCallback<bool> OnLoadingStateChanged { get; set; }
        
        #endregion

        #region Protected Properties
        
        /// <summary>
        /// The current theme applied to this component.
        /// </summary>
        protected ComponentTheme? CurrentTheme { get; private set; }
        
        /// <summary>
        /// Whether the component is currently loading.
        /// </summary>
        protected bool IsLoading { get; private set; }
        
        /// <summary>
        /// The current error message, if any.
        /// </summary>
        protected string? ErrorMessage { get; private set; }
        
        /// <summary>
        /// The component's unique identifier for JavaScript interop.
        /// </summary>
        protected string ComponentId { get; } = Guid.NewGuid().ToString("N")[..8];
        
        #endregion

        #region Lifecycle Methods
        
        protected override async Task OnInitializedAsync()
        {
            try
            {
                // Load the current theme if theme service is available
                if (ThemeService != null)
                {
                    CurrentTheme = await ThemeService.GetCurrentThemeAsync();
                    ThemeService.ThemeChanged += OnThemeChanged;
                }
                
                // Call the derived component's initialization
                await OnComponentInitializedAsync();
            }
            catch (Exception ex)
            {
                await HandleErrorAsync(ex, "Failed to initialize component");
            }
        }
        
        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            try
            {
                if (firstRender)
                {
                    await OnComponentFirstRenderAsync();
                }
                else
                {
                    await OnComponentRenderedAsync();
                }
            }
            catch (Exception ex)
            {
                await HandleErrorAsync(ex, "Error during component render");
            }
        }
        
        #endregion

        #region Virtual Methods for Derived Components
        
        /// <summary>
        /// Called during component initialization. Override in derived components.
        /// </summary>
        protected virtual Task OnComponentInitializedAsync() => Task.CompletedTask;
        
        /// <summary>
        /// Called after the component's first render. Override in derived components.
        /// </summary>
        protected virtual Task OnComponentFirstRenderAsync() => Task.CompletedTask;
        
        /// <summary>
        /// Called after each render (except the first). Override in derived components.
        /// </summary>
        protected virtual Task OnComponentRenderedAsync() => Task.CompletedTask;
        
        /// <summary>
        /// Called when an error occurs. Override to provide custom error handling.
        /// </summary>
        protected virtual Task OnComponentErrorAsync(Exception exception, string context) => Task.CompletedTask;
        
        #endregion

        #region Protected Helper Methods
        
        /// <summary>
        /// Sets the loading state and optionally triggers the loading state changed event.
        /// </summary>
        protected async Task SetLoadingAsync(bool isLoading)
        {
            if (IsLoading != isLoading)
            {
                IsLoading = isLoading;
                
                if (OnLoadingStateChanged.HasDelegate)
                {
                    await OnLoadingStateChanged.InvokeAsync(isLoading);
                }
                
                StateHasChanged();
            }
        }
        
        /// <summary>
        /// Handles errors in a consistent way across all components.
        /// </summary>
        protected async Task HandleErrorAsync(Exception exception, string context = "")
        {
            ErrorMessage = EnableErrorHandling ? exception.Message : null;
            
            // Log the error if logger is available
            Logger?.LogError(exception, "Component error in {ComponentType}: {Context}", 
                GetType().Name, context);
            
            // Call the derived component's error handler
            await OnComponentErrorAsync(exception, context);
            
            // Trigger the error event callback
            if (OnError.HasDelegate)
            {
                var errorArgs = new ComponentErrorEventArgs
                {
                    Exception = exception,
                    Context = context,
                    ComponentType = GetType().Name,
                    ComponentId = ComponentId
                };
                await OnError.InvokeAsync(errorArgs);
            }
            
            StateHasChanged();
        }
        
        /// <summary>
        /// Clears the current error message.
        /// </summary>
        protected void ClearError()
        {
            ErrorMessage = null;
            StateHasChanged();
        }
        
        /// <summary>
        /// Gets the CSS classes to apply to the component's root element.
        /// </summary>
        protected string GetRootCssClasses()
        {
            var classes = new List<string> { "wildwood-component", $"wildwood-{GetType().Name.ToLowerInvariant()}" };
            
            if (CurrentTheme != null)
            {
                classes.Add(CurrentTheme.GetThemeClass());
            }
            
            if (!string.IsNullOrEmpty(CssClass))
            {
                classes.Add(CssClass);
            }
            
            if (IsLoading && ShowLoadingStates)
            {
                classes.Add("wildwood-loading");
            }
            
            if (!string.IsNullOrEmpty(ErrorMessage))
            {
                classes.Add("wildwood-error");
            }
            
            return string.Join(" ", classes);
        }
        
        /// <summary>
        /// Safely executes an async operation with error handling and loading state management.
        /// </summary>
        protected async Task<T?> ExecuteAsync<T>(Func<Task<T>> operation, string? operationName = null)
        {
            try
            {
                await SetLoadingAsync(true);
                ClearError();
                
                return await operation();
            }
            catch (Exception ex)
            {
                await HandleErrorAsync(ex, operationName ?? "async operation");
                return default;
            }
            finally
            {
                await SetLoadingAsync(false);
            }
        }
        
        /// <summary>
        /// Safely executes an async operation with error handling and loading state management.
        /// </summary>
        protected async Task ExecuteAsync(Func<Task> operation, string? operationName = null)
        {
            try
            {
                await SetLoadingAsync(true);
                ClearError();
                
                await operation();
            }
            catch (Exception ex)
            {
                await HandleErrorAsync(ex, operationName ?? "async operation");
            }
            finally
            {
                await SetLoadingAsync(false);
            }
        }
        
        /// <summary>
        /// Invokes a JavaScript function safely with error handling.
        /// </summary>
        protected async Task<T?> InvokeJSAsync<T>(string identifier, params object?[]? args)
        {
            try
            {
                return await JSRuntime.InvokeAsync<T>(identifier, args);
            }
            catch (Exception ex)
            {
                await HandleErrorAsync(ex, $"JavaScript interop: {identifier}");
                return default;
            }
        }
        
        /// <summary>
        /// Invokes a JavaScript function safely with error handling.
        /// </summary>
        protected async Task InvokeJSVoidAsync(string identifier, params object?[]? args)
        {
            try
            {
                await JSRuntime.InvokeVoidAsync(identifier, args);
            }
            catch (Exception ex)
            {
                await HandleErrorAsync(ex, $"JavaScript interop: {identifier}");
            }
        }
        
        #endregion

        #region Event Handlers
        
        private async void OnThemeChanged(object? sender, ComponentTheme theme)
        {
            CurrentTheme = theme;
            await InvokeAsync(StateHasChanged);
        }
        
        #endregion

        #region IDisposable Implementation
        
        private bool _disposed = false;
        
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Unsubscribe from theme changes
                    if (ThemeService != null)
                    {
                        ThemeService.ThemeChanged -= OnThemeChanged;
                    }
                }
                _disposed = true;
            }
        }
        
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        
        #endregion
    }
    
    /// <summary>
    /// Event arguments for component errors.
    /// </summary>
    public class ComponentErrorEventArgs : EventArgs
    {
        public Exception Exception { get; set; } = default!;
        public string Context { get; set; } = string.Empty;
        public string ComponentType { get; set; } = string.Empty;
        public string ComponentId { get; set; } = string.Empty;
    }
}
