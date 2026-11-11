// File này định nghĩa các trạng thái vòng đời Payment của PaymentService Domain.
// Tạo ở Day 48 để Charge/Refund chỉ chuyển trạng thái theo một tập giá trị rõ ràng.
// Gateway Day 49 và Saga Day 52 sẽ dựa vào các trạng thái này khi bù trừ thanh toán.

namespace PaymentService.Domain.Enums;

/// <summary>
/// Trạng thái của thanh toán từ lúc chờ xử lý đến khi hoàn tiền.
/// </summary>
public enum PaymentStatus
{
    /// <summary>Thanh toán vừa tạo, chưa có kết quả từ gateway.</summary>
    Pending,

    /// <summary>Gateway đã chấp nhận thu tiền.</summary>
    Succeeded,

    /// <summary>Gateway từ chối thanh toán.</summary>
    Failed,

    /// <summary>Khoản thanh toán thành công đã được hoàn tiền.</summary>
    Refunded
}
