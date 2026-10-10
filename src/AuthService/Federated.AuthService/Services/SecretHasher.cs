using System;
using System.Security.Cryptography;

namespace Federated.AuthService.Services;

public interface ISecretHasher
{
    string HashSecret(string rawSecret);
    bool VerifySecret(string rawSecret, string hash);
}

public class SecretHasher : ISecretHasher
{
    private const int SaltSize = 16; // 128-bit
    private const int KeySize = 32;  // 256-bit
    private const int Iterations = 100_000;
    private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA256;

    public string HashSecret(string rawSecret)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
            rawSecret,
            salt,
            Iterations,
            Algorithm,
            KeySize);

        // Format: {iterations}.{saltBase64}.{hashBase64}
        return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public bool VerifySecret(string rawSecret, string hash)
    {
        if (string.IsNullOrWhiteSpace(hash)) return false;

        string[] parts = hash.Split('.', 3);
        if (parts.Length != 3) return false;

        if (!int.TryParse(parts[0], out int iterations)) return false;
        byte[] salt = Convert.FromBase64String(parts[1]);
        byte[] expectedHash = Convert.FromBase64String(parts[2]);

        byte[] actualHash = Rfc2898DeriveBytes.Pbkdf2(
            rawSecret,
            salt,
            iterations,
            Algorithm,
            expectedHash.Length);

        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }
}
