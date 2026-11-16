using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Chirograph.Tests.Scaffolding;

/// <summary>
///     Self-signed certificates made in code for a test, so no private key is ever committed: the server certificate of
///     <see cref="RocketsTlsApi" />, and client certificates written to a scaffold as the files an application's configuration names.
/// </summary>
internal static class TestCertificates
{
    /// <summary>The password of the PKCS#12 file and of the encrypted PEM key <see cref="WriteClientCertificate" /> writes.</summary>
    public const string Password = "rockets-test-password";

    /// <summary>
    ///     A self-signed certificate with its private key, for <paramref name="subject" /> (a CN), usable for client authentication, and
    ///     for server authentication of <c>127.0.0.1</c> and <c>localhost</c>.
    /// </summary>
    public static X509Certificate2 Create(string subject)
    {
        using var ephemeral = CreateEphemeral(subject);
        // through PKCS#12, so the key is one that every platform's TLS stack can use
        return X509CertificateLoader.LoadPkcs12(ephemeral.Export(X509ContentType.Pkcs12), null);
    }

    // the certificate as CertificateRequest makes it: its key can be exported, but not every TLS stack takes it
    private static X509Certificate2 CreateEphemeral(string subject)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest($"CN={subject}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));
        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                [
                    new Oid("1.3.6.1.5.5.7.3.1"), // server authentication
                    new Oid("1.3.6.1.5.5.7.3.2"), // client authentication
                ],
                false
            )
        );
        var names = new SubjectAlternativeNameBuilder();
        names.AddIpAddress(System.Net.IPAddress.Loopback);
        names.AddDnsName("localhost");
        request.CertificateExtensions.Add(names.Build());

        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(7));
    }

    /// <summary>
    ///     Makes a client certificate and writes it to <paramref name="directory" /> in each form the configuration keys take:
    ///     <c>client.pfx</c> (PKCS#12, with <see cref="Password" />), <c>client.crt</c> with <c>client.key</c> (a PEM pair),
    ///     <c>client-encrypted.key</c> (the PEM key encrypted with <see cref="Password" />), and <c>client.pem</c> (the certificate and
    ///     its key in one PEM file).
    /// </summary>
    /// <returns>The certificate's thumbprint, which <see cref="RocketsTlsApi" /> echoes.</returns>
    public static string WriteClientCertificate(string directory)
    {
        Directory.CreateDirectory(directory);
        using var certificate = CreateEphemeral("rockets-client");
        using var key = certificate.GetRSAPrivateKey()!;
        var certificatePem = certificate.ExportCertificatePem();
        var keyPem = key.ExportPkcs8PrivateKeyPem();

        File.WriteAllBytes(Path.Combine(directory, "client.pfx"), certificate.Export(X509ContentType.Pkcs12, Password));
        File.WriteAllText(Path.Combine(directory, "client.crt"), certificatePem);
        File.WriteAllText(Path.Combine(directory, "client.key"), keyPem);
        File.WriteAllText(
            Path.Combine(directory, "client-encrypted.key"),
            key.ExportEncryptedPkcs8PrivateKeyPem(Password, new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 100_000))
        );
        File.WriteAllText(Path.Combine(directory, "client.pem"), certificatePem + "\n" + keyPem);
        return certificate.Thumbprint;
    }
}
