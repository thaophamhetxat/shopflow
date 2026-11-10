// File này chứa enum trạng thái của Order trong Domain của OrderService.
// Tạo ở Day 39 để làm nền cho Aggregate Order ở Day 40.
// Domain này định nghĩa các trạng thái nghiệp vụ chuẩn mà Order sẽ dùng.

namespace OrderService.Domain.Enums;

/// <summary>
/// Trạng thái của một đơn hàng trong nghiệp vụ Place, Confirm, Cancel và Fulfill.
/// Bất biến: enum này là dữ liệu cố định, không đổi sau khi phát hành — chỉ có trạng thái chuyển dần theo vòng đời đơn hàng.
/// Bất biến giữ: chỉ có đúng bốn giá trị được phép: Pending, Confirmed, Cancelled và Fulfilled; không thêm Created, Paid hay Shipped.
/// Ví dụ hợp lệ: Pending, Confirmed. Ví dụ không hợp lệ: Created vì đây là trạng thái không nằm trong miền đã chốt.
/// </summary>
public enum OrderStatus
{
    Pending = 0,
    Confirmed = 1,
    Cancelled = 2,
    Fulfilled = 3
}
