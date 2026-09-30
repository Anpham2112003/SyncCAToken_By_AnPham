using SyncCAToken_By_AnPham.Models;

namespace SyncCAToken_By_AnPham.Services;

public interface ISignatureService
{
    Task<SignResult> SignHashAsync(SignHashRequest request);
    Task<SignResult> SignDataAsync(SignDataRequest request);
}
