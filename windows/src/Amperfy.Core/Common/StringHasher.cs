using System.Security.Cryptography;

namespace Amperfy.Core.Common;

public static class StringHasher
{
    public static string Sha256(string dataString) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(dataString)));

    public static string Md5Hex(string dataString) =>
        Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(dataString)));

    public static string Md5Base64(string dataString) =>
        Convert.ToBase64String(MD5.HashData(Encoding.UTF8.GetBytes(dataString)));
}
