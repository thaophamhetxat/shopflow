// File này tạo API minimal cho PaymentService để project build được ở Day 38.
// Đây là shell API đầu tiên dành cho bounded context Payment, chưa triển khai Charge/Refund điều gì.
// Mục tiêu Day 38 là tạo ranh giới rõ ràng và giữ service có thể start lên để kiểm tra health.
// Không dùng logic thanh toán hay call đến service khác ở đây vì đây là Day 38.
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// Tạo builder: nơi đăng ký dependency injection trước khi app khởi chạy.
var builder = WebApplication.CreateBuilder(args);
// Đăng ký health check: để /health xác nhận PaymentService còn sống và sẵn sàng nhận request.
builder.Services.AddHealthChecks();
// Build app từ builder: từ đây service không thêm dependency injection khác nữa.
var app = builder.Build();
// Map endpoint GET /health: trả 200 cho kiểm tra startup và tích hợp probe ở Day 65.
app.MapHealthChecks("/health");
// Chạy app: bắt đầu lắng nghe request trên port cấu hình sẵn trong ASP.NET Core.
app.Run();