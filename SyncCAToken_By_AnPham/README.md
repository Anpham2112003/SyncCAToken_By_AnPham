# SyncCAToken USB Adapter API

Adapter ký số chạy trên máy Windows đang cắm USB Token. Web client gọi API trên loopback; private key vẫn nằm trong Windows Certificate Store hoặc trên Token và không được gửi về server.

## Chạy adapter

Yêu cầu .NET 10 SDK, driver/phần mềm quản lý của nhà cung cấp Token và chứng thư có private key trong **Current User > Personal (My)**.

Từ thư mục chứa file dự án, chạy:

~~~powershell
dotnet run --launch-profile https
~~~

Profile **https** mở hai địa chỉ:

- HTTPS: https://localhost:7054
- HTTP dùng khi phát triển: http://localhost:5034

Nếu Windows hoặc trình duyệt chưa tin cậy chứng thư HTTPS phát triển, có thể đăng ký chứng thư phát triển của .NET bằng lệnh **dotnet dev-certs https --trust**, sau đó khởi động lại adapter.

## Tài liệu tương tác

- Swagger UI: https://localhost:7054/swagger/index.html
- OpenAPI JSON: https://localhost:7054/openapi/v1.json
- Trạng thái gốc: https://localhost:7054/

Swagger UI và API cần adapter đang chạy trên chính máy người ký.

## Quy ước chung

Các API trả JSON theo phong cách camelCase. Response bọc trong cấu trúc chung:

~~~json
{
  "success": true,
  "message": "Thông báo kết quả.",
  "data": {},
  "timestamp": "2026-09-30T04:23:31.994996Z"
}
~~~

**timestamp** và **signedAt** dùng UTC. Khi thất bại, **success** là false, **message** chứa lý do và **data** có thể là null.

## Endpoints

### GET /api/token/status

Kiểm tra adapter và đếm chứng thư trong Windows Store hiện tại.

~~~powershell
Invoke-RestMethod "https://localhost:7054/api/token/status"
~~~

Các trường trong **data**:

| Trường | Ý nghĩa |
| --- | --- |
| **isRunning** | Luôn là true khi endpoint trả về; xác nhận tiến trình adapter đang phản hồi. |
| **machineName** | Tên máy đang chạy adapter. |
| **osVersion** | Phiên bản hệ điều hành. |
| **activeCertificatesCount** | Số chứng thư có private key và còn hạn. |
| **hardwareTokensCount** | Số chứng thư có private key được nhận diện là phần cứng theo thông tin CA/provider. Đây là phép nhận diện heuristic, không đảm bảo USB đang cắm tại thời điểm gọi. |
| **serverTime** | Thời gian UTC của adapter. |

### GET /api/certificates

Lấy chứng thư có private key từ **Current User > Personal (My)**.

~~~powershell
Invoke-RestMethod "https://localhost:7054/api/certificates?validOnly=true&hardwareOnly=true"
~~~

Query parameters:

| Tên | Mặc định | Ý nghĩa |
| --- | --- | --- |
| **validOnly** | true | Chỉ trả chứng thư còn hạn. Đặt false để gồm cả chứng thư hết hạn. |
| **hardwareOnly** | false | Chỉ trả chứng thư được nhận diện là USB Token/phần cứng. |

Danh sách không có **certificateBase64**; trường này chỉ được trả ở API chi tiết bên dưới.

### GET /api/certificates/{thumbprint}

Lấy chi tiết chứng thư theo thumbprint. Adapter chuẩn hóa thumbprint trước khi tìm trong Windows Store.

~~~powershell
$thumbprint = "<THUMBPRINT>"
Invoke-RestMethod "https://localhost:7054/api/certificates/$thumbprint"
~~~

**data** có các trường **thumbprint**, **serialNumber**, **subject**, **commonName**, **organization**, **taxCode**, **issuer**, **issuerName**, **validFrom**, **validTo**, **isValid**, **hasPrivateKey**, **isHardwareToken**, **keyAlgorithm**, **keySize** và **certificateBase64**.

**certificateBase64** là chứng thư công khai dạng DER được Base64 hóa; nó không chứa private key. API trả HTTP 404 nếu không tìm thấy thumbprint.

### POST /api/sign/hash

Ký một hash đã được client tính trước. **hashBase64** phải là Base64 của byte hash nhị phân, không phải dữ liệu gốc và cũng không phải chuỗi hex.

~~~json
{
  "thumbprint": "<THUMBPRINT>",
  "hashBase64": "<BASE64_CUA_HASH>",
  "hashAlgorithm": "SHA256"
}
~~~

Thuật toán hỗ trợ **SHA1**, **SHA256**, **SHA384** và **SHA512**; mặc định là **SHA256**. Độ dài hash phải khớp thuật toán: lần lượt 20, 32, 48 hoặc 64 byte. Giá trị thuật toán không nhận diện được hiện dùng mặc định SHA256.

### POST /api/sign/data

Ký dữ liệu gốc. **dataBase64** là Base64 của các byte dữ liệu; adapter tự băm dữ liệu theo **hashAlgorithm** rồi ký hash đó.

~~~powershell
$body = @{
  thumbprint = "<THUMBPRINT>"
  dataBase64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes("Dữ liệu cần ký"))
  hashAlgorithm = "SHA256"
} | ConvertTo-Json

