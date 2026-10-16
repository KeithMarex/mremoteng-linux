namespace mRemoteNG.Avalonia.ViewModels;

/// <summary>The View menu: which panels show. Both are hidden until the user shows them, and the choice is remembered.</summary>
public sealed partial class MainWindowViewModel
{
    public bool IsNotificationsVisible
    {
        get => _settings.ShowNotifications;
        set
        {
            if (value == _settings.ShowNotifications)
                return;
            _settings.ShowNotifications = value;
            OnPropertyChanged();
            SaveSettings();
        }
    }

    /// <summary>The selected property's description under the Config panel.</summary>
    public bool IsPropertyDescriptionVisible
    {
        get => _settings.ShowPropertyDescription;
        set
        {
            if (value == _settings.ShowPropertyDescription)
                return;
            _settings.ShowPropertyDescription = value;
            PropertyPanel.IsDescriptionVisible = value;
            OnPropertyChanged();
            SaveSettings();
        }
    }
}
