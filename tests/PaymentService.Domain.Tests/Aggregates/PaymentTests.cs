// File này kiểm thử các invariant vòng đời Payment của PaymentService Domain.
// Tạo ở Day 48 để cung cấp Evidence cho Pending, Succeeded, Failed và điều kiện Refund.
// Các test này không cần PostgreSQL vì chỉ kiểm thử aggregate thuần.

using PaymentService.Domain.Aggregates;
using PaymentService.Domain.Enums;
using PaymentService.Domain.Exceptions;
using PaymentService.Domain.ValueObjects;

namespace PaymentService.Domain.Tests.Aggregates;

/// <summary>
/// Kiểm tra hành vi nghiệp vụ Payment theo Evidence Day 48.
/// </summary>
public sealed class PaymentTests
{
    /// <summary>
    /// Tạo Payment hợp lệ với OrderId cố định trong phạm vi test.
    /// </summary>
    /// <returns>Payment mới ở trạng thái Pending.</returns>
    private static Payment CreatePayment()
    {
        return Payment.Create(
            PaymentId.From(Guid.Parse("00000000-0000-0000-0000-000000000002")),
            Guid.Parse("00000000-0000-0000-0000-000000000003"),
            250000m,
            "VND");
    }

    /// <summary>Chứng minh Payment mới bắt đầu ở Pending theo Evidence Day 48.</summary>
    [Fact]
    public void Create_IsPending()
    {
        // Evidence Day 48: chứng minh Payment mới có trạng thái Pending và chưa có FailureReason.
        var payment = CreatePayment();

        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Null(payment.FailureReason);
    }

    /// <summary>Chứng minh Payment Pending có thể chuyển sang Succeeded theo Evidence Day 48.</summary>
    [Fact]
    public void MarkSucceeded_FromPending()
    {
        // Evidence Day 48: chứng minh Payment đang Pending chuyển sang Succeeded khi gateway chấp nhận.
        var payment = CreatePayment();

        payment.MarkSucceeded();

        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
    }

    /// <summary>Chứng minh MarkFailed lưu lý do từ chối theo Evidence Day 48.</summary>
    [Fact]
    public void MarkFailed_SetsReason()
    {
        // Evidence Day 48: chứng minh Payment lưu lý do khi gateway từ chối khoản thu.
        var payment = CreatePayment();

        payment.MarkFailed("Gateway từ chối giao dịch.");

        Assert.Equal(PaymentStatus.Failed, payment.Status);
        Assert.Equal("Gateway từ chối giao dịch.", payment.FailureReason);
    }

    /// <summary>Chứng minh Refund chuyển khoản đã thu thành công sang Refunded theo Evidence Day 48.</summary>
    [Fact]
    public void Refund_FromSucceeded()
    {
        // Evidence Day 48: chứng minh chỉ sau khi thu thành công, Refund mới chuyển trạng thái sang Refunded.
        var payment = CreatePayment();
        payment.MarkSucceeded();

        payment.Refund();

        Assert.Equal(PaymentStatus.Refunded, payment.Status);
    }

    /// <summary>Chứng minh không thể Refund Payment còn Pending theo Evidence Day 48.</summary>
    [Fact]
    public void Refund_FromPending_ThrowsPaymentDomainException()
    {
        // Evidence Day 48: chứng minh không thể hoàn tiền khi chưa thu được tiền từ gateway.
        var payment = CreatePayment();

        Assert.Throws<PaymentDomainException>(() => payment.Refund());
    }
}
