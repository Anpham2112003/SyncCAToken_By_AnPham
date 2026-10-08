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

public class BatchItemSignature
{
    public string Id { get; set; } = string.Empty;
    public string SignatureBase64 { get; set; } = string.Empty;
}

public class BatchSignSuccessData
{
    public int TotalSigned { get; set; }
    public string Thumbprint { get; set; } = string.Empty;
    public string SignerSubject { get; set; } = string.Empty;
    public string Algorithm { get; set; } = string.Empty;
    public DateTime SignedAt { get; set; } = DateTime.UtcNow;
    public List<BatchItemSignature> Signatures { get; set; } = [];
}
