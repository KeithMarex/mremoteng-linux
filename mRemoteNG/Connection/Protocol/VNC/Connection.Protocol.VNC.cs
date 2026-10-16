using System;
using System.Threading;
using System.ComponentModel;
using System.Net.Sockets;
using mRemoteNG.App;
using mRemoteNG.Tools;
using mRemoteNG.UI.Forms;
using mRemoteNG.Resources.Language;
using System.Runtime.Versioning;
using mRemoteNG.Security;
using System.Runtime.ExceptionServices;

// ReSharper disable ArrangeAccessorOwnerBody


namespace mRemoteNG.Connection.Protocol.VNC
{
    [SupportedOSPlatform("windows")]
    public class ProtocolVNC : ProtocolBase
    {
        #region Private Declarations

        private VncSharpCore.RemoteDesktop _vnc;
        private ConnectionInfo _info;
        private static volatile bool _isConnectionSuccessful;
        private static ExceptionDispatchInfo _socketexception;
        private static readonly ManualResetEvent TimeoutObject = new(false);
        private static readonly object _testConnectLock = new();

        #endregion

        #region Public Methods

        public ProtocolVNC()
        {
            Control = new VncSharpCore.RemoteDesktop();
        }

        public override bool Initialize()
        {
            base.Initialize();

            try
            {
                _vnc = Control as VncSharpCore.RemoteDesktop;
                _info = InterfaceControl.Info;
                _vnc.VncPort = _info.Port;

                return true;
            }
            catch (Exception ex)
            {
                Runtime.MessageCollector.AddMessage(Messages.MessageClass.ErrorMsg,
                                                    Language.VncSetPropsFailed + Environment.NewLine + ex.Message,
                                                    true);
                return false;
            }
        }
 
        public override bool Connect()
        {
            SetEventHandlers();
            try
            {
                if (TestConnect(_info.Hostname, _info.Port, 500))
                    _vnc.Connect(_info.Hostname, _info.VNCViewOnly, _info.VNCSmartSizeMode != SmartSizeMode.SmartSNo);
            }
            catch (Exception ex)
            {
                Runtime.MessageCollector.AddMessage(Messages.MessageClass.ErrorMsg,
                                                    Language.ConnectionOpenFailed + Environment.NewLine +
                                                    ex.Message);
                return false;
            }

            return true;
        }

        public override void Disconnect()
        {
            try
            {
                _vnc.Disconnect();
            }
            catch (Exception ex)
            {
                Runtime.MessageCollector.AddMessage(Messages.MessageClass.ErrorMsg,
                                                    Language.VncConnectionDisconnectFailed + Environment.NewLine +
                                                    ex.Message, true);
            }
        }

        public void SendSpecialKeys(SpecialKeys Keys)
        {
            try
            {
                // ReSharper disable once SwitchStatementMissingSomeCases
                switch (Keys)
                {
                    case SpecialKeys.CtrlAltDel:
                        _vnc.SendSpecialKeys(VncSharpCore.SpecialKeys.CtrlAltDel);
                        break;
                    case SpecialKeys.CtrlEsc:
                        _vnc.SendSpecialKeys(VncSharpCore.SpecialKeys.CtrlEsc);
                        break;
                }
            }
            catch (Exception ex)
            {
                Runtime.MessageCollector.AddMessage(Messages.MessageClass.ErrorMsg,
                                                    Language.VncSendSpecialKeysFailed + Environment.NewLine +
                                                    ex.Message, true);
            }
        }

        public void StartChat()
        {
            throw new NotImplementedException();
        }

        public void StartFileTransfer()
        {
            throw new NotImplementedException();
        }

        public void RefreshScreen()
        {
            try
            {
                _vnc.FullScreenUpdate();
            }
            catch (Exception ex)
            {
                Runtime.MessageCollector.AddMessage(Messages.MessageClass.ErrorMsg,
                                                    Language.VncRefreshFailed + Environment.NewLine + ex.Message,
                                                    true);
            }
        }

        #endregion

        #region Private Methods

        private void SetEventHandlers()
        {
            try
            {
                _vnc.ConnectComplete += VNCEvent_Connected;
                _vnc.ConnectionLost += VNCEvent_Disconnected;
                FrmMain.ClipboardChanged += VNCEvent_ClipboardChanged;
                if (!Force.HasFlag(ConnectionInfo.Force.NoCredentials) && _info?.Password?.Length > 0)
                {
                    _vnc.GetPassword = VNCEvent_Authenticate;
                }
            }
            catch (Exception ex)
            {
                Runtime.MessageCollector.AddMessage(Messages.MessageClass.ErrorMsg,
                                                    Language.VncSetEventHandlersFailed + Environment.NewLine +
                                                    ex.Message, true);
            }
        }

        private static bool TestConnect(string hostName, int port, int timeoutMSec)
        {
            lock (_testConnectLock)
            {
                _socketexception = null;
                TcpClient tcpclient = new();

                TimeoutObject.Reset();
                tcpclient.BeginConnect(hostName, port, CallBackMethod, tcpclient);

                if (TimeoutObject.WaitOne(timeoutMSec, false))
                {
                    if (_isConnectionSuccessful) return true;
                    // Connection completed but failed - tcpclient will be closed in CallBackMethod's finally block
                    if (_socketexception != null)
                    {
                        _socketexception.Throw();
                    }
                }
                else
                {
                    tcpclient.Close();
                    throw new TimeoutException($"Connection timed out to host " + hostName + " on port " + port);
                }

                return false;
            }
        }

        private static void CallBackMethod(IAsyncResult asyncresult)
        {
            TcpClient tcpclient = null;
            try
            {
                _isConnectionSuccessful = false;
                tcpclient = asyncresult.AsyncState as TcpClient;

                if (tcpclient?.Client == null) return;

                tcpclient.EndConnect(asyncresult);
                _isConnectionSuccessful = true;
            }
            catch (Exception ex)
            {
                _isConnectionSuccessful = false;
                _socketexception = ExceptionDispatchInfo.Capture(ex);
            }
            finally
            {
                tcpclient?.Close();
                TimeoutObject.Set();
            }
        }

        #endregion

        #region Private Events & Handlers

        private void VNCEvent_Connected(object sender, EventArgs e)
        {
            Event_Connected(this);
            _vnc.AutoScroll = _info.VNCSmartSizeMode == SmartSizeMode.SmartSNo;
        }

        private void VNCEvent_Disconnected(object sender, EventArgs e)
        {
            FrmMain.ClipboardChanged -= VNCEvent_ClipboardChanged;
            Event_Disconnected(this, @"VncSharp Disconnected.", null);
            Close();
        }

        private void VNCEvent_ClipboardChanged()
        {
            _vnc.FillServerClipboard();
        }

        private string VNCEvent_Authenticate()
        {
            //return _info.Password.ConvertToUnsecureString();
            return _info.Password;
        }

        #endregion
    }
}
