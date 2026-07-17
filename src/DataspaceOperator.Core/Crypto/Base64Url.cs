using System.Text;

namespace DataspaceOperator.Core.Crypto;

/// <summary>Base64URL (RFC 7515) without padding — the encoding used by JWS/JWT.</summary>
public static class Base64Url
{
    public static string Encode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string Encode(string utf8) => Encode(Encoding.UTF8.GetBytes(utf8));

    public static byte[] Decode(string input)
    {
        var s = input.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }
        return Convert.FromBase64String(s);
    }

    public static string DecodeToString(string input) => Encoding.UTF8.GetString(Decode(input));
}
