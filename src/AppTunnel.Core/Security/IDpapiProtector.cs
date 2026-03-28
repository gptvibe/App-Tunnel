namespace AppTunnel.Core.Security;

public interface IDpapiProtector
{
    byte[] Protect(byte[] clearText);

    byte[] Unprotect(byte[] cipherText);
}
