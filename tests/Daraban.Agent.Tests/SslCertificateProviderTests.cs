using Daraban.Agent.Core.Transport;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Daraban.Agent.Tests;

public class SslCertificateProviderTests
{
    [Fact]
    public void GetClientCertificate_NoKeystore_ReturnsNull()
    {
        Assert.Null(SslCertificateProvider.GetClientCertificate(null));
        Assert.Null(SslCertificateProvider.GetClientCertificate(""));
        Assert.Null(SslCertificateProvider.GetClientCertificate("   "));
    }

    [Fact]
    public void FingerprintMatches_NoConfiguredFingerprint_ReturnsTrue()
    {
        using var cert = CreateSelfSigned();

        Assert.True(SslCertificateProvider.FingerprintMatches(null, cert));
        Assert.True(SslCertificateProvider.FingerprintMatches("", cert));
    }

    [Fact]
    public void FingerprintMatches_NullCert_ReturnsFalse()
    {
        Assert.False(SslCertificateProvider.FingerprintMatches(null, null));
        Assert.False(SslCertificateProvider.FingerprintMatches("AA:BB", null));
    }

    [Fact]
    public void FingerprintMatches_ColonSeparatedHex_MatchesCertificateSha256()
    {
        using var cert = CreateSelfSigned();
        var raw = Convert.ToHexString(cert.GetCertHash(HashAlgorithmName.SHA256));

        // Insert colons every 2 chars: "AABBCC" -> "AA:BB:CC"
        var colonFormatted = string.Join(":", Enumerable.Range(0, raw.Length / 2)
            .Select(i => raw.Substring(i * 2, 2)));

        Assert.True(SslCertificateProvider.FingerprintMatches(colonFormatted, cert));
    }

    [Fact]
    public void FingerprintMatches_WrongFingerprint_ReturnsFalse()
    {
        using var cert = CreateSelfSigned();
        const string wrong = "00:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00";

        Assert.False(SslCertificateProvider.FingerprintMatches(wrong, cert));
    }

    private static X509Certificate2 CreateSelfSigned()
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("CN=test-cert", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
    }
}