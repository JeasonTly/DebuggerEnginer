using System.Security.Cryptography;
using System.Text;

namespace TestEngineer.Desktop.Services;

internal static class SecretBox
{
    public static string Protect(string plain)
    {
        if (string.IsNullOrEmpty(plain))
        {
            return "";
        }

        var protectedBytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), optionalEntropy: null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(protectedBytes);
    }

    public static string Unprotect(string stored)
    {
        if (string.IsNullOrEmpty(stored))
        {
            return "";
        }

        var plain = ProtectedData.Unprotect(Convert.FromBase64String(stored), optionalEntropy: null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(plain);
    }
}
