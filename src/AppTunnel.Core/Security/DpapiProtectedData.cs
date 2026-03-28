using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace AppTunnel.Core.Security;

[SupportedOSPlatform("windows")]
public sealed class DpapiProtectedData : IDpapiProtector
{
    public byte[] Protect(byte[] clearText)
    {
        ArgumentNullException.ThrowIfNull(clearText);
        return ProtectedData.Protect(clearText, optionalEntropy: null, DataProtectionScope.CurrentUser);
    }

    public byte[] Unprotect(byte[] cipherText)
    {
        ArgumentNullException.ThrowIfNull(cipherText);
        return ProtectedData.Unprotect(cipherText, optionalEntropy: null, DataProtectionScope.CurrentUser);
    }
}
