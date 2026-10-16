using System.Security.Cryptography;

namespace Nexus.Application.Common;

/// <summary>
/// Random bearer tokens for e-mailed links (invitations). 256 bits from the OS CSPRNG, base64url so they survive
/// URLs and e-mail clients. Only <see cref="Hash"/> is stored, so a database leak does not expose usable links.
/// </summary>
public static class SecureTokens
{
    public static (string Token, byte[] Hash) Create()
    {
        var token = Base64Url(RandomNumberGenerator.GetBytes(32));
        return (token, Hash(token));
    }

    public static byte[] Hash(string token) => SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token));

    public static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
