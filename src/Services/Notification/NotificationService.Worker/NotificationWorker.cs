// File này là worker nền tối thiểu cho NotificationService ở Day 38.
// Nó chỉ đảm bảo worker khởi động được và không chứa logic Notify thật theo nghiệp vụ.
// Khi Day 39+ bắt đầu xây dựng bus/message, class này sẽ được mở rộng thành công việc thực tế.
// Mục tiêu chính là project build được và giữ worker được phân ranh với API rõ ràng.
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace NotificationService.Worker;

public class NotificationWorker : BackgroundService
{
    // ExecuteAsync chạy nền trong vòng đời worker và giữ cho task không lỗi ở Day 38.
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Cứ để task ở trạng thái chờ đến khi worker bị dừng, không triển khai công việc thực tế.
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
}