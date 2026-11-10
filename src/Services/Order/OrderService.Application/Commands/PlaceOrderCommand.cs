// File này chứa command đặt đơn hàng cho OrderService.Application.
// Tạo ở Day 41 để UI hoặc API có thể gửi yêu cầu tạo đơn bằng dữ liệu nghiệp vụ rõ ràng và không trao đổi trực tiếp với aggregate.
// Application layer dùng command này để chuyển đổi từ input sang domain object an toàn.

using MediatR;
using OrderService.Domain.ValueObjects;

namespace OrderService.Application.Commands;

/// <summary>
/// Command yêu cầu đặt mới một đơn hàng cho khách hàng, bao gồm địa chỉ giao hàng và danh sách sản phẩm cần mua.
/// API hoặc orchestrator gửi command này và nhận lại OrderId của đơn vừa được tạo.
/// </summary>
public sealed record PlaceOrderCommand(
    Guid CustomerId,
    string Street,
    string City,
    string Country,
    string? PostalCode,
    List<PlaceOrderItem> Items,
    string? IdempotencyKey = null) : IRequest<OrderId>;

/// <summary>
/// Dữ liệu một dòng hàng do người gửi command cung cấp để handler tạo OrderItem trong Domain.
/// Bao gồm mã sản phẩm, số lượng, đơn giá và mã tiền tệ.
/// </summary>
public sealed record PlaceOrderItem(Guid ProductId, int Quantity, decimal UnitPriceAmount, string UnitPriceCurrency);
