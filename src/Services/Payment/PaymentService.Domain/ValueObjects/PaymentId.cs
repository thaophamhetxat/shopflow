// File này chứa Value Object định danh cho Payment trong PaymentService Domain.
// Tạo ở Day 48 để aggregate và repository không dùng Guid trần làm ID thanh toán.
// Gateway Day 49 và Saga Day 52 sẽ dùng PaymentId để tra cứu và Refund.

namespace PaymentService.Domain.ValueObjects;

/// <summary>
/// Mã định danh Payment được bọc trong Guid để phân biệt với các loại ID khác.
/// </summary>
public sealed record PaymentId(Guid Value)
{
    /// <summary>
    /// Tạo PaymentId mới cho khoản thanh toán vừa được khởi tạo.
    /// </summary>
    /// <returns>PaymentId chứa một Guid mới.</returns>
    public static PaymentId New()
    {
        return new PaymentId(Guid.NewGuid());
    }

    /// <summary>
    /// Bọc một Guid có sẵn; chấp nhận Guid.Empty để query có thể trả về không tìm thấy.
    /// </summary>
    /// <param name="value">Giá trị Guid cần bọc.</param>
    /// <returns>PaymentId chứa Guid được cung cấp.</returns>
    public static PaymentId From(Guid value)
    {
        return new PaymentId(value);
    }
}
