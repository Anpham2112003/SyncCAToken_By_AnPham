using System.ComponentModel.DataAnnotations;

namespace SyncCAToken_By_AnPham.Models;

public class SignHashRequest
{
    [Required(ErrorMessage = "Thumbprint là bắt buộc.")]
    public string Thumbprint { get; set; } = string.Empty;

    [Required(ErrorMessage = "Dữ liệu băm HashBase64 là bắt buộc.")]
    public string HashBase64 { get; set; } = string.Empty;

    public string HashAlgorithm { get; set; } = "SHA256";
}

public class SignDataRequest
{
    [Required(ErrorMessage = "Thumbprint là bắt buộc.")]
    public string Thumbprint { get; set; } = string.Empty;

    [Required(ErrorMessage = "Dữ liệu DataBase64 là bắt buộc.")]
    public string DataBase64 { get; set; } = string.Empty;

    public string HashAlgorithm { get; set; } = "SHA256";
}

public class BatchHashItem
{
    [Required(ErrorMessage = "Id của tài liệu là bắt buộc.")]
    public string Id { get; set; } = string.Empty;

    [Required(ErrorMessage = "Dữ liệu băm HashBase64 là bắt buộc.")]
    public string HashBase64 { get; set; } = string.Empty;
}

public class SignBatchHashRequest
{
    [Required(ErrorMessage = "Thumbprint là bắt buộc.")]
    public string Thumbprint { get; set; } = string.Empty;

    public string HashAlgorithm { get; set; } = "SHA256";

    [Required(ErrorMessage = "Danh sách tài liệu cần ký là bắt buộc.")]
    [MinLength(1, ErrorMessage = "Danh sách cần ký phải có ít nhất 1 tài liệu.")]
    public List<BatchHashItem> Items { get; set; } = [];
}

public class BatchDataItem
{
    [Required(ErrorMessage = "Id của tài liệu là bắt buộc.")]
    public string Id { get; set; } = string.Empty;

    [Required(ErrorMessage = "Dữ liệu DataBase64 là bắt buộc.")]
    public string DataBase64 { get; set; } = string.Empty;
}

public class SignBatchDataRequest
{
    [Required(ErrorMessage = "Thumbprint là bắt buộc.")]
    public string Thumbprint { get; set; } = string.Empty;

    public string HashAlgorithm { get; set; } = "SHA256";

    [Required(ErrorMessage = "Danh sách tài liệu cần ký là bắt buộc.")]
    [MinLength(1, ErrorMessage = "Danh sách cần ký phải có ít nhất 1 tài liệu.")]
    public List<BatchDataItem> Items { get; set; } = [];
}
