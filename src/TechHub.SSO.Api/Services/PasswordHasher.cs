using Microsoft.AspNetCore.Cryptography.KeyDerivation;

namespace TechHub.SSO.Api.Services;

/// <summary>
/// Hasher de contraseñas basado en PBKDF2 (HMAC-SHA512, 100k iteraciones) con
/// salt aleatorio por credencial. Formato almacenado:
///   v1|&lt;iteraciones&gt;|&lt;base64(salt)&gt;|&lt;base64(subkey)&gt;
/// La verificación usa comparación en tiempo constante.
/// </summary>
public static class PasswordHasher
{
    private const int SaltSize = 128 / 8;      // 16 bytes
    private const int SubKeySize = 256 / 8;    // 32 bytes
    private const int Iterations = 100_000;
    private const string Prefix = "v1";

    /// <summary>Hash de referencia usado para igualar el coste cuando el usuario no existe.</summary>
    public static readonly string DummyHash = Hash("invalid-password-placeholder");

    public static string Hash(string password)
    {
        byte[] salt = new byte[SaltSize];
        System.Security.Cryptography.RandomNumberGenerator.Fill(salt);

        byte[] subKey = KeyDerivation.Pbkdf2(
            password: password,
            salt: salt,
            prf: KeyDerivationPrf.HMACSHA512,
            iterationCount: Iterations,
            numBytesRequested: SubKeySize);

        return $"{Prefix}|{Iterations}|{Convert.ToBase64String(salt)}|{Convert.ToBase64String(subKey)}";
    }

    public static bool Verify(string password, string stored)
    {
        var parts = stored.Split('|');
        if (parts.Length != 4 || parts[0] != Prefix)
            return false;

        if (!int.TryParse(parts[1], out var iterations))
            return false;

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

        byte[] actual = KeyDerivation.Pbkdf2(
            password: password,
            salt: salt,
            prf: KeyDerivationPrf.HMACSHA512,
            iterationCount: iterations,
            numBytesRequested: expected.Length);

        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
