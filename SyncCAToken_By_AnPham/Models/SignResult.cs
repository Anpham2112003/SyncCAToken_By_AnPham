namespace SyncCAToken_By_AnPham.Models;

public class SignResult
{
    public bool Success { get; set; }
    public string? SignatureBase64 { get; set; }
    public string? Thumbprint { get; set; }
    public string? SignerSubject { get; set; }
    public string? Algorithm { get; set; }
    public DateTime SignedAt { get; set; } = DateTime.UtcNow;
    public string? Error { get; set; }
}
