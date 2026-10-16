using System;
using System.ComponentModel;
using System.Data.SqlTypes;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Security;
using mRemoteNG.App;
using mRemoteNG.Messages;
using mRemoteNG.UI.Forms;
using MySql.Data.Types;
using mRemoteNG.Resources.Language;
using System.Runtime.Versioning;

namespace mRemoteNG.Tools
{
    [SupportedOSPlatform("windows")]
    public static class MiscTools
    {
        public static Icon? GetIconFromFile(string FileName)
        {
            try
            {
                return File.Exists(FileName) ? Icon.ExtractAssociatedIcon(FileName) : null;
            }
            catch (ArgumentException AEx)
            {
                Runtime.MessageCollector.AddMessage(MessageClass.WarningMsg, "GetIconFromFile failed (Tools.Misc) - using default icon" + Environment.NewLine + AEx.Message, true);
                return Properties.Resources.mRemoteNG_Icon;
            }
            catch (Exception ex)
            {
                Runtime.MessageCollector.AddMessage(MessageClass.WarningMsg, "GetIconFromFile failed (Tools.Misc)" + Environment.NewLine + ex.Message, true);
                return null;
            }
        }

        public static Optional<SecureString> PasswordDialog(string? passwordName = null, bool verify = true)
        {
            //var splash = FrmSplashScreenNew.GetInstance();
            //TODO: something not right there 
            //if (PresentationSource.FromVisual(splash))
            //    splash.Close();

            passwordName ??= string.Empty; // Ensure passwordName is not null
            FrmPassword passwordForm = new(passwordName, verify);
            return FrmMain.IsCreated && FrmMain.Default.Visible
                ? passwordForm.GetKey(FrmMain.Default)
                : passwordForm.GetKey();
        }

        public static string LeadingZero(string Number)
        {
            if (Convert.ToInt32(Number) < 10)
            {
                return "0" + Number;
            }

            return Number;
        }

        public static bool GetBooleanValue(object dataObject)
        {
            Type type = dataObject.GetType();

            if (type == typeof(bool))
            {
                return (bool)dataObject;
            }
            if (type == typeof(string))
            {
                return (string)dataObject == "1";
            }
            if (type == typeof(sbyte))
            {
                return (sbyte)dataObject == 1;
            }

            Runtime.MessageCollector.AddMessage(MessageClass.ErrorMsg, $"Conversion of object to boolean failed because the type, {type}, is not handled.");
            return false;
        }

        public static string DBDate(DateTime Dt)
		{
			switch (Properties.OptionsDBsPage.Default.SQLServerType)
			{
				case "mysql":
					return Dt.ToString("yyyy/MM/dd HH:mm:ss");
				case "mssql":
				default:
					return Dt.ToString("yyyyMMdd HH:mm:ss");
			}
		}

		public static Type DBTimeStampType()
		{
			switch (Properties.OptionsDBsPage.Default.SQLServerType)
			{
				case "mysql":
					return typeof(MySqlDateTime);
				case "mssql":
				default:
					return typeof(SqlDateTime);
			}
		}

		public static object DBTimeStampNow()
		{
			switch (Properties.OptionsDBsPage.Default.SQLServerType)
			{
				case "mysql":
					return new MySqlDateTime(DateTime.Now.ToUniversalTime());
				case "mssql":
				default:
					return DateTime.Now.ToUniversalTime();
			}
		}

        public static string PrepareValueForDB(string Text)
        {
            return Text.Replace("\'", "\'\'");
        }

        public static string GetExceptionMessageRecursive(Exception ex)
        {
            return ex.GetExceptionMessageRecursive();
        }


        public static Image? TakeScreenshot(UI.Tabs.ConnectionTab sender)
        {
            try
            {
                if (sender != null)
                {
                    Bitmap bmp = new(sender.Width, sender.Height, PixelFormat.Format32bppRgb);
                    Graphics g = Graphics.FromImage(bmp);
                    g.CopyFromScreen(sender.PointToScreen(System.Drawing.Point.Empty), System.Drawing.Point.Empty, bmp.Size, CopyPixelOperation.SourceCopy);
                    return bmp;
                }
            }
            catch (Exception ex)
            {
                Runtime.MessageCollector.AddExceptionStackTrace("Taking Screenshot failed", ex);
            }

            return null;
        }

    }
}