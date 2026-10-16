using System;
using System.ComponentModel;
using System.Drawing.Design;
using mRemoteNG.Config.Putty;
using mRemoteNG.Connection;
using mRemoteNG.Tools;
using mRemoteNG.UI.Controls.ConnectionInfoPropertyGrid;
using NUnit.Framework;

namespace mRemoteNGTests.Connection;

/// <summary>
/// The connection model (in mRemoteNG.Core) names its WinForms editors and converters as strings;
/// these tests fail if one of those names no longer resolves to the intended type.
/// </summary>
public class WinFormsPropertyGridTypesTests
{
    [TestCase(WinFormsPropertyGridTypes.UITypeEditor, typeof(UITypeEditor))]
    [TestCase(WinFormsPropertyGridTypes.ColorStringEditor, typeof(ColorStringEditor))]
    [TestCase(WinFormsPropertyGridTypes.ConnectionIconConverter, typeof(ConnectionIcon))]
    [TestCase(WinFormsPropertyGridTypes.ExternalToolsConverter, typeof(ExternalToolsTypeConverter))]
    [TestCase(WinFormsPropertyGridTypes.SshTunnelConverter, typeof(SshTunnelTypeConverter))]
    [TestCase(WinFormsPropertyGridTypes.PuttySessionListConverter, typeof(PuttySessionsManager.SessionList))]
    [TestCase(WinFormsPropertyGridTypes.ConnectionInfoTypeDescriptionProvider, typeof(ConnectionInfoTypeDescriptionProvider))]
    public void TypeNameResolvesToWinFormsType(string typeName, Type expected)
    {
        Assert.That(Type.GetType(typeName), Is.EqualTo(expected));
    }

    [TestCase(nameof(ConnectionInfo.Color))]
    [TestCase(nameof(ConnectionInfo.TabColor))]
    public void ColorPropertiesUseColorStringEditor(string propertyName)
    {
        PropertyDescriptor property = TypeDescriptor.GetProperties(new ConnectionInfo())[propertyName];

        Assert.That(property.GetEditor(typeof(UITypeEditor)), Is.InstanceOf<ColorStringEditor>());
    }

    [TestCase(nameof(ConnectionInfo.ExtApp), typeof(ExternalToolsTypeConverter))]
    [TestCase(nameof(ConnectionInfo.SSHTunnelConnectionName), typeof(SshTunnelTypeConverter))]
    [TestCase(nameof(ConnectionInfo.PuttySession), typeof(PuttySessionsManager.SessionList))]
    [TestCase(nameof(ConnectionInfo.Icon), typeof(ConnectionIcon))]
    public void PropertiesUseWinFormsConverters(string propertyName, Type expectedConverter)
    {
        PropertyDescriptor property = TypeDescriptor.GetProperties(new ConnectionInfo())[propertyName];

        Assert.That(property.Converter, Is.InstanceOf(expectedConverter));
    }
}
