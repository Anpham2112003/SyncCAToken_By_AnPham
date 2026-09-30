using System.Text.Json.Serialization;

namespace SyncCAToken_By_AnPham.Models;

public class CertificateDto
{
    public string Thumbprint { get; set; } = string.Empty;
    public string SerialNumber { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string CommonName { get; set; } = string.Empty;
    public string? Organization { get; set; }
    public string? TaxCode { get; set; }
    public string Issuer { get; set; } = string.Empty;
    public string IssuerName { get; set; } = string.Empty;
    public DateTime ValidFrom { get; set; }
    public DateTime ValidTo { get; set; }
    public bool IsValid { get; set; }
    public bool HasPrivateKey { get; set; }
    public bool IsHardwareToken { get; set; }
    public string KeyAlgorithm { get; set; } = string.Empty;
    public int KeySize { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CertificateBase64 { get; set; }
}
