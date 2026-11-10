using System.Security.Cryptography;

namespace Company.Api.Auth;

/// <summary>
/// Password storage and the rules for passwords and user names. A password is kept as a salted PBKDF2 hash (SHA-256, 600 000 rounds);
/// nothing that would let anyone read it back is stored. A session token is random and only its SHA-256 is kept.
/// </summary>
public static class Passwords
{
    private const int Rounds = 600_000;
    public const int MinimumLength = 10;
    private static readonly string Dummy = Hash("not-a-real-password-just-for-timing");

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Rounds, HashAlgorithmName.SHA256, 32);
        return $"v1.{Rounds}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string stored)
    {
        var parts = stored.Split('.');
        if (parts.Length != 4 || parts[0] != "v1" || !int.TryParse(parts[1], out var rounds)) return false;

        var salt = Convert.FromBase64String(parts[2]);
        var expected = Convert.FromBase64String(parts[3]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, rounds, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    /// <summary>Spends the time of a check when the user name does not exist, so that the answer does not tell whether it does</summary>
    public static void SpendTheTimeOfACheck(string password) => Verify(password, Dummy);

    /// <summary>The reason a password is refused, or null</summary>
    public static string? Refusal(string? password, string username)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinimumLength) return $"The password needs at least {MinimumLength} characters.";
        if (password.Length > 200) return "The password is too long.";
        if (password.Equals(username, StringComparison.OrdinalIgnoreCase)) return "The password cannot be the user name.";
        return null;
    }

    /// <summary>User names are lower case letters, digits, dot, dash and underscore, 3 to 50 characters</summary>
    public static string? UsernameRefusal(string normalized) =>
        normalized.Length is < 3 or > 50 || !normalized.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '.' or '-' or '_')
            ? "A user name has 3 to 50 characters: letters, digits, dot, dash or underscore."
            : null;

    /// <summary>A temporary password for a reset: 14 characters without the ones that are easy to mix up (0/O, 1/l/I)</summary>
    public static string NewTemporary()
    {
        const string alphabet = "abcdefghjkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var chars = new char[14];
        for (var i = 0; i < chars.Length; i++) chars[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
        return new string(chars);
    }

    public static string NewToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    public static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));
}
