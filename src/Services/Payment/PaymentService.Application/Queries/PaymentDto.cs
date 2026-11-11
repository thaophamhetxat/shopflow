// File này chứa DTO đọc Payment của PaymentService Application.
// Tạo ở Day 48 để API trả kết quả Charge/Refund mà không làm lộ aggregate Domain.
// Gateway Day 49 và Saga Day 52 có thể dùng hợp đồng này khi xử lý trạng thái thanh toán.

namespace PaymentService.Application.Queries;

/// <summary>
/// Dữ liệu thanh toán trả về cho API; Status là tên của PaymentStatus.
/// </summary>
/// <param name="PaymentId">Mã định danh khoản thanh toán.</param>
/// <param name="OrderId">Mã đơn hàng liên quan.</param>
/// <param name="Amount">Số tiền đã yêu cầu thu.</param>
/// <param name="Currency">Mã tiền tệ.</param>
/// <param name="Status">Tên trạng thái thanh toán.</param>
/// <param name="FailureReason">Lý do từ chối nếu gateway báo thất bại.</param>
public sealed record PaymentDto(
    Guid PaymentId,
    Guid OrderId,
    decimal Amount,
    string Currency,
    string Status,
    string? FailureReason);
