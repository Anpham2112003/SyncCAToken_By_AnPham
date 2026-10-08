using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using SyncCAToken_By_AnPham.Models;

namespace SyncCAToken_By_AnPham.Services;

public class CertificateService : ICertificateService
{
    private static readonly string[] KnownVietnameseCaKeywords =
    [
        "VIETTEL", "VNPT", "BKAV", "FPT", "MISA", "CA2", "NACENCOMM",
        "EASYCA", "FASTCA", "SMARTSIGN", "VINACA", "NEWTEL", "SAFE-CA",
        "NC-CA", "I-CA", "CMC-CA"
    ];

    public IEnumerable<CertificateDto> GetCertificates(bool validOnly = true, bool hardwareOnly = false)
    {
        var result = new List<CertificateDto>();

        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);

        foreach (var cert in store.Certificates)
        {
            try
            {
                // Chỉ quan tâm chứng thư có Private Key
                if (!cert.HasPrivateKey)
                    continue;

                bool isValid = DateTime.Now >= cert.NotBefore && DateTime.Now <= cert.NotAfter;
                if (validOnly && !isValid)
                    continue;

                bool isHardware = IsSmartCardOrHardwareToken(cert);
                if (hardwareOnly && !isHardware)
                    continue;

                result.Add(MapToDto(cert, isValid, isHardware));
            }
            catch
            {
                // Bỏ qua chứng thư bị lỗi truy cập hoặc không tương thích
            }
        }

        return result;
    }

    public CertificateDto? GetCertificateDetail(string thumbprint)
    {
        using var cert = FindCertificate(thumbprint);
        if (cert == null)
            return null;

        bool isValid = DateTime.Now >= cert.NotBefore && DateTime.Now <= cert.NotAfter;
        bool isHardware = IsSmartCardOrHardwareToken(cert);

        var detail = MapToDto(cert, isValid, isHardware);
        detail.CertificateBase64 = Convert.ToBase64String(cert.RawData);
        return detail;
    }

    public X509Certificate2? FindCertificate(string thumbprint)
    {
        if (string.IsNullOrWhiteSpace(thumbprint))
            return null;

        string cleanThumbprint = CleanThumbprint(thumbprint);

        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);

        var matches = store.Certificates.Find(X509FindType.FindByThumbprint, cleanThumbprint, false);
        if (matches.Count > 0)
        {
            return matches[0];
        }

        // Thử tìm thủ công nếu FindByThumbprint không bắt được do định dạng chuỗi
        foreach (var cert in store.Certificates)
        {
            if (string.Equals(CleanThumbprint(cert.Thumbprint), cleanThumbprint, StringComparison.OrdinalIgnoreCase))
            {
                return cert;
            }
        }

        return null;
    }

    public TokenStatusDto GetStatus()
    {
        var allCerts = GetCertificates(validOnly: false, hardwareOnly: false).ToList();
        return new TokenStatusDto
        {
            IsRunning = true,
            ActiveCertificatesCount = allCerts.Count(c => c.IsValid),
            HardwareTokensCount = allCerts.Count(c => c.IsHardwareToken),
            ServerTime = DateTime.UtcNow
        };
    }

    private static CertificateDto MapToDto(X509Certificate2 cert, bool isValid, bool isHardware)
    {
        string subject = cert.Subject;
        string commonName = cert.GetNameInfo(X509NameType.SimpleName, false) ?? string.Empty;
        string issuerName = cert.GetNameInfo(X509NameType.SimpleName, true) ?? cert.Issuer;

        int keySize = 0;
        string keyAlgorithm = cert.PublicKey.Oid.FriendlyName ?? cert.PublicKey.Oid.Value ?? "Unknown";

        try
        {
            using var rsa = cert.GetRSAPrivateKey();
            if (rsa != null)
            {
                keySize = rsa.KeySize;
            }
        }
        catch
        {
            // Tránh pop-up PIN khi chỉ đọc thông số public key
            try
            {
                using var rsaPublic = cert.GetRSAPublicKey();
                if (rsaPublic != null)
                {
                    keySize = rsaPublic.KeySize;
                }
            }
            catch { }
        }

        return new CertificateDto
        {
            Thumbprint = cert.Thumbprint,
            SerialNumber = cert.SerialNumber,
            Subject = subject,
            CommonName = commonName,
            Organization = ExtractOrganization(subject),
            TaxCode = ExtractTaxCode(subject),
            Issuer = cert.Issuer,
            IssuerName = issuerName,
            ValidFrom = cert.NotBefore,
            ValidTo = cert.NotAfter,
            IsValid = isValid,
            HasPrivateKey = cert.HasPrivateKey,
            IsHardwareToken = isHardware,
            KeyAlgorithm = keyAlgorithm,
            KeySize = keySize
        };
    }

    private static bool IsSmartCardOrHardwareToken(X509Certificate2 cert)
    {
        // 1. Kiểm tra Nhà cung cấp CA phổ biến tại Việt Nam
        string fullInfo = $"{cert.Issuer} {cert.Subject}".ToUpperInvariant();
        if (KnownVietnameseCaKeywords.Any(k => fullInfo.Contains(k)))
        {
            return true;
        }

        // 2. Thử kiểm tra thuộc tính phần cứng từ Key Container (không kích hoạt prompt PIN)
        try
        {
            using var rsa = cert.GetRSAPrivateKey();
            if (rsa is RSACryptoServiceProvider rsaCsp)
            {
                if (rsaCsp.CspKeyContainerInfo.HardwareDevice)
                    return true;

                string? provName = rsaCsp.CspKeyContainerInfo.ProviderName?.ToUpperInvariant();
                if (provName != null && (provName.Contains("SMART CARD") || provName.Contains("TOKEN") || provName.Contains("CSP")))
                    return true;
            }
            else if (rsa is RSACng rsaCng)
            {
                string? provider = rsaCng.Key.Provider?.Provider?.ToUpperInvariant();
                if (provider != null && (provider.Contains("SMART CARD") || provider.Contains("TOKEN")))
                    return true;
            }
        }
        catch
        {
            // Bỏ qua lỗi truy cập thông tin provider
        }

        return false;
    }

    private static string? ExtractTaxCode(string subject)
    {
        // Nhận diện MST: dạng MST:010..., UID=010..., OID.2.5.4.5=010...
        var match = Regex.Match(
            subject,
            @"(?:MST|UID|2\.5\.4\.5|0\.9\.2342\.19200300\.100\.1\.1)[\s:=]+([0-9A-Z\-]+)",
            RegexOptions.IgnoreCase
        );

        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static string? ExtractOrganization(string subject)
    {
        var match = Regex.Match(subject, @"(?:^|,\s*)O=([^,]+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static string CleanThumbprint(string thumbprint)
    {
        return Regex.Replace(thumbprint, @"[^0-9a-fA-F]", string.Empty).ToUpperInvariant();
    }
}
