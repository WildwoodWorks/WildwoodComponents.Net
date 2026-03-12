using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using WildwoodComponents.Blazor.Components.Base;
using WildwoodComponents.Blazor.Models;

namespace WildwoodComponents.Blazor.Components.Registration
{
    public partial class SignupWithSubscriptionComponent : BaseWildwoodComponent
    {
        #region Parameters

        [Parameter, EditorRequired]
        public string AppId { get; set; } = string.Empty;

        [Parameter] public string? PreSelectedTierId { get; set; }
        [Parameter] public string? RegistrationToken { get; set; }
        [Parameter] public bool RequireToken { get; set; }
        [Parameter] public bool AllowOpenRegistration { get; set; } = true;

        /// <summary>
        /// If true, skips the tier selection step and goes directly to success after registration.
        /// </summary>
        [Parameter] public bool SkipTierSelection { get; set; }

        [Parameter] public EventCallback OnComplete { get; set; }
        [Parameter] public EventCallback OnCancel { get; set; }

        #endregion

        #region State

        private SignupStep _currentStep = SignupStep.Register;
        private string? _selectedTierId;

        private enum SignupStep
        {
            Register,
            SelectTier,
            Success
        }

        private class StepDef
        {
            public SignupStep Key { get; set; }
            public string Label { get; set; } = string.Empty;
            public int Number { get; set; }
        }

        private readonly List<StepDef> _stepConfig = new()
        {
            new StepDef { Key = SignupStep.Register, Label = "Create Account", Number = 1 },
            new StepDef { Key = SignupStep.SelectTier, Label = "Choose Plan", Number = 2 },
            new StepDef { Key = SignupStep.Success, Label = "Complete", Number = 3 }
        };

        #endregion

        #region Lifecycle

        protected override Task OnComponentInitializedAsync()
        {
            _selectedTierId = PreSelectedTierId;
            return Task.CompletedTask;
        }

        #endregion

        #region Event Handlers

        private void HandleAutoLoginSuccess(AuthenticationResponse response)
        {
            if (SkipTierSelection)
            {
                _currentStep = SignupStep.Success;
            }
            else
            {
                _currentStep = SignupStep.SelectTier;
            }
            StateHasChanged();
        }

        private void HandleSubscriptionChanged(AppTierSubscriptionChangedEventArgs args)
        {
            _selectedTierId = args.TierId;
            _currentStep = SignupStep.Success;
            StateHasChanged();
        }

        private void HandleTierError(ComponentErrorEventArgs args)
        {
            _ = HandleErrorAsync(args.Exception, args.Context);
        }

        private void HandleBack()
        {
            if (_currentStep == SignupStep.SelectTier)
            {
                _currentStep = SignupStep.Register;
                StateHasChanged();
            }
        }

        private async Task HandleCancel()
        {
            if (OnCancel.HasDelegate)
            {
                await OnCancel.InvokeAsync();
            }
        }

        private async Task HandleComplete()
        {
            if (OnComplete.HasDelegate)
            {
                await OnComplete.InvokeAsync();
            }
        }

        #endregion

        #region Helpers

        private int GetStepIndex(SignupStep step)
        {
            for (int i = 0; i < _stepConfig.Count; i++)
            {
                if (_stepConfig[i].Key == step) return i;
            }
            return 0;
        }

        private bool IsStepCompleted(SignupStep step)
        {
            return GetStepIndex(_currentStep) > GetStepIndex(step);
        }

        #endregion
    }
}
