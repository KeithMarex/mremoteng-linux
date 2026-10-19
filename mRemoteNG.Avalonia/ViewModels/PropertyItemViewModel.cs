using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using mRemoteNG.Connection;
using mRemoteNG.Resources.Language;

namespace mRemoteNG.Avalonia.ViewModels;

public enum PropertyEditorKind
{
    Text,
    Password,
    Boolean,
    Choice,
    Number
}

/// <summary>A value a property can take, with the (localized) text to show for it.</summary>
public sealed record PropertyChoice(object Value, string DisplayText)
{
    public override string ToString() => DisplayText;
}

/// <summary>
/// One row of the property grid: a property of a connection, or (in the Inheritance view) one of its inheritance
/// settings. As in the Windows property grid, booleans are Yes/No choices and values set on purpose are bold.
/// </summary>
public sealed partial class PropertyItemViewModel : ObservableObject
{
    private readonly object _component;
    private readonly PropertyDescriptor _property;
    private readonly Action<PropertyItemViewModel> _changed;

    /// <param name="component">The connection, or its <see cref="ConnectionInfoInheritance"/> in the Inheritance view.</param>
    /// <param name="iconNames">The connection icons to offer for the Icon property.</param>
    /// <param name="toolNames">The external tools to offer for the external tool properties.</param>
    /// <param name="sshTunnelNames">The SSH connections to offer for the SSH Tunnel, as the Windows app does.</param>
    public PropertyItemViewModel(object component, PropertyDescriptor property, Action<PropertyItemViewModel> changed,
                                 IReadOnlyList<string>? iconNames = null, IReadOnlyList<string>? toolNames = null,
                                 IReadOnlyList<string>? sshTunnelNames = null)
    {
        _component = component;
        _property = property;
        _changed = changed;
        bool isIcon = component is ConnectionInfo && property.Name == nameof(ConnectionInfo.Icon) && iconNames is { Count: > 0 };
        bool isTool = component is ConnectionInfo && ToolProperties.Contains(property.Name) && toolNames is not null;
        bool isTunnel = component is ConnectionInfo && property.Name == nameof(ConnectionInfo.SSHTunnelConnectionName) && sshTunnelNames is not null;
        Kind = isIcon || isTool || isTunnel ? PropertyEditorKind.Choice : KindOf(property);
        Choices = Kind switch
        {
            PropertyEditorKind.Boolean => [new PropertyChoice(true, Language.Yes), new PropertyChoice(false, Language.No)],
            PropertyEditorKind.Choice when isIcon => NameChoices(iconNames!, property.GetValue(component) as string),
            // As on Windows, an external tool property can also be empty: no tool.
            PropertyEditorKind.Choice when isTool =>
                [new PropertyChoice(string.Empty, UiText.NoExternalTool), .. NameChoices(toolNames!, property.GetValue(component) as string)],
            // Empty: no tunnel.
            PropertyEditorKind.Choice when isTunnel =>
                [new PropertyChoice(string.Empty, UiText.NoSshTunnel), .. NameChoices(sshTunnelNames!, property.GetValue(component) as string)],
            PropertyEditorKind.Choice => Enum.GetValues(property.PropertyType).Cast<object>()
                                             .Select(value => new PropertyChoice(value, property.Converter.ConvertToString(value) ?? value.ToString()!))
                                             .ToArray(),
            _ => []
        };
    }

    public string Name => _property.Name;
    public string DisplayName => _property.DisplayName;
    public string Description => _property.Description;

    /// <summary>The category as shown; the raw value carries leading tabs that order the categories.</summary>
    public string Category => _property.Category.Trim();
    public string CategorySortKey => _property.Category;

    /// <summary>Whether this row is an inheritance setting (the Inheritance view).</summary>
    public bool IsInheritanceSetting => _component is ConnectionInfoInheritance;

    public PropertyEditorKind Kind { get; }
    public bool IsText => Kind is PropertyEditorKind.Text or PropertyEditorKind.Number;
    public bool IsPassword => Kind == PropertyEditorKind.Password;
    public bool IsChoice => Kind is PropertyEditorKind.Choice or PropertyEditorKind.Boolean;
    public IReadOnlyList<PropertyChoice> Choices { get; }

    public bool IsEditable => !_property.IsReadOnly;

    /// <summary>Whether the value is set rather than the default; the Windows property grid shows those in bold.</summary>
    public bool IsModified => _property.ShouldSerializeValue(_component);

    /// <summary>The row the user is on; its description is shown under the grid.</summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>The value for text, password and number editors. Unparseable numbers are ignored.</summary>
    public string? Text
    {
        get => Kind == PropertyEditorKind.Number
            ? Convert.ToString(_property.GetValue(_component), CultureInfo.CurrentCulture)
            : _property.GetValue(_component) as string;
        set
        {
            if (value == Text)
                return;
            if (Kind == PropertyEditorKind.Number)
            {
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.CurrentCulture, out int number))
                {
                    OnPropertyChanged();
                    return;
                }
                SetValue(number);
            }
            else
            {
                SetValue(value ?? string.Empty);
            }
        }
    }

    public bool BooleanValue
    {
        get => _property.GetValue(_component) is true;
        set
        {
            if (value != BooleanValue)
                SetValue(value);
        }
    }

    public PropertyChoice? SelectedChoice
    {
        get => Choices.FirstOrDefault(choice => Equals(choice.Value, _property.GetValue(_component)));
        set
        {
            if (value != null && !Equals(value.Value, _property.GetValue(_component)))
                SetValue(value.Value);
        }
    }

    /// <summary>Re-reads the value, e.g. after another property (or the parent's inherited value) changed.</summary>
    public void RefreshValue()
    {
        OnPropertyChanged(nameof(Text));
        OnPropertyChanged(nameof(BooleanValue));
        OnPropertyChanged(nameof(SelectedChoice));
        OnPropertyChanged(nameof(IsModified));
    }

    private void SetValue(object value)
    {
        _property.SetValue(_component, value);
        RefreshValue();
        _changed(this);
    }

    /// <summary>The connection properties that name an external tool.</summary>
    private static readonly string[] ToolProperties =
        [nameof(ConnectionInfo.ExtApp), nameof(ConnectionInfo.PreExtApp), nameof(ConnectionInfo.PostExtApp)];

    // The names there are (icons, tools), plus the connection's own if it names one this app does not have.
    private static PropertyChoice[] NameChoices(IReadOnlyList<string> names, string? current) =>
        names.Concat(string.IsNullOrEmpty(current) || names.Contains(current) ? [] : [current])
             .Select(name => new PropertyChoice(name, name))
             .ToArray();

    private static PropertyEditorKind KindOf(PropertyDescriptor property)
    {
        Type type = property.PropertyType;
        if (type == typeof(bool))
            return PropertyEditorKind.Boolean;
        if (type.IsEnum)
            return PropertyEditorKind.Choice;
        if (type == typeof(int))
            return PropertyEditorKind.Number;
        return property.Attributes[typeof(PasswordPropertyTextAttribute)] is PasswordPropertyTextAttribute { Password: true }
            ? PropertyEditorKind.Password
            : PropertyEditorKind.Text;
    }
}
