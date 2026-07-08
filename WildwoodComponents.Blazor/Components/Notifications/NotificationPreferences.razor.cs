using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using WildwoodComponents.Blazor.Components.Base;
using WildwoodComponents.Blazor.Services;
using WildwoodComponents.Shared.Models;
using WildwoodComponents.Shared.Utilities;

namespace WildwoodComponents.Blazor.Components.Notifications;

/// <summary>
/// Per-app delivery-channel toggles (Email / SMS / Push / Browser) bound to
/// <see cref="UserNotificationPreference"/>. Saves optimistically and reverts on a
/// transient failure. The Browser toggle first requests Web Notifications permission
/// and only enables when granted. Mirrors the @wildwood/react NotificationPreferences.
/// </summary>
public partial class NotificationPreferences : BaseWildwoodComponent
{
    [Inject] private INotificationInboxService InboxService { get; set; } = default!;

    /// <summary>App whose preferences are edited.</summary>
    [Parameter, EditorRequired] public string AppId { get; set; } = string.Empty;

    /// <summary>Whether the Push channel row is shown.</summary>
    [Parameter] public bool ShowPush { get; set; } = true;

    /// <summary>Whether the Browser channel row is shown.</summary>
    [Parameter] public bool ShowBrowser { get; set; } = true;

    private UserNotificationPreference? _pref;
    private bool _saving;

    protected override async Task OnComponentInitializedAsync()
    {
        var token = LocalStorage != null
            ? await LocalStorage.GetItemAsync<string>(WildwoodStorageKeys.AccessToken)
            : null;
        InboxService.SetAuthToken(token);
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        var pref = await InboxService.GetPreferencesAsync(AppId);
        if (pref != null)
        {
            _pref = pref;
        }
        else if (_pref == null)
        {
            // Transient failure with nothing loaded yet — fall back to safe defaults.
            _pref = UserNotificationPreference.CreateDefault(AppId);
        }
        // Transient failure with an existing _pref: retain the prior values.
    }

    private Task ToggleEmail(bool value) => SetChannelAsync(p => p.EmailEnabled = value);

    private Task ToggleSms(bool value) => SetChannelAsync(p => p.SmsEnabled = value);

    private Task TogglePush(bool value) => SetChannelAsync(p => p.PushEnabled = value);

    private async Task ToggleBrowser(bool value)
    {
        if (_pref == null) return;

        if (value)
        {
            var supported = await InvokeJSAsync<bool>("wildwoodBrowserNotifications.isSupported");
            if (supported != true) return;

            var permission = await InvokeJSAsync<string>("wildwoodBrowserNotifications.requestPermission");
            if (!string.Equals(permission, "granted", StringComparison.OrdinalIgnoreCase))
            {
                // Permission denied/dismissed — leave the toggle off.
                StateHasChanged();
                return;
            }
        }

        await SetChannelAsync(p => p.BrowserEnabled = value);
    }

    private async Task SetChannelAsync(Action<UserNotificationPreference> apply)
    {
        if (_pref == null) return;

        var snapshot = Clone(_pref);
        apply(_pref);

        _saving = true;
        StateHasChanged();

        var saved = await InboxService.UpdatePreferencesAsync(_pref);

        _saving = false;
        // On success adopt the server record; on transient failure (null) revert to the snapshot.
        _pref = saved ?? snapshot;
        StateHasChanged();
    }

    private static UserNotificationPreference Clone(UserNotificationPreference source) => new()
    {
        AppId = source.AppId,
        EmailEnabled = source.EmailEnabled,
        SmsEnabled = source.SmsEnabled,
        PushEnabled = source.PushEnabled,
        BrowserEnabled = source.BrowserEnabled,
        EventOptOutsJson = source.EventOptOutsJson,
    };
}