$invokeParams = @{
  Method = "Post"
  Uri = "https://localhost:7054/api/sign/data"
  ContentType = "application/json"
  Body = $body
}
Invoke-RestMethod @invokeParams
~~~

**/api/sign/hash** và **/api/sign/data** trả cùng kiểu **data** khi thành công:

~~~json
{
  "success": true,
  "signatureBase64": "<BASE64_CHU_KY_THO>",
  "thumbprint": "<THUMBPRINT>",
  "signerSubject": "<SUBJECT_CHUNG_THU>",
  "algorithm": "SHA256withRSA",
  "signedAt": "2026-09-30T04:23:31.994996Z",
  "error": null
}
~~~

**signatureBase64** là chữ ký mật mã thô. Adapter không tạo CMS/PKCS#7 hay đóng gói PDF. RSA dùng PKCS#1 v1.5; ECDSA được hỗ trợ nếu chứng thư dùng khóa ECDSA. Lần ký có thể làm Windows hoặc phần mềm Token hiện hộp thoại nhập PIN.

Lỗi nhập liệu hoặc lỗi ký thường trả HTTP 400 với **success: false** và lý do trong **message**. Ví dụ: Base64 sai định dạng, hash sai độ dài, không tìm thấy chứng thư, chứng thư không có private key, Token chưa sẵn sàng, PIN sai hoặc thao tác PIN bị hủy.

### POST /api/sign/batch-hash

Ký chuỗi băm hàng loạt với nguyên tắc **Tất cả hoặc Không có gì (All-or-Nothing)**:
- **Chỉ nhập PIN 1 lần**: Giữ crypto context trong suốt mẻ ký.
- **Thẩm định trước (Pre-validation)**: Kiểm tra định dạng Base64, độ dài hash và tính duy nhất của `id` trong RAM trước khi chạm vào Token; nếu có lỗi ở bất kỳ item nào, adapter từ chối ngay mà không hiện popup PIN.
- **Nguyên tử (Atomic Rollback)**: Nếu xảy ra bất kỳ lỗi nào trong quá trình ký (người dùng hủy PIN, sai PIN, rút token), adapter lập tức **hủy toàn bộ chữ ký đã tạo**, dừng khẩn cấp và trả HTTP 400 (không trả về chữ ký dở dang).

Body mẫu:

~~~json
{
  "thumbprint": "<THUMBPRINT>",
  "hashAlgorithm": "SHA256",
  "items": [
    {
      "id": "INV-001",
      "hashBase64": "<BASE64_HASH_1>"
    },
    {
      "id": "INV-002",
      "hashBase64": "<BASE64_HASH_2>"
    }
  ]
}
~~~

Response thành công (HTTP 200) - chỉ trả về khi 100% item thành công:

~~~json
{
  "success": true,
  "message": "Ký batch thành công toàn bộ 2/2 tài liệu.",
  "data": {
    "totalSigned": 2,
    "thumbprint": "<THUMBPRINT>",
    "signerSubject": "<SUBJECT>",
    "algorithm": "SHA256withRSA",
    "signedAt": "2026-10-05T02:30:00Z",
    "signatures": [
      {
        "id": "INV-001",
        "signatureBase64": "<BASE64_CHU_KY_1>"
      },
      {
        "id": "INV-002",
        "signatureBase64": "<BASE64_CHU_KY_2>"
      }
    ]
  },
  "timestamp": "2026-10-05T02:30:00Z"
}
~~~

### POST /api/sign/batch-data

Ký dữ liệu thô hàng loạt với cùng nguyên tắc **All-or-Nothing**. Adapter tự tính hash cho từng item trong bộ nhớ trước, thẩm định toàn bộ rồi ký tuần tự qua 1 phiên Token:

~~~json
{
  "thumbprint": "<THUMBPRINT>",
  "hashAlgorithm": "SHA256",
  "items": [
    {
      "id": "DOC-001",
      "dataBase64": "<BASE64_RAW_DATA_1>"
    },
    {
      "id": "DOC-002",
      "dataBase64": "<BASE64_RAW_DATA_2>"
    }
  ]
}
~~~

## Luồng ký PDF hai pha của SmartLab

1. Client gọi **GET /api/certificates?validOnly=true&hardwareOnly=true** để chọn chứng thư còn hạn có private key.
2. Client gọi **GET /api/certificates/{thumbprint}** để lấy **certificateBase64** gửi cho SmartLab backend khi chuẩn bị PDF.
3. Backend chuẩn bị PDF và trả **signingDataBase64** cùng **hashAlgorithm**.
4. Client gọi **POST /api/sign/data**, truyền **signingDataBase64** vào **dataBase64**. Đây là dữ liệu cần ký, không phải hash có sẵn.
5. Client gửi **signatureBase64** nhận được về backend để hoàn tất chữ ký PDF.

Chỉ dùng **/api/sign/hash** khi bên gọi đã có sẵn byte hash đúng thuật toán và độ dài.

## Phạm vi truy cập

Adapter hiện không yêu cầu xác thực và cấu hình CORS **AllowAll**. Mặc định profile chỉ mở trên **localhost**; giữ API trong phạm vi máy người ký và không cấu hình để lắng nghe trên mạng nội bộ hoặc Internet.