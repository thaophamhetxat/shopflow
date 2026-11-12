// File này tạo API minimal cho NotificationService để project build được ở Day 38.
// API này chỉ cần khởi động và trả /health, vì nghiệp vụ Notify sẽ được thêm ở Day 41+.
// Đặt ranh giới bounded context rõ ràng ở đây để service không phụ thuộc vào các context khác.
// Mục đích chính của Day 38 là setup nền tảng khung, không triển khai logic thông báo thật.
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ShopFlow.BuildingBlocks;

// Tạo builder: nơi đăng ký dependency injection trước khi app khởi chạy.
var builder = WebApplication.CreateBuilder(args);
builder.AddShopFlowSerilog("shopflow-notification");
builder.Services.AddShopFlowHealthChecks(null, null, null);
// Build app từ builder: sau khi build, dữ liệu DI đã ổn định, không cần thêm nữa.
var app = builder.Build();
app.UseShopFlowMetrics();
// Notification API chưa có dependency ngoài; health endpoint chỉ phản ánh process hiện có.
app.MapHealthChecks("/health");
app.MapGet("/health/live", () => Results.Ok(new { status = "Live" }));
// Không có dependency ngoài nên ready cũng phản ánh process đã khởi động và sẵn sàng xử lý request.
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
// Chạy app: khởi động HTTP server để API nhận request và chạy trên địa chỉ được cấu hình.
app.Run();