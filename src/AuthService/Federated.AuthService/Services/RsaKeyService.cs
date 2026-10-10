using System;
using System.IO;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace Federated.AuthService.Services;

public interface IRsaKeyService
{
    string KeyId { get; }
    RsaSecurityKey GetSigningKey();
    object GetJwks();
}

public class RsaKeyService : IRsaKeyService
{
    private const string DefaultKeyId = "federated-rsa-key-1";
    private readonly RSA _rsa;
    private readonly RsaSecurityKey _securityKey;

    public string KeyId => DefaultKeyId;

    public RsaKeyService(string? keysDirectory = null)
    {
        string dir = keysDirectory ?? Path.Combine(AppContext.BaseDirectory, "keys");
        Directory.CreateDirectory(dir);
        string keyPath = Path.Combine(dir, "private-key.pem");

        _rsa = RSA.Create(2048);

        if (File.Exists(keyPath))
        {
            string pem = File.ReadAllText(keyPath);
            _rsa.ImportFromPem(pem);
        }
        else
        {
            string pem = _rsa.ExportPkcs8PrivateKeyPem();
            File.WriteAllText(keyPath, pem);
        }

        _securityKey = new RsaSecurityKey(_rsa)
        {
            KeyId = DefaultKeyId
        };
    }

    public RsaSecurityKey GetSigningKey() => _securityKey;

    public object GetJwks()
    {
        RSAParameters parameters = _rsa.ExportParameters(false);

        return new
        {
            keys = new[]
            {
                new
                {
                    kty = "RSA",
                    use = "sig",
                    alg = "RS256",
                    kid = DefaultKeyId,
                    n = Base64UrlEncoder.Encode(parameters.Modulus),
                    e = Base64UrlEncoder.Encode(parameters.Exponent)
                }
            }
        };
    }
}
