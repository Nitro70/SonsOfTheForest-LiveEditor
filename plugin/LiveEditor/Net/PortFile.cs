using System.Security.Cryptography;

namespace LiveEditor.Net;

/// <summary>
/// Writes UserData\LiveEditor.port (PLAN.md 4.2): "&lt;port&gt;\n&lt;token&gt;\n".
/// Regenerated every launch — the external app reads this file instead of
/// scanning ports or hardcoding config.
/// </summary>
public static class PortFile
{
    public static string NewToken()
    {
        Span<byte> bytes = stackalloc byte[16]; // 128-bit
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static void Write(string path, int port, string token)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, $"{port}\n{token}\n");
    }

    public static bool FixedTimeEquals(string a, string b)
    {
        var aBytes = System.Text.Encoding.UTF8.GetBytes(a);
        var bBytes = System.Text.Encoding.UTF8.GetBytes(b);
        return CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
    }
}
