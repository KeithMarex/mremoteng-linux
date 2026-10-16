using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using mRemoteNG.Connection;

namespace mRemoteNG.Avalonia.ViewModels;

public enum PropertyEditorKind
{
    Text,
    Password,
    Boolean,
    Choice,
    Number
}

/// <summary>A value an enum property can take, with the (localized) text to show for it.</summary>
public sealed record PropertyChoice(object Value, string DisplayText)
{
    public override string ToString() => DisplayText;
}

/// <summary>One editable property of a connection in the property panel.</summary>
public sealed class PropertyItemViewModel : ObservableObject
{
    private readonly ConnectionInfo _connection;
    private readonly PropertyDescriptor _property;
    private readonly PropertyInfo? _inheritanceFlag;
    private readonly Action<PropertyItemViewModel> _changed;

    public PropertyItemViewModel(ConnectionInfo connection, PropertyDescriptor property, Action<PropertyItemViewModel> changed)
    {
        _connection = connection;
        _property = property;
        _changed = changed;
        _inheritanceFlag = connection.Inheritance.InheritanceActive
            ? connection.Inheritance.GetType().GetProperty(property.Name, typeof(bool))
            : null;
        Kind = KindOf(property);
        Choices = Kind == PropertyEditorKind.Choice
            ? Enum.GetValues(property.PropertyType).Cast<object>()
                  .Select(value => new PropertyChoice(value, property.Converter.ConvertToString(value) ?? value.ToString()!))
                  .ToArray()
            : [];
    }

    public string Name => _property.Name;
    public string DisplayName => _property.DisplayName;
    public string Description => _property.Description;

    /// <summary>The category as shown; the raw value carries leading tabs that order the categories.</summary>
    public string Category => _property.Category.Trim();
    public string CategorySortKey => _property.Category;

    public PropertyEditorKind Kind { get; }
    public bool IsText => Kind is PropertyEditorKind.Text or PropertyEditorKind.Number;
    public bool IsPassword => Kind == PropertyEditorKind.Password;
    public bool IsBoolean => Kind == PropertyEditorKind.Boolean;
    public bool IsChoice => Kind == PropertyEditorKind.Choice;
    public IReadOnlyList<PropertyChoice> Choices { get; }

    public bool CanInherit => _inheritanceFlag != null;

    public bool IsInherited
    {
        get => _inheritanceFlag?.GetValue(_connection.Inheritance) is true;
        set
        {
            if (_inheritanceFlag == null || IsInherited == value)
                return;
            _inheritanceFlag.SetValue(_connection.Inheritance, value);
            RefreshValue();
            _changed(this);
        }
    }

    public bool IsEditable => !IsInherited && !_property.IsReadOnly;

    /// <summary>The value for text, password and number editors. Unparseable numbers are ignored.</summary>
    public string? Text
    {
        get => Kind == PropertyEditorKind.Number
            ? Convert.ToString(_property.GetValue(_connection), CultureInfo.CurrentCulture)
            : _property.GetValue(_connection) as string;
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
        get => _property.GetValue(_connection) is true;
        set
        {
            if (value != BooleanValue)
                SetValue(value);
        }
    }

    public PropertyChoice? SelectedChoice
    {
        get => Choices.FirstOrDefault(choice => Equals(choice.Value, _property.GetValue(_connection)));
        set
        {
            if (value != null && !Equals(value.Value, _property.GetValue(_connection)))
                SetValue(value.Value);
        }
    }

    /// <summary>Re-reads the value, e.g. after another property (or the parent's inherited value) changed.</summary>
    public void RefreshValue()
    {
        OnPropertyChanged(nameof(Text));
        OnPropertyChanged(nameof(BooleanValue));
        OnPropertyChanged(nameof(SelectedChoice));
        OnPropertyChanged(nameof(IsInherited));
        OnPropertyChanged(nameof(IsEditable));
    }

    private void SetValue(object value)
    {
        _property.SetValue(_connection, value);
        RefreshValue();
        _changed(this);
    }

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
