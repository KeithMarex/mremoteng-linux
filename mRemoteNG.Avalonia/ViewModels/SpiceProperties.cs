using System.ComponentModel;
using System.Globalization;
using mRemoteNG.Avalonia.Sessions;
using mRemoteNG.Connection;
using mRemoteNG.Connection.Protocol;

namespace mRemoteNG.Avalonia.ViewModels;

/// <summary>
/// The SPICE settings of a connection in the property panel: where its console comes from and, for Proxmox VE, which
/// virtual machine. Kept in the connection's plugin properties (and the sidecar file), like the file transfer settings.
/// </summary>
public static class SpiceProperties
{
    // Sorts right after the Connection category (see LocalizedCategoryAttribute: fewer tabs sort later).
    private static readonly string CategoryName = new string('\t', 8) + UiText.SpiceCategory;

    private static readonly PropertyDescriptor Source = new PluginPropertyDescriptor(CategoryName,
        SpiceSettings.SourceKey, typeof(SpiceSource), UiText.SpiceSourceName, UiText.SpiceSourceDescription, SpiceSource.Proxmox,
        new SpiceSourceConverter());

    private static readonly PropertyDescriptor Node = new PluginPropertyDescriptor(CategoryName,
        SpiceSettings.NodeKey, typeof(string), UiText.ProxmoxNodeName, UiText.ProxmoxNodeDescription, string.Empty);

    private static readonly PropertyDescriptor Vm = new PluginPropertyDescriptor(CategoryName,
        SpiceSettings.VmKey, typeof(string), UiText.ProxmoxVmName, UiText.ProxmoxVmDescription, string.Empty);

    /// <summary>The settings shown for a connection (none for folders: plugin properties are not inherited).</summary>
    public static IEnumerable<PropertyDescriptor> For(ConnectionInfo connection) =>
        connection.IsContainer || connection.Protocol != ProtocolType.SPICE ? []
        : SpiceSettings.Source(connection) == SpiceSource.Proxmox ? [Source, Vm, Node]
        : [Source];

    private sealed class SpiceSourceConverter() : EnumConverter(typeof(SpiceSource))
    {
        public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType) =>
            destinationType == typeof(string) && value is SpiceSource source
                ? source == SpiceSource.Proxmox ? UiText.SpiceSourceProxmox : UiText.SpiceSourceDirect
                : base.ConvertTo(context, culture, value, destinationType);
    }
}
