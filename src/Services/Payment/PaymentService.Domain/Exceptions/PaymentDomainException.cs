// File này chứa ngoại lệ cho các quy tắc nghiệp vụ của PaymentService Domain.
// Tạo ở Day 48 để trạng thái Charge/Refund sai được phân biệt với lỗi kỹ thuật.
// API Day 48 ánh xạ ngoại lệ này thành Conflict; Saga Day 52 sẽ dùng nó khi bù trừ.

namespace PaymentService.Domain.Exceptions;

/// <summary>
/// Ngoại lệ báo dữ liệu hoặc chuyển trạng thái Payment vi phạm quy tắc nghiệp vụ.
/// </summary>
public sealed class PaymentDomainException : Exception
{
    /// <summary>
    /// Tạo ngoại lệ với thông điệp mô tả quy tắc Payment bị vi phạm.
    /// </summary>
    /// <param name="message">Mô tả lỗi nghiệp vụ bằng tiếng Việt.</param>
    public PaymentDomainException(string message)
        : base(message)
    {
    }
}
