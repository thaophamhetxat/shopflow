// File này chứa request DTO cho thao tác thanh toán của Payment API.
// Tạo ở Day 48 để API nhận input HTTP rồi chuyển sang command Application có validation.
// Gateway Day 49 và Saga Day 52 sẽ gửi yêu cầu Charge theo hợp đồng endpoint này.

namespace PaymentService.API.Dtos;

/// <summary>
/// Body của yêu cầu tạo khoản thanh toán cho một Order.
/// </summary>
/// <param name="OrderId">Mã đơn hàng cần thanh toán.</param>
/// <param name="Amount">Số tiền cần thu.</param>
/// <param name="Currency">Mã tiền tệ gồm ba chữ cái in hoa.</param>
public sealed record ProcessPaymentRequest(Guid OrderId, decimal Amount, string Currency);
