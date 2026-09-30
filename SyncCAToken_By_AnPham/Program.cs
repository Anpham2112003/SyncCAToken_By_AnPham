using SyncCAToken_By_AnPham.Endpoints;
using SyncCAToken_By_AnPham.Services;

var builder = WebApplication.CreateBuilder(args);

// 1. Cấu hình CORS để web frontend (React, Vue, Angular, ASP.NET Web...) gọi được Local API
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

builder.Services.AddOpenApi();


// 2. Đăng ký Services phục vụ chứng thư số và ký số
builder.Services.AddSingleton<ICertificateService, CertificateService>();
builder.Services.AddScoped<ISignatureService, SignatureService>();

// 3. Cấu hình Swagger & OpenAPI
builder.Services.AddEndpointsApiExplorer();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseCors("AllowAll");

// Kích hoạt OpenAPI JSON và Swagger UI
app.MapOpenApi();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/openapi/v1.json", "SyncCAToken API v1");
    options.RoutePrefix = "swagger";
});

app.UseDefaultFiles();
app.UseStaticFiles();

// Endpoint trang chủ giới thiệu và kiểm tra adapter
app.MapGet("/", () => Results.Ok(new
{
    App = "SyncCAToken USB Adapter API",
    Version = "1.0.0",
    Status = "Running",
    Documentation = "/openapi/v1.json",
    Endpoints = new[]
    {
        "GET /api/token/status",
        "GET /api/certificates",
        "GET /api/certificates/{thumbprint}",
        "POST /api/sign/hash",
        "POST /api/sign/data"
    }
}))
.WithName("RootInfo")
.ExcludeFromDescription();

// Đăng ký các Minimal API Endpoints
app.MapTokenEndpoints();

app.Run();
