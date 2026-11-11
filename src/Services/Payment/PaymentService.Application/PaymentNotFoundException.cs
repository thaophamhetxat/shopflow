// File này chứa ngoại lệ Application khi Payment được yêu cầu không tồn tại.
// Tạo ở Day 48 để query/command phân biệt dữ liệu thiếu với lỗi hạ tầng.
// API Day 48 ánh xạ ngoại lệ này thành 404 Problem Details.

namespace PaymentService.Application;

/// <summary>
/// Ngoại lệ cho biết PaymentId được yêu cầu không có trong repository.
/// </summary>
public sealed class PaymentNotFoundException : Exception
{
    /// <summary>
    /// Tạo ngoại lệ với ID thanh toán không tìm thấy.
    /// </summary>
    /// <param name="paymentId">ID thanh toán được yêu cầu.</param>
    public PaymentNotFoundException(Guid paymentId)
        : base($"Không tìm thấy thanh toán với PaymentId '{paymentId}'.")
    {
        PaymentId = paymentId;
    }

    /// <summary>Mã thanh toán không được tìm thấy.</summary>
    public Guid PaymentId { get; }
}
