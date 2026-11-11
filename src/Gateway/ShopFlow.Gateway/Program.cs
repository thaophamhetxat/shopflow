var builder = WebApplication.CreateBuilder(args);

// Dòng này đăng ký YARP ReverseProxy từ cấu hình ReverseProxy trong appsettings.json để gateway có thể forward request tới Order, Inventory và Payment.
// Day 49 không dùng transform đổi path hay kèm auth vì contract downstream đã đúng chuẩn và không có JWT nào được định nghĩa.
builder.Services.AddReverseProxy().LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// Đăng ký client riêng chỉ để probe /health downstream, timeout 2 giây mỗi service: 1 service treo không được làm /health của gateway treo theo.
// Dùng IHttpClientFactory thay vì tự tạo HttpClient trực tiếp để tránh tạo socket mới mỗi lần gọi và để timeout được quản lý tập trung ở đây.
builder.Services.AddHttpClient("GatewayHealth", client => client.Timeout = TimeSpan.FromSeconds(2));

var app = builder.Build();

// Dòng này định nghĩa endpoint live probe của gateway, riêng biệt với downstream health, để kiểm tra process gateway đang ngủ hay sống.
// Chúng ta không gọi downstream ở đây vì route live chỉ xác nhận gateway đã khởi động và coi như lộ trình của proxy chưa cần validate.
app.MapGet("/health/live", () => Results.Ok(new { status = "Live" }));

// Dòng này định nghĩa endpoint /health tổng hợp, dùng HttpClient probe từng service Order/Inventory/Payment rồi trả 200 Healthy hoặc 503 Degraded.
// YARP không tự gộp health của các cluster trong Day 49, nên phải tự viết logic này để gateway đánh giá service nào còn sống.
// Endpoint /health tổng hợp: lấy client từ factory (đã gắn timeout 2 giây) thay vì tự tạo HttpClient trực tiếp.
app.MapGet("/health", async (IHttpClientFactory httpClientFactory) =>
{
    // Client này đã có timeout 2 giây từ lúc đăng ký: downstream treo quá 2 giây thì GetAsync ném TaskCanceledException, catch bên dưới đánh dấu Unhealthy.
    var httpClient = httpClientFactory.CreateClient("GatewayHealth");
    var serviceTargets = new[]
    {
        new { Name = "orders", Url = "http://localhost:5181/health" },
        new { Name = "inventory", Url = "http://localhost:5140/health" },
        new { Name = "payments", Url = "http://localhost:5199/health" }
    };

    var checks = await Task.WhenAll(serviceTargets.Select(async service =>
    {
        try
        {
            // Dòng này gọi health endpoint của từng service trực tiếp để gateway biết service nào còn sẵn sàng xử lý request.
            var response = await httpClient.GetAsync(service.Url, HttpCompletionOption.ResponseHeadersRead);
            var payload = await response.Content.ReadAsStringAsync();
            var isHealthy = response.IsSuccessStatusCode;

            // Dòng này lưu trạng thái từng service để trả JSON rõ ràng cho caller khi one service bị hỏng.
            return new { service.Name, Status = isHealthy ? "Healthy" : "Unhealthy", Healthy = isHealthy, ResponseBody = payload };
        }
        catch (Exception)
        {
            // Dòng này đánh dấu downstream bị lỗi kết nối mà không làm gateway crash hoặc ném stack trace cho client.
            return new { service.Name, Status = "Unhealthy", Healthy = false, ResponseBody = "Connection failed" };
        }
    }));

    var hasDegradedService = checks.Any(check => !check.Healthy);

    // Dòng này trả 503 nếu có ít nhất một downstream không healthy, vì hệ thống đang bị suy giảm và không phải Healthy tuyệt đối.
    return hasDegradedService
        ? Results.Json(new { status = "Degraded", services = checks }, statusCode: StatusCodes.Status503ServiceUnavailable)
        : Results.Ok(new { status = "Healthy", services = checks });
});

// Dòng này đặt MapReverseProxy sau /health để route /health luôn được xử lý bởi gateway trước khi proxy request đi downstream.
// Nếu reverse proxy được đặt lên trước, /health có thể bị định tuyến sai ra service không có endpoint tương ứng và đánh giá sai.
app.MapReverseProxy();

app.Run();
