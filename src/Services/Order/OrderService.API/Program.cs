// File này tạo API minimal cho OrderService để project build được ở Day 38.
// Đây là khung API đầu tiên trong solution này, chịu trách nhiệm chỉ trả /health và không chứa nghiệp vụ.
// Layer API này dự kiến sẽ nhận request phục vụ luồng Place/Confirm/Cancel/Fulfill ở Day 47 trở đi.
// Mục tiêu ở Day 38 là đảm bảo service khởi động và đáp ứng sự sống khỏe cho kiểm tra thủ công.
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// Tạo builder: nơi đăng ký dependency injection trước khi app khởi chạy.
var builder = WebApplication.CreateBuilder(args);
// Đăng ký health check: dùng để /health báo service còn sống và sẵn sàng nhận request.
builder.Services.AddHealthChecks();
// Build app từ builder: sau bước này không đăng ký service mới được nữa.
var app = builder.Build();
// Map endpoint GET /health: trả OK để kiểm tra service còn sống và gắn probe cho sau này.
app.MapHealthChecks("/health");
// Chạy app: lắng nghe request trên port được cấu hình trong ASP.NET Core.
app.Run();