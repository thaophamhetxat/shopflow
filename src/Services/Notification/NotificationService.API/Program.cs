// File này tạo API minimal cho NotificationService để project build được ở Day 38.
// API này chỉ cần khởi động và trả /health, vì nghiệp vụ Notify sẽ được thêm ở Day 41+.
// Đặt ranh giới bounded context rõ ràng ở đây để service không phụ thuộc vào các context khác.
// Mục đích chính của Day 38 là setup nền tảng khung, không triển khai logic thông báo thật.
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// Tạo builder: nơi đăng ký dependency injection trước khi app khởi chạy.
var builder = WebApplication.CreateBuilder(args);
// Đăng ký health check: giúp /health báo NotificationService còn sống và sẵn sàng xử lý message.
builder.Services.AddHealthChecks();
// Build app từ builder: sau khi build, dữ liệu DI đã ổn định, không cần thêm nữa.
var app = builder.Build();
// Map endpoint GET /health: trả OK để kiểm tra service còn sống khi chạy local bằng dotnet run.
app.MapHealthChecks("/health");
// Chạy app: khởi động HTTP server để API nhận request và chạy trên địa chỉ được cấu hình.
app.Run();