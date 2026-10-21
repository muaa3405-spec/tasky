using System.Security.Cryptography;
using System.Text;

namespace Tasky.Helpers;

/// <summary>
/// Encrypts small secrets (such as sign-in tokens) with Windows DPAPI, tied to
/// the current Windows user. The encrypted bytes are useless on another PC or
/// to another user account, and no key is stored in the app or the repository.
/// </summary>
public static class UserDataProtector
{
    /// <summary>App-specific salt so other programs using DPAPI can't read Tasky's data by accident.</summary>
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Tasky.UserData.v1");

    public static byte[] Protect(byte[] plain)
        => ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);

    /// <summary>Returns null when the data is not Tasky-encrypted data for this user.</summary>
    public static byte[]? TryUnprotect(byte[] protectedBytes)
    {
        try { return ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser); }
        catch (CryptographicException) { return null; }
    }
}
