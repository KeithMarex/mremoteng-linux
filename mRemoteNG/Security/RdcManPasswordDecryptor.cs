using System;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace mRemoteNG.Security
{
    /// <summary>Decrypts passwords stored by Remote Desktop Connection Manager (DPAPI, machine scope).</summary>
    [SupportedOSPlatform("windows")]
    public static class RdcManPasswordDecryptor
    {
        public static string Decrypt(string ciphertext)
        {
            byte[] plaintextData = ProtectedData.Unprotect(Convert.FromBase64String(ciphertext), new byte[] { },
                                                           DataProtectionScope.LocalMachine);
            return new string(Encoding.Unicode.GetChars(plaintextData));
        }
    }
}
