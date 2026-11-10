// File này tạo API minimal cho InventoryService để project build được ở Day 38.
// Layer API này chỉ khởi tạo app và health check, không chứa nghiệp vụ Reserve/Release.
// Day 39+ mới mô hình domain và transaction thực tế, còn ở đây chỉ giữ shell để build và test startup.
// Chúng ta giới hạn đúng Day 38 và không viết nghiệp vụ cùng lúc để giữ bounded context rõ ràng.
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// Tạo builder: nơi đăng ký dependency injection trước khi app khởi chạy.
var builder = WebApplication.CreateBuilder(args);
// Đăng ký health check: để /health xác nhận InventoryService còn sống và sẵn sàng lắng nghe.
builder.Services.AddHealthChecks();
// Build app từ builder: từ đây service có thể map endpoint nhưng không thêm DI nữa.
var app = builder.Build();
// Map endpoint GET /health: trả 200 khi InventoryService khỏe để hoạt động kiểm tra thủ công.
app.MapHealthChecks("/health");
// Chạy app: khởi động HTTP server để service nhận request ở port được cấu hình sẵn.
app.Run();