namespace SyncCAToken_By_AnPham.Models;

public class TokenStatusDto
{
    public bool IsRunning { get; set; } = true;
    public string MachineName { get; set; } = Environment.MachineName;
    public string OsVersion { get; set; } = Environment.OSVersion.ToString();
    public int ActiveCertificatesCount { get; set; }
    public int HardwareTokensCount { get; set; }
    public DateTime ServerTime { get; set; } = DateTime.UtcNow;
}
