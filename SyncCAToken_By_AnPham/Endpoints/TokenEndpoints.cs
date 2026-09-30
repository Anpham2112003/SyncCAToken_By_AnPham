using Microsoft.AspNetCore.Mvc;
using SyncCAToken_By_AnPham.Models;
using SyncCAToken_By_AnPham.Services;

namespace SyncCAToken_By_AnPham.Endpoints;

public static class TokenEndpoints
{
    public static IEndpointRouteBuilder MapTokenEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        // 1. Kiểm tra trạng thái Adapter và Token
        api.MapGet("/token/status", (ICertificateService certService) =>
        {
            var status = certService.GetStatus();
            return Results.Ok(ApiResponse<TokenStatusDto>.Ok(status, "Lấy trạng thái thành công."));
        })
        .WithName("GetTokenStatus")
        .WithSummary("Kiểm tra trạng thái kết nối của Adapter và phát hiện USB Token")
        .WithTags("Token Status");

        // 2. Lấy danh sách chứng thư số từ USB Token
        api.MapGet("/certificates", (
            ICertificateService certService,
            [FromQuery] bool validOnly = true,
            [FromQuery] bool hardwareOnly = false) =>
        {
            var certs = certService.GetCertificates(validOnly, hardwareOnly);
            return Results.Ok(ApiResponse<IEnumerable<CertificateDto>>.Ok(certs, "Lấy danh sách chứng thư số thành công."));
        })
        .WithName("GetCertificates")
        .WithSummary("Lấy danh sách chứng thư số từ Windows Certificate Store và USB Token")
        .WithTags("Certificates");

        // 3. Lấy chi tiết một chứng thư số theo Thumbprint
        api.MapGet("/certificates/{thumbprint}", (string thumbprint, ICertificateService certService) =>
        {
            var cert = certService.GetCertificateDetail(thumbprint);
            if (cert == null)
            {
                return Results.NotFound(ApiResponse<CertificateDto>.Fail($"Không tìm thấy chứng thư số với Thumbprint '{thumbprint}'."));
            }

            return Results.Ok(ApiResponse<CertificateDto>.Ok(cert, "Lấy thông tin chứng thư số thành công."));
        })
        .WithName("GetCertificateByThumbprint")
        .WithSummary("Lấy thông tin chi tiết của một chứng thư số theo Thumbprint")
        .WithTags("Certificates");

        // 4. Ký dữ liệu băm (Hash Base64) - Chuẩn tối ưu cho Web App
        api.MapPost("/sign/hash", async ([FromBody] SignHashRequest request, ISignatureService signService) =>
        {
            if (string.IsNullOrWhiteSpace(request.Thumbprint) || string.IsNullOrWhiteSpace(request.HashBase64))
            {
                return Results.BadRequest(ApiResponse<SignResult>.Fail("Thumbprint và HashBase64 là bắt buộc."));
            }

            var result = await signService.SignHashAsync(request);
            if (!result.Success)
            {
                return Results.BadRequest(ApiResponse<SignResult>.Fail(result.Error ?? "Ký số thất bại."));
            }

            return Results.Ok(ApiResponse<SignResult>.Ok(result, "Ký số thành công."));
        })
        .WithName("SignHash")
        .WithSummary("Ký chuỗi băm (Hash Base64) bằng USB Token")
        .WithTags("Digital Signature");

        // 5. Ký dữ liệu thô (Raw Data Base64)
        api.MapPost("/sign/data", async ([FromBody] SignDataRequest request, ISignatureService signService) =>
        {
            if (string.IsNullOrWhiteSpace(request.Thumbprint) || string.IsNullOrWhiteSpace(request.DataBase64))
            {
                return Results.BadRequest(ApiResponse<SignResult>.Fail("Thumbprint và DataBase64 là bắt buộc."));
            }

            var result = await signService.SignDataAsync(request);
            if (!result.Success)
            {
                return Results.BadRequest(ApiResponse<SignResult>.Fail(result.Error ?? "Ký số dữ liệu thất bại."));
            }

            return Results.Ok(ApiResponse<SignResult>.Ok(result, "Ký số dữ liệu thành công."));
        })
        .WithName("SignData")
        .WithSummary("Ký chuỗi dữ liệu thô (Data Base64) bằng USB Token")
        .WithTags("Digital Signature");

        return app;
    }
}
