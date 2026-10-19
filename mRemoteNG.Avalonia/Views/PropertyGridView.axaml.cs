using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using mRemoteNG.Avalonia.ViewModels;

namespace mRemoteNG.Avalonia.Views;

/// <summary>The property grid; the row the user clicks or tabs into is selected, and its help is shown.</summary>
public partial class PropertyGridView : UserControl
{
    public PropertyGridView()
    {
        InitializeComponent();
        AddHandler(GotFocusEvent, (_, e) => Select(e.Source), RoutingStrategies.Bubble);
        AddHandler(PointerPressedEvent, (_, e) => Select(e.Source), RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private void Select(object? source)
    {
        if ((source as StyledElement)?.DataContext is PropertyItemViewModel item && DataContext is PropertyPanelViewModel panel)
            panel.SelectedItem = item;
    }
}
