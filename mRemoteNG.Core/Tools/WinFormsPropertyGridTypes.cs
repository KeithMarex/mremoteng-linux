namespace mRemoteNG.Tools
{
    /// <summary>
    /// Assembly-qualified names of the WinForms property-grid helpers that the connection model is
    /// annotated with. Referencing them by name keeps Core free of WinForms: the Windows app resolves
    /// them at runtime as before, and where they are absent (Linux) TypeDescriptor falls back to its
    /// defaults.
    /// </summary>
    public static class WinFormsPropertyGridTypes
    {
        private const string App = ", mRemoteNG";

        public const string UITypeEditor = "System.Drawing.Design.UITypeEditor, System.Windows.Forms";
        public const string ColorStringEditor = "mRemoteNG.UI.Controls.ConnectionInfoPropertyGrid.ColorStringEditor" + App;
        public const string ConnectionIconConverter = "mRemoteNG.Connection.ConnectionIcon" + App;
        public const string ExternalToolsConverter = "mRemoteNG.Tools.ExternalToolsTypeConverter" + App;
        public const string SshTunnelConverter = "mRemoteNG.Tools.SshTunnelTypeConverter" + App;
        public const string PuttySessionListConverter = "mRemoteNG.Config.Putty.PuttySessionsManager+SessionList" + App;
        public const string ConnectionInfoTypeDescriptionProvider = "mRemoteNG.Connection.ConnectionInfoTypeDescriptionProvider" + App;
    }
}
