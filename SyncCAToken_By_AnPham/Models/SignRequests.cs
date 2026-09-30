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
