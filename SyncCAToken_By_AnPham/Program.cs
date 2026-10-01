using System.Diagnostics;
using SyncCAToken_By_AnPham.Endpoints;
using SyncCAToken_By_AnPham.Services;

// 1. Kiểm tra Single Instance (chỉ cho phép chạy duy nhất 1 ứng dụng)
const string mutexName = "Global\\SyncCAToken_By_AnPham_SingleInstance_Mutex";
using var mutex = new Mutex(true, mutexName, out bool isNewInstance);
if (!isNewInstance)
{
    MessageBox.Show(
        "SyncCAToken USB Adapter đang chạy ngầm trên máy tính của bạn.\n" +
        "Vui lòng kiểm tra biểu tượng chiếc khiên ở khay hệ thống (góc dưới bên phải màn hình, cạnh đồng hồ).",
        "SyncCAToken USB Adapter",
        MessageBoxButtons.OK,
        MessageBoxIcon.Information);
    return;
}

// 2. Khởi tạo Kestrel Web API
var builder = WebApplication.CreateBuilder(args);

// Cấu hình CORS để web frontend gọi được Local API
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.SetIsOriginAllowed(_ => true)
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

builder.Services.AddOpenApi();

// Đăng ký Services phục vụ chứng thư số và ký số
builder.Services.AddSingleton<ICertificateService, CertificateService>();
builder.Services.AddScoped<ISignatureService, SignatureService>();

// Cấu hình Swagger & OpenAPI
builder.Services.AddEndpointsApiExplorer();

var app = builder.Build();

// Middleware hỗ trợ Private Network Access (PNA) cho Chrome / Edge
app.Use(async (context, next) =>
{
    context.Response.Headers["Access-Control-Allow-Private-Network"] = "true";

    if (HttpMethods.IsOptions(context.Request.Method))
    {
        var origin = context.Request.Headers.Origin.ToString();
        context.Response.Headers.AccessControlAllowOrigin = string.IsNullOrEmpty(origin) ? "*" : origin;
        context.Response.Headers.AccessControlAllowMethods = "GET, POST, PUT, DELETE, OPTIONS";
        context.Response.Headers.AccessControlAllowHeaders = "*";
        context.Response.Headers["Access-Control-Allow-Private-Network"] = "true";
        context.Response.StatusCode = StatusCodes.Status204NoContent;
        return;
    }

    await next();
});

app.UseCors("AllowAll");

app.MapOpenApi();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/openapi/v1.json", "SyncCAToken API v1");
    options.RoutePrefix = "swagger";
});

app.UseDefaultFiles();
app.UseStaticFiles();

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

app.MapTokenEndpoints();

// Khởi chạy Kestrel Web Server ở luồng ngầm
var webAppTask = app.RunAsync();

// 3. Khởi tạo khay hệ thống (System Tray Icon) trên luồng STA
var trayThread = new Thread(() =>
{
    Application.EnableVisualStyles();
    Application.SetCompatibleTextRenderingDefault(false);

    using var notifyIcon = new NotifyIcon
    {
        Icon = SystemIcons.Shield,
        Text = "SyncCAToken USB Adapter (Đang chạy)",
        Visible = true
    };

    var contextMenu = new ContextMenuStrip();

    var titleItem = new ToolStripMenuItem("SyncCAToken USB Adapter - Port 7054")
    {
        Enabled = false
    };
    contextMenu.Items.Add(titleItem);

    contextMenu.Items.Add(new ToolStripSeparator());

    var statusItem = new ToolStripMenuItem("Mở trang kiểm tra (Swagger)", null, (_, _) =>
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "https://localhost:7054/swagger",
            UseShellExecute = true
        });
    });
    contextMenu.Items.Add(statusItem);

    var certItem = new ToolStripMenuItem("Cài đặt lại chứng chỉ bảo mật", null, (_, _) =>
    {
        string batPath = Path.Combine(AppContext.BaseDirectory, "CaiDatChungChi.bat");
        if (File.Exists(batPath))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = batPath,
                UseShellExecute = true,
                Verb = "runas"
            });
        }
    });
    contextMenu.Items.Add(certItem);

    contextMenu.Items.Add(new ToolStripSeparator());

    var exitItem = new ToolStripMenuItem("Thoát ứng dụng", null, async (_, _) =>
    {
        notifyIcon.Visible = false;
        try
        {
            await app.StopAsync();
        }
        catch { }
        Application.Exit();
        Environment.Exit(0);
    });
    contextMenu.Items.Add(exitItem);

    notifyIcon.ContextMenuStrip = contextMenu;

    // Nhấp đúp vào icon -> mở Swagger UI
    notifyIcon.DoubleClick += (_, _) =>
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "https://localhost:7054/swagger",
            UseShellExecute = true
        });
    };

    // Hiển thị bóng thông báo khi khởi động
    notifyIcon.ShowBalloonTip(
        3000,
        "SyncCAToken USB Adapter",
        "Ứng dụng đang chạy ngầm trên cổng https://localhost:7054 sẵn sàng phục vụ ký số.",
        ToolTipIcon.Info);

    Application.Run();
});

trayThread.SetApartmentState(ApartmentState.STA);
trayThread.IsBackground = false;
trayThread.Start();

await webAppTask;