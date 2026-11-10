// File này chứa exception nghiệp vụ cho trường hợp không tìm thấy đơn hàng khi hủy hoặc query.
// Tạo ở Day 45 để caller nhận lỗi rõ ràng thay vì chỉ đọc null hoặc ném generic exception.
// Application layer dùng exception này để báo hiệu business rule "đơn này không tồn tại" rõ ràng hơn.

namespace OrderService.Application;

/// <summary>
/// Exception cho trường hợp không tìm thấy đơn hàng trong repository khi thực hiện command hoặc query cần Order hiện có.
/// </summary>
public sealed class OrderNotFoundException : Exception
{
    /// <summary>
    /// Khởi tạo exception với OrderId đã được yêu cầu nhưng không tồn tại trong hệ thống.
    /// </summary>
    /// <param name="orderId">OrderId không tìm thấy.</param>
    public OrderNotFoundException(Guid orderId)
        : base($"Không tìm thấy đơn hàng với OrderId '{orderId}'.")
    {
        OrderId = orderId;
    }

    /// <summary>
    /// OrderId đã được yêu cầu nhưng không tồn tại.
    /// </summary>
    public Guid OrderId { get; }
}
