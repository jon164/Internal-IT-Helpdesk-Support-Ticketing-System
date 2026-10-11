using System.Security.Cryptography;

namespace Helpdesk.Core.Security;

/// <summary>
/// Salted PBKDF2-HMAC-SHA256 password hashing using only the .NET base class library.
/// Stored format: <c>PBKDF2-SHA256$iterations$base64(salt)$base64(hash)</c>.
/// The iteration count travels with the hash, so it can be raised later without
/// invalidating existing passwords.
/// </summary>
public sealed class PasswordHasher
{
    /// <summary>OWASP Password Storage Cheat Sheet figure for PBKDF2-HMAC-SHA256.</summary>
    public const int DefaultIterations = 600_000;

    private const string Scheme = "PBKDF2-SHA256";
    private const int SaltBytes = 16;
    private const int HashBytes = 32;
    private const int MinIterations = 1_000;
    private const int MaxIterations = 5_000_000;

    private readonly int _iterations;
    private readonly Lazy<string> _dummyHash;

    public PasswordHasher(int iterations = DefaultIterations)
    {
        if (iterations < MinIterations || iterations > MaxIterations)
        {
            throw new ArgumentOutOfRangeException(nameof(iterations), $"Iterations must be between {MinIterations} and {MaxIterations}.");
        }

        _iterations = iterations;
        _dummyHash = new Lazy<string>(() => Hash(Convert.ToBase64String(RandomNumberGenerator.GetBytes(16))));
    }

    /// <summary>
    /// Performs the same work as a real verification against a throwaway hash and always
    /// returns false. Used when the account does not exist, so an unknown username takes as
    /// long to reject as a wrong password and response time does not reveal which it was.
    /// </summary>
    public bool VerifyAgainstDummy(string password)
    {
        _ = Verify(password ?? string.Empty, _dummyHash.Value);
        return false;
    }

    public string Hash(string password)
    {
        ArgumentNullException.ThrowIfNull(password);

        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, _iterations, HashAlgorithmName.SHA256, HashBytes);
        return $"{Scheme}${_iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    /// <summary>
    /// Returns false — never throws — for a wrong password, a null or blank stored value,
    /// or a stored value that is malformed. Comparison is constant-time.
    /// </summary>
    public bool Verify(string password, string? storedHash)
    {
        if (password is null || string.IsNullOrWhiteSpace(storedHash))
        {
            return false;
        }

        var parts = storedHash.Split('$');
        if (parts.Length != 4 || parts[0] != Scheme)
        {
            return false;
        }

        if (!int.TryParse(parts[1], out var iterations) || iterations < MinIterations || iterations > MaxIterations)
        {
            return false;
        }

        byte[] salt;
        byte[] expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        if (salt.Length == 0 || expected.Length == 0)
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
