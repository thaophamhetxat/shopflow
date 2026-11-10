// File này tạo Worker minimal cho NotificationService để project Day 38 build được.
// Worker này là BackgroundService, không phải API, vì Notification tập trung xử lý message nền.
// Chúng ta chỉ giữ shell khởi chạy và không triển khai bus/message logic ở Day 38.
// Day 39+ sẽ mở rộng Worker bằng các job tương tác RabbitMQ hoặc xử lý notification thực tế.
// Thêm namespace nền tảng cho hosting, dependency injection và worker để BackgroundService khởi động đúng.
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NotificationService.Worker;

// Tạo builder: nơi đăng ký services và hosted background task trước khi app chạy.
var builder = Host.CreateApplicationBuilder(args);
// Đăng ký NotificationWorker: service nền này sẽ chạy trong BackgroundService và xử lý công việc dài hạn.
builder.Services.AddHostedService<NotificationWorker>();
// Build host: host chính của worker sẵn sàng chạy với các services đã đăng ký.
var host = builder.Build();
// Chạy host: worker bắt đầu vòng đời nền và lặp theo lifecycle server.
host.Run();