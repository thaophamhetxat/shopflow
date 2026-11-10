// File này chứa exception dùng để biểu diễn lỗi nghiệp vụ của Order trong Domain của OrderService.
// Tạo ở Day 40 để phân biệt lỗi doanh nghiệp với lỗi kỹ thuật và chuẩn bị cho Day 47 map sang 404/409/422.
// Domain này giữ các quy tắc toàn vẹn của Order, không để lỗi nghiệp vụ bị giấu dưới InvalidOperationException hay ArgumentException.

namespace OrderService.Domain.Exceptions;

/// <summary>
/// Ngoại lệ dùng để báo lỗi nghiệp vụ của aggregate Order, phân biệt rõ với lỗi kỹ thuật hoặc lỗi dữ liệu hệ thống.
/// Bất biến: mọi hợp đồng nghiệp vụ không được bỏ qua, vì vậy tất cả vi phạm invariant của Order đều ném lớp này để Day 47 có thể map đúng trạng thái lỗi.
/// Ví dụ: xác nhận đơn rỗng, hủy đơn đã hủy, thêm hàng sau khi đã xác nhận đều ném OrderDomainException.
/// </summary>
public sealed class OrderDomainException : Exception
{
    /// <summary>
    /// Tạo exception nghiệp vụ với thông điệp mô tả rõ rule bị vi phạm.
    /// </summary>
    /// <param name="message">Mô tả lỗi nghiệp vụ theo ngữ cảnh của Order.</param>
    public OrderDomainException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Tạo exception nghiệp vụ kèm nguyên nhân kỹ thuật nếu cần trace sâu hơn cho debug.
    /// </summary>
    /// <param name="message">Mô tả lỗi nghiệp vụ.</param>
    /// <param name="innerException">Ngoại lệ kỹ thuật gốc đã xuất hiện.</param>
    public OrderDomainException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
