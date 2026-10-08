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
            byte[]? hashBytes = null;
            byte[]? signatureBytes = null;

            try
            {
                if (string.IsNullOrWhiteSpace(request.Thumbprint))
                    return new SignResult { Success = false, Error = "Thumbprint không được để trống." };

                if (string.IsNullOrWhiteSpace(request.HashBase64))
                    return new SignResult { Success = false, Error = "HashBase64 không được để trống." };

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
                    signatureBytes = rsa.SignHash(hashBytes, hashAlgo, RSASignaturePadding.Pkcs1);

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
                    signatureBytes = ecdsa.SignHash(hashBytes);

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
            finally
            {
                // Tiêu hủy byte hash và byte chữ ký nhạy cảm trên RAM
                if (hashBytes != null)
                {
                    CryptographicOperations.ZeroMemory(hashBytes);
                }
                if (signatureBytes != null)
                {
                    CryptographicOperations.ZeroMemory(signatureBytes);
                }
            }
        });
    }

    public async Task<SignResult> SignDataAsync(SignDataRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.DataBase64))
        {
            return new SignResult { Success = false, Error = "DataBase64 không được để trống." };
        }

        byte[]? rawBytes = null;
        byte[]? hashBytes = null;

        try
        {
            try
            {
                rawBytes = Convert.FromBase64String(request.DataBase64);
            }
            catch (FormatException)
            {
                return new SignResult { Success = false, Error = "Dữ liệu DataBase64 không đúng định dạng Base64." };
            }

            var hashAlgo = ParseHashAlgorithm(request.HashAlgorithm);
            hashBytes = ComputeHash(rawBytes, hashAlgo);

            var signHashRequest = new SignHashRequest
            {
                Thumbprint = request.Thumbprint,
                HashBase64 = Convert.ToBase64String(hashBytes),
                HashAlgorithm = request.HashAlgorithm
            };

            return await SignHashAsync(signHashRequest);
        }
        finally
        {
            // Tiêu hủy byte thô và byte băm ngay sau khi chuẩn bị xong yêu cầu ký
            if (rawBytes != null)
            {
                CryptographicOperations.ZeroMemory(rawBytes);
            }
            if (hashBytes != null)
            {
                CryptographicOperations.ZeroMemory(hashBytes);
            }
        }
    }

    public async Task<ApiResponse<BatchSignSuccessData>> SignBatchHashAtomicAsync(SignBatchHashRequest request)
    {
        // === GIAI ĐOẠN 1: THẨM ĐỊNH (PRE-VALIDATION) NGOẠI TUYẾN ===
        if (string.IsNullOrWhiteSpace(request.Thumbprint))
        {
            return ApiResponse<BatchSignSuccessData>.Fail("Thumbprint không được để trống.");
        }

        if (request.Items == null || request.Items.Count == 0)
        {
            return ApiResponse<BatchSignSuccessData>.Fail("Danh sách tài liệu cần ký không được để trống.");
        }

        var duplicateIds = request.Items
            .GroupBy(x => x.Id)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicateIds.Count > 0)
        {
            return ApiResponse<BatchSignSuccessData>.Fail($"Phát hiện trùng lặp Id tài liệu trong mẻ ký: {string.Join(", ", duplicateIds)}.");
        }

        var hashAlgo = ParseHashAlgorithm(request.HashAlgorithm);
        var validatedHashes = new List<(string Id, byte[] HashBytes)>(request.Items.Count);

        try
        {
            foreach (var item in request.Items)
            {
                if (string.IsNullOrWhiteSpace(item.Id))
                {
                    return ApiResponse<BatchSignSuccessData>.Fail("Có tài liệu trong danh sách bị thiếu Id định danh.");
                }

                if (string.IsNullOrWhiteSpace(item.HashBase64))
                {
                    return ApiResponse<BatchSignSuccessData>.Fail($"Tài liệu '{item.Id}' bị thiếu dữ liệu băm HashBase64.");
                }

                byte[] rawHash;
                try
                {
                    rawHash = Convert.FromBase64String(item.HashBase64);
                }
                catch (FormatException)
                {
                    return ApiResponse<BatchSignSuccessData>.Fail($"Tài liệu '{item.Id}' có HashBase64 không đúng định dạng Base64.");
                }

                try
                {
                    ValidateHashLength(rawHash, hashAlgo);
                }
                catch (ArgumentException ex)
                {
                    CryptographicOperations.ZeroMemory(rawHash);
                    return ApiResponse<BatchSignSuccessData>.Fail($"Tài liệu '{item.Id}': {ex.Message}");
                }

                validatedHashes.Add((item.Id, rawHash));
            }

            // === GIAI ĐOẠN 2: THỰC THI NGUYÊN TỬ (ATOMIC SIGNING) ===
            var response = await Task.Run(() =>
            {
                using var cert = _certificateService.FindCertificate(request.Thumbprint);
                if (cert == null)
                {
                    return ApiResponse<BatchSignSuccessData>.Fail($"Không tìm thấy chứng thư số với Thumbprint '{request.Thumbprint}' trong Windows Store.");
                }

                if (!cert.HasPrivateKey)
                {
                    return ApiResponse<BatchSignSuccessData>.Fail("Chứng thư số này không có Private Key để thực hiện ký số.");
                }

                using var rsa = cert.GetRSAPrivateKey();
                using var ecdsa = cert.GetECDsaPrivateKey();

                if (rsa == null && ecdsa == null)
                {
                    return ApiResponse<BatchSignSuccessData>.Fail("Không thể truy xuất Private Key (RSA/ECDSA) từ thiết bị USB Token.");
                }

                var signatureBuffer = new List<BatchItemSignature>(validatedHashes.Count);

                try
                {
                    // Duyệt tuần tự qua cùng 1 crypto session để chỉ nhập PIN 1 lần
                    for (int i = 0; i < validatedHashes.Count; i++)
                    {
                        var (id, hashBytes) = validatedHashes[i];
                        byte[]? sigBytes = null;

                        try
                        {
                            if (rsa != null)
                            {
                                sigBytes = rsa.SignHash(hashBytes, hashAlgo, RSASignaturePadding.Pkcs1);
                            }
                            else
                            {
                                sigBytes = ecdsa!.SignHash(hashBytes);
                            }

                            signatureBuffer.Add(new BatchItemSignature
                            {
                                Id = id,
                                SignatureBase64 = Convert.ToBase64String(sigBytes)
                            });
                        }
                        catch (CryptographicException ex)
                        {
                            _logger.LogError(ex, "Lỗi mã hóa tại tài liệu {Id} trong mẻ ký batch", id);
                            signatureBuffer.Clear(); // Hủy bỏ toàn bộ buffer chữ ký

                            string errorDetail = MapCryptographicError(ex);
                            return ApiResponse<BatchSignSuccessData>.Fail(
                                $"Ký batch thất bại tại tài liệu '{id}': {errorDetail}. Toàn bộ mẻ ký đã bị hủy bỏ."
                            );
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Lỗi không xác định tại tài liệu {Id} trong mẻ ký batch", id);
                            signatureBuffer.Clear(); // Hủy bỏ toàn bộ buffer chữ ký

                            return ApiResponse<BatchSignSuccessData>.Fail(
                                $"Ký batch thất bại tại tài liệu '{id}': {ex.Message}. Toàn bộ mẻ ký đã bị hủy bỏ."
                            );
                        }
                        finally
                        {
                            // Tiêu hủy byte chữ ký nhị phân trên RAM ngay sau khi đã encode sang Base64
                            if (sigBytes != null)
                            {
                                CryptographicOperations.ZeroMemory(sigBytes);
                            }
                        }
                    }

                    // === GIAI ĐOẠN 3: HOÀN TẤT KHI 100% THÀNH CÔNG ===
                    var successData = new BatchSignSuccessData
                    {
                        TotalSigned = signatureBuffer.Count,
                        Thumbprint = cert.Thumbprint ?? request.Thumbprint,
                        SignerSubject = cert.Subject,
                        Algorithm = rsa != null ? $"{hashAlgo.Name}withRSA" : $"{hashAlgo.Name}withECDSA",
                        SignedAt = DateTime.UtcNow,
                        Signatures = signatureBuffer
                    };

                    return ApiResponse<BatchSignSuccessData>.Ok(
                        successData,
                        $"Ký batch thành công toàn bộ {successData.TotalSigned}/{successData.TotalSigned} tài liệu."
                    );
                }
                finally
                {
                    // Nếu không đạt 100% thành công, xóa sạch bộ đệm
                    if (signatureBuffer.Count < validatedHashes.Count)
                    {
                        signatureBuffer.Clear();
                    }
                }
            });

            return response;
        }
        finally
        {
            // BẢO MẬT & DỌN DẸP RAM: Tiêu hủy toàn bộ byte hash trong bộ nhớ
            foreach (var item in validatedHashes)
            {
                if (item.HashBytes != null)
                {
                    CryptographicOperations.ZeroMemory(item.HashBytes);
                }
            }
            validatedHashes.Clear();

            // Nếu mẻ ký có từ 5 item trở lên, kích hoạt thu hồi bộ nhớ tối ưu cho ứng dụng Tray ngầm
            if (request.Items.Count >= 5)
            {
                GC.Collect(2, GCCollectionMode.Optimized, blocking: false);
            }
        }
    }

    public async Task<ApiResponse<BatchSignSuccessData>> SignBatchDataAtomicAsync(SignBatchDataRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Thumbprint))
        {
            return ApiResponse<BatchSignSuccessData>.Fail("Thumbprint không được để trống.");
        }

        if (request.Items == null || request.Items.Count == 0)
        {
            return ApiResponse<BatchSignSuccessData>.Fail("Danh sách tài liệu cần ký không được để trống.");
        }

        var duplicateIds = request.Items
            .GroupBy(x => x.Id)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicateIds.Count > 0)
        {
            return ApiResponse<BatchSignSuccessData>.Fail($"Phát hiện trùng lặp Id tài liệu trong mẻ ký: {string.Join(", ", duplicateIds)}.");
        }

        var hashAlgo = ParseHashAlgorithm(request.HashAlgorithm);
        var hashItems = new List<BatchHashItem>(request.Items.Count);

        try
        {
            foreach (var item in request.Items)
            {
                if (string.IsNullOrWhiteSpace(item.Id))
                {
                    return ApiResponse<BatchSignSuccessData>.Fail("Có tài liệu trong danh sách bị thiếu Id định danh.");
                }

                if (string.IsNullOrWhiteSpace(item.DataBase64))
                {
                    return ApiResponse<BatchSignSuccessData>.Fail($"Tài liệu '{item.Id}' bị thiếu dữ liệu DataBase64.");
                }

                byte[]? rawBytes = null;
                byte[]? hashBytes = null;

                try
                {
                    try
                    {
                        rawBytes = Convert.FromBase64String(item.DataBase64);
                    }
                    catch (FormatException)
                    {
                        return ApiResponse<BatchSignSuccessData>.Fail($"Tài liệu '{item.Id}' có DataBase64 không đúng định dạng Base64.");
                    }

                    hashBytes = ComputeHash(rawBytes, hashAlgo);
                    hashItems.Add(new BatchHashItem
                    {
                        Id = item.Id,
                        HashBase64 = Convert.ToBase64String(hashBytes)
                    });
                }
                finally
                {
                    // Tiêu hủy byte thô và byte hash trung gian ngay sau khi tính toán
                    if (rawBytes != null)
                    {
                        CryptographicOperations.ZeroMemory(rawBytes);
                    }
                    if (hashBytes != null)
                    {
                        CryptographicOperations.ZeroMemory(hashBytes);
                    }
                }
            }

            var batchHashRequest = new SignBatchHashRequest
            {
                Thumbprint = request.Thumbprint,
                HashAlgorithm = request.HashAlgorithm,
                Items = hashItems
            };

            return await SignBatchHashAtomicAsync(batchHashRequest);
        }
        finally
        {
            hashItems.Clear();
        }
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
