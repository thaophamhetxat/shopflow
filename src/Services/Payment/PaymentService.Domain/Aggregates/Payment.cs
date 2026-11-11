// File này chứa aggregate Payment và các quy tắc chuyển trạng thái thanh toán.
// Tạo ở Day 48 để chỉ cho phép thành công/thất bại từ Pending và Refund từ Succeeded.
// Gateway Day 49 và Saga Day 52 sẽ dùng aggregate này trong các bước Charge/Refund.

using PaymentService.Domain.Enums;
using PaymentService.Domain.Exceptions;
using PaymentService.Domain.ValueObjects;

namespace PaymentService.Domain.Aggregates;

/// <summary>
/// Aggregate thanh toán, bảo vệ số tiền, tiền tệ và chuyển trạng thái của một khoản thu.
/// </summary>
public sealed class Payment
{
    private Payment()
    {
        Currency = string.Empty;
    }

    private Payment(PaymentId id, Guid orderId, decimal amount, string currency)
    {
        Id = id;
        OrderId = orderId;
        Amount = amount;
        Currency = currency;
        Status = PaymentStatus.Pending;
    }

    /// <summary>Mã định danh của khoản thanh toán.</summary>
    public PaymentId Id { get; private set; } = null!;

    /// <summary>Mã đơn hàng liên quan đến khoản thanh toán.</summary>
    public Guid OrderId { get; private set; }

    /// <summary>Số tiền cần thu.</summary>
    public decimal Amount { get; private set; }

    /// <summary>Mã tiền tệ gồm ba chữ cái in hoa.</summary>
    public string Currency { get; private set; }

    /// <summary>Trạng thái hiện tại của khoản thanh toán.</summary>
    public PaymentStatus Status { get; private set; }

    /// <summary>Lý do thất bại nếu gateway từ chối; null ở các trạng thái còn lại.</summary>
    public string? FailureReason { get; private set; }

    /// <summary>
    /// Tạo Payment ở trạng thái Pending sau khi xác thực order, amount và currency.
    /// </summary>
    /// <param name="id">PaymentId của khoản thanh toán.</param>
    /// <param name="orderId">OrderId không được là Guid.Empty.</param>
    /// <param name="amount">Số tiền phải lớn hơn không.</param>
    /// <param name="currency">Mã tiền tệ gồm đúng ba chữ cái in hoa.</param>
    /// <returns>Payment mới ở trạng thái Pending, chưa có lý do thất bại.</returns>
    /// <exception cref="PaymentDomainException">Ném khi dữ liệu đầu vào vi phạm quy tắc thanh toán.</exception>
    public static Payment Create(PaymentId id, Guid orderId, decimal amount, string currency)
    {
        ArgumentNullException.ThrowIfNull(id);

        if (orderId == Guid.Empty)
        {
            throw new PaymentDomainException("OrderId không được là Guid.Empty.");
        }

        if (amount <= 0)
        {
            throw new PaymentDomainException("Số tiền thanh toán phải lớn hơn 0.");
        }

        if (!IsCurrencyCode(currency))
        {
            throw new PaymentDomainException("Mã tiền tệ phải gồm đúng ba chữ cái in hoa.");
        }

        return new Payment(id, orderId, amount, currency);
    }

    /// <summary>
    /// Đánh dấu khoản thanh toán thành công nếu đang Pending.
    /// </summary>
    /// <exception cref="PaymentDomainException">Ném nếu Payment không ở trạng thái Pending.</exception>
    public void MarkSucceeded()
    {
        EnsurePending("chuyển sang thành công");
        Status = PaymentStatus.Succeeded;
        FailureReason = null;
    }

    /// <summary>
    /// Đánh dấu thanh toán thất bại và lưu lý do khi đang Pending.
    /// </summary>
    /// <param name="reason">Lý do thất bại không được để trống.</param>
    /// <exception cref="PaymentDomainException">Ném nếu Payment không Pending hoặc reason rỗng.</exception>
    public void MarkFailed(string reason)
    {
        EnsurePending("chuyển sang thất bại");
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new PaymentDomainException("Lý do thanh toán thất bại không được để trống.");
        }

        Status = PaymentStatus.Failed;
        FailureReason = reason;
    }

    /// <summary>
    /// Hoàn tiền cho khoản đã thu thành công.
    /// </summary>
    /// <exception cref="PaymentDomainException">Ném nếu Payment chưa ở trạng thái Succeeded.</exception>
    public void Refund()
    {
        // Chỉ hoàn khoản đã thu thành công để không trả tiền hai lần hoặc trả khoản chưa từng thu.
        if (Status != PaymentStatus.Succeeded)
        {
            throw new PaymentDomainException("Chỉ có thể hoàn tiền cho khoản thanh toán đã thành công.");
        }

        Status = PaymentStatus.Refunded;
    }

    /// <summary>Chặn thao tác chuyển trạng thái nếu Payment không còn Pending.</summary>
    /// <param name="operation">Mô tả thao tác đang được yêu cầu.</param>
    private void EnsurePending(string operation)
    {
        if (Status != PaymentStatus.Pending)
        {
            throw new PaymentDomainException($"Không thể {operation} khi Payment đang ở trạng thái {Status}.");
        }
    }

    /// <summary>Kiểm tra mã tiền tệ có đúng ba chữ cái ASCII in hoa hay không.</summary>
    /// <param name="currency">Mã tiền tệ cần kiểm tra.</param>
    /// <returns>True nếu mã hợp lệ; ngược lại false.</returns>
    private static bool IsCurrencyCode(string currency)
    {
        return currency is { Length: 3 } && currency.All(character => character is >= 'A' and <= 'Z');
    }
}
