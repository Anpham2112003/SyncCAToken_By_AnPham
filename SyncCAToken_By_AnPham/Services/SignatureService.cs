using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using SyncCAToken_By_AnPham.Models;

namespace SyncCAToken_By_AnPham.Services;

public class SignatureService : ISignatureService
{
    private readonly ICertificateService _certificateService;
    private readonly ILogger<SignatureService> _logger;

    public SignatureService(ICertificateService certificateService, ILogger<SignatureService> logger)
    {
        _certificateService = certificateService;
        _logger = logger;
    }

    public async Task<SignResult> SignHashAsync(SignHashRequest request)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Thumbprint))
                    return new SignResult { Success = false, Error = "Thumbprint không được để trống." };

                if (string.IsNullOrWhiteSpace(request.HashBase64))
                    return new SignResult { Success = false, Error = "HashBase64 không được để trống." };

                byte[] hashBytes;
                try
                {
                    hashBytes = Convert.FromBase64String(request.HashBase64);
                }
                catch (FormatException)
                {
                    return new SignResult { Success = false, Error = "Dữ liệu HashBase64 không đúng định dạng Base64." };
                }

                var hashAlgo = ParseHashAlgorithm(request.HashAlgorithm);
                ValidateHashLength(hashBytes, hashAlgo);

                using var cert = _certificateService.FindCertificate(request.Thumbprint);
                if (cert == null)
                {
                    return new SignResult
                    {
                        Success = false,
                        Error = $"Không tìm thấy chứng thư số với Thumbprint '{request.Thumbprint}' trong Windows Store."
                    };
                }

                if (!cert.HasPrivateKey)
                {
                    return new SignResult
                    {
                        Success = false,
                        Error = "Chứng thư số này không có Private Key để thực hiện ký số."
                    };
                }

                // Thực hiện ký bằng RSA (99% USB Token tại VN dùng RSA 2048)
                using var rsa = cert.GetRSAPrivateKey();
                if (rsa != null)
                {
                    // Lệnh này sẽ kích hoạt popup nhập PIN của Windows/USB Token nếu chưa nhập PIN
                    byte[] signatureBytes = rsa.SignHash(hashBytes, hashAlgo, RSASignaturePadding.Pkcs1);

                    return new SignResult
                    {
                        Success = true,
                        SignatureBase64 = Convert.ToBase64String(signatureBytes),
                        Thumbprint = cert.Thumbprint,
                        SignerSubject = cert.Subject,
                        Algorithm = $"{hashAlgo.Name}withRSA",
                        SignedAt = DateTime.UtcNow
                    };
                }

                // Hỗ trợ trường hợp chữ ký số thế hệ mới dùng ECDSA
                using var ecdsa = cert.GetECDsaPrivateKey();
                if (ecdsa != null)
                {
                    byte[] signatureBytes = ecdsa.SignHash(hashBytes);

                    return new SignResult
                    {
                        Success = true,
                        SignatureBase64 = Convert.ToBase64String(signatureBytes),
                        Thumbprint = cert.Thumbprint,
                        SignerSubject = cert.Subject,
                        Algorithm = $"{hashAlgo.Name}withECDSA",
                        SignedAt = DateTime.UtcNow
                    };
                }

                return new SignResult
                {
                    Success = false,
                    Error = "Không thể truy xuất Private Key (RSA/ECDSA) từ thiết bị USB Token."
                };
            }
            catch (CryptographicException ex)
            {
                _logger.LogError(ex, "Lỗi mã hóa/ký số qua USB Token");
                return new SignResult
                {
                    Success = false,
                    Error = MapCryptographicError(ex)
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi không xác định khi thực hiện ký số");
                return new SignResult
                {
                    Success = false,
                    Error = $"Lỗi khi ký số: {ex.Message}"
                };
            }
        });
    }

    public async Task<SignResult> SignDataAsync(SignDataRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.DataBase64))
        {
            return new SignResult { Success = false, Error = "DataBase64 không được để trống." };
        }

        byte[] rawBytes;
        try
        {
            rawBytes = Convert.FromBase64String(request.DataBase64);
        }
        catch (FormatException)
        {
            return new SignResult { Success = false, Error = "Dữ liệu DataBase64 không đúng định dạng Base64." };
        }

        var hashAlgo = ParseHashAlgorithm(request.HashAlgorithm);
        byte[] hashBytes = ComputeHash(rawBytes, hashAlgo);

        var signHashRequest = new SignHashRequest
        {
            Thumbprint = request.Thumbprint,
            HashBase64 = Convert.ToBase64String(hashBytes),
            HashAlgorithm = request.HashAlgorithm
        };

        return await SignHashAsync(signHashRequest);
    }

    private static HashAlgorithmName ParseHashAlgorithm(string? algorithm)
    {
        return (algorithm?.Trim().ToUpperInvariant()) switch
        {
            "SHA1" => HashAlgorithmName.SHA1,
            "SHA384" => HashAlgorithmName.SHA384,
            "SHA512" => HashAlgorithmName.SHA512,
            _ => HashAlgorithmName.SHA256 // Mặc định SHA256
        };
    }

    private static void ValidateHashLength(byte[] hashBytes, HashAlgorithmName algo)
    {
        int expectedLen = algo.Name switch
        {
            "SHA1" => 20,
            "SHA256" => 32,
            "SHA384" => 48,
            "SHA512" => 64,
            _ => 32
        };

        if (hashBytes.Length != expectedLen)
        {
            throw new ArgumentException(
                $"Độ dài dữ liệu băm không khớp với thuật toán {algo.Name}. Cần {expectedLen} bytes, nhận được {hashBytes.Length} bytes."
            );
        }
    }

    private static byte[] ComputeHash(byte[] data, HashAlgorithmName algo)
    {
        if (algo == HashAlgorithmName.SHA256) return SHA256.HashData(data);
        if (algo == HashAlgorithmName.SHA512) return SHA512.HashData(data);
        if (algo == HashAlgorithmName.SHA384) return SHA384.HashData(data);
        if (algo == HashAlgorithmName.SHA1) return SHA1.HashData(data);

        return SHA256.HashData(data);
    }

    private static string MapCryptographicError(CryptographicException ex)
    {
        string message = ex.Message.ToLowerInvariant();
        int hresult = ex.HResult;

        // Các mã lỗi SmartCard / CryptoAPI thông dụng trên Windows
        // 0x8010006E: SCARD_W_CANCELLED_BY_USER
        // 0x8010006B: SCARD_W_WRONG_CHV (Sai PIN)
        // 0x8010006C: SCARD_W_CHV_BLOCKED (Khóa PIN)
        // 0x8010000C: SCARD_E_NO_SMARTCARD (Rút token)
        if (hresult == unchecked((int)0x8010006E) || message.Contains("cancelled") || message.Contains("hủy"))
        {
            return "Người dùng đã hủy thao tác nhập mã PIN USB Token.";
        }

        if (hresult == unchecked((int)0x8010006B) || message.Contains("pin") || message.Contains("chv"))
        {
            return "Mã PIN USB Token không chính xác.";
        }

        if (hresult == unchecked((int)0x8010006C) || message.Contains("blocked"))
        {
            return "USB Token đã bị khóa PIN do nhập sai nhiều lần.";
        }

        if (hresult == unchecked((int)0x8010000C) || message.Contains("no smartcard") || message.Contains("smart card"))
        {
            return "Không tìm thấy USB Token. Vui lòng kiểm tra lại kết nối USB Token.";
        }

        return $"Lỗi chữ ký số: {ex.Message}";
    }
}
