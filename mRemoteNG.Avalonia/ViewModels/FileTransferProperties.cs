using System.ComponentModel;
using System.Globalization;
using mRemoteNG.Avalonia.Sessions;
using mRemoteNG.Connection;
using mRemoteNG.Connection.Protocol;
using mRemoteNG.FileTransfer.Ftp;

namespace mRemoteNG.Avalonia.ViewModels;

/// <summary>
/// The file transfer settings of FTP and SFTP connections in the property panel. They are kept in the connection's
/// plugin properties (and the sidecar file), so Windows mRemoteNG leaves them alone.
/// </summary>
public static class FileTransferProperties
{
    // Sorts right after the Connection category (see LocalizedCategoryAttribute: fewer tabs sort later).
    private static readonly string CategoryName = new string('\t', 8) + UiText.FileTransferCategory;

    private static readonly PropertyDescriptor Encryption = new PluginPropertyDescriptor(CategoryName,
        FileTransferSettings.EncryptionKey, typeof(FtpEncryption), UiText.FtpEncryptionName, UiText.FtpEncryptionDescription,
        FtpEncryption.Explicit, new FtpEncryptionConverter());

    private static readonly PropertyDescriptor Passive = new PluginPropertyDescriptor(CategoryName,
        FileTransferSettings.PassiveKey, typeof(bool), UiText.FtpPassiveName, UiText.FtpPassiveDescription, true);

    private static readonly PropertyDescriptor StartDirectory = new PluginPropertyDescriptor(CategoryName,
        FileTransferSettings.StartDirectoryKey, typeof(string), UiText.StartDirectoryName, UiText.StartDirectoryDescription, string.Empty);

    /// <summary>The settings shown for a connection (none for folders: plugin properties are not inherited).</summary>
    public static IEnumerable<PropertyDescriptor> For(ConnectionInfo connection) => connection.IsContainer
        ? []
        : connection.Protocol switch
        {
            ProtocolType.FTP => [Encryption, Passive, StartDirectory],
            ProtocolType.SFTP => [StartDirectory],
            _ => []
        };

    private sealed class FtpEncryptionConverter() : EnumConverter(typeof(FtpEncryption))
    {
        public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType) =>
            destinationType == typeof(string) && value is FtpEncryption encryption
                ? encryption switch
                {
                    FtpEncryption.Explicit => UiText.FtpExplicitTls,
                    FtpEncryption.Implicit => UiText.FtpImplicitTls,
                    _ => UiText.FtpNoEncryption
                }
                : base.ConvertTo(context, culture, value, destinationType);
    }
}

/// <summary>
/// A setting of mRemoteNG for Linux in a connection's plugin properties, shown in the property panel; the default value
/// is stored as no value at all.
/// </summary>
internal sealed class PluginPropertyDescriptor(string category, string key, Type type, string displayName, string description,
                                               object defaultValue, TypeConverter? converter = null)
    : PropertyDescriptor(key, [new CategoryAttribute(category), new DisplayNameAttribute(displayName), new DescriptionAttribute(description)])
{
    public override Type ComponentType => typeof(ConnectionInfo);
    public override bool IsReadOnly => false;
    public override Type PropertyType => type;
    public override TypeConverter Converter => converter ?? base.Converter;

    public override object GetValue(object? component)
    {
        string text = ((ConnectionInfo)component!).GetPluginProperty(Name);
        if (text.Length == 0)
            return defaultValue;
        if (type == typeof(bool))
            return bool.TryParse(text, out bool value) ? value : defaultValue;
        if (type.IsEnum)
            return Enum.TryParse(type, text, out object? value) && Enum.IsDefined(type, value!) ? value! : defaultValue;
        return text;
    }

    public override void SetValue(object? component, object? value)
    {
        string? text = Equals(value, defaultValue) ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
        ((ConnectionInfo)component!).SetPluginProperty(Name, string.IsNullOrWhiteSpace(text) ? null : text.Trim());
    }

    public override bool CanResetValue(object component) => true;

    public override void ResetValue(object component) => ((ConnectionInfo)component).SetPluginProperty(Name, null);

    public override bool ShouldSerializeValue(object component) => false;
}
