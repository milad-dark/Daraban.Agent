using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;

namespace Daraban.Agent.Core.Transport;

/// <summary>
/// Resolves SSL client certificates and server fingerprint pinning, mirroring
/// glpi-agent's `ssl-keystore` and `ssl-fingerprint` options.
///
/// ssl-keystore behavior:
///   Windows — comma-separated logical store names, searched in LocalMachine then
///     CurrentUser: "My", "CA", "Root", "User-My", "User-CA", "User-Root".
///     The search is case-insensitive.
///   macOS   — user keychain is used by default (daemon runs as root).
///   Linux   — no keystore support, returns null (client certs should then come
///     from ssl-cert-file, not wired here).
/// </summary>
public static class SslCertificateProvider
{
    private static readonly Dictionary<string, StoreName> WindowsStoreNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["My"] = StoreName.My,
        ["CA"] = StoreName.CertificateAuthority,
        ["Root"] = StoreName.Root,
        ["User-My"] = StoreName.My,
        ["User-CA"] = StoreName.CertificateAuthority,
        ["User-Root"] = StoreName.Root,
    };

    private static readonly Dictionary<string, StoreLocation> WindowsStoreLocations = new(StringComparer.OrdinalIgnoreCase)
    {
        ["My"] = StoreLocation.LocalMachine,
        ["CA"] = StoreLocation.LocalMachine,
        ["Root"] = StoreLocation.LocalMachine,
        ["User-My"] = StoreLocation.CurrentUser,
        ["User-CA"] = StoreLocation.CurrentUser,
        ["User-Root"] = StoreLocation.CurrentUser,
    };

    /// <summary>
    /// Returns the first valid client certificate found in the configured keystore,
    /// or null when no keystore is configured / nothing suitable is found.
    /// </summary>
    public static X509Certificate2? GetClientCertificate(string? keystoreSpec)
    {
        if (string.IsNullOrWhiteSpace(keystoreSpec))
            return null;

        if (OperatingSystem.IsWindows())
            return FindInWindowsStore(keystoreSpec);

        if (OperatingSystem.IsMacOS())
            return FindInMacKeychain();

        // Linux: no native keystore; client certs come from ssl-cert-file instead.
        return null;
    }

    /// <summary>
    /// True when a configured fingerprint matches the presented server certificate's
    /// SHA-256. Returns false when the presented cert is null. When fingerprint is null
    /// (not configured), returns true so default validation continues normally.
    /// </summary>
    public static bool FingerprintMatches(string? expectedFingerprint, X509Certificate2? presentedCert)
    {
        if (presentedCert is null)
            return false;

        if (string.IsNullOrWhiteSpace(expectedFingerprint))
            return true; // no pin configured — let the default chain validation decide

        var thumbprint = Convert.ToHexString(presentedCert.GetCertHash(HashAlgorithmName.SHA256));
        var normalized = expectedFingerprint.Replace(":", "", StringComparison.OrdinalIgnoreCase)
                                            .Replace("-", "", StringComparison.OrdinalIgnoreCase);

        return thumbprint.Equals(normalized, StringComparison.OrdinalIgnoreCase);
    }

    private static X509Certificate2? FindInWindowsStore(string spec)
    {
        foreach (var token in spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!WindowsStoreNames.TryGetValue(token, out var name) ||
                !WindowsStoreLocations.TryGetValue(token, out var location))
                continue;

            try
            {
                using var store = new X509Store(name, location);
                store.Open(OpenFlags.ReadOnly);
                foreach (var cert in store.Certificates)
                {
                    if (cert.HasPrivateKey)
                        return cert;
                }
            }
            catch
            {
                // Store may not exist or be unreadable — try the next one.
            }
        }

        return null;
    }

    private static X509Certificate2? FindInMacKeychain()
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "/usr/bin/security",
                Arguments = "find-certificate -a -p -c \"\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc is null)
                return null;

            var pem = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(3000);

            // First PEM cert block that loads as a certificate wins.
            var matches = Regex.Matches(pem, @"-----BEGIN CERTIFICATE-----[\s\S]*?-----END CERTIFICATE-----");
            foreach (Match m in matches)
            {
                try
                {
                    var cert = X509CertificateLoader.LoadCertificate(Encoding.UTF8.GetBytes(m.Value));
                    if (cert.HasPrivateKey)
                        return cert;
                }
                catch
                {
                    // keep scanning
                }
            }
        }
        catch
        {
            // security command unavailable — no keystore this run
        }

        return null;
    }
}