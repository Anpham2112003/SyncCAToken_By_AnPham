using System.Security.Cryptography.X509Certificates;
using SyncCAToken_By_AnPham.Models;

namespace SyncCAToken_By_AnPham.Services;

public interface ICertificateService
{
    IEnumerable<CertificateDto> GetCertificates(bool validOnly = true, bool hardwareOnly = false);
    CertificateDto? GetCertificateDetail(string thumbprint);
    X509Certificate2? FindCertificate(string thumbprint);
    TokenStatusDto GetStatus();
}
