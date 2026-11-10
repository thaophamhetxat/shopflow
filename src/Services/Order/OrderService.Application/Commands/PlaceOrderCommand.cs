// File này chứa command đặt đơn hàng cho OrderService.Application.
// Tạo ở Day 41 để UI hoặc API có thể gửi yêu cầu tạo đơn bằng dữ liệu nghiệp vụ rõ ràng và không trao đổi trực tiếp với aggregate.
// Application layer dùng command này để chuyển đổi từ input sang domain object an toàn.

using MediatR;
using OrderService.Application.Queries;

namespace OrderService.Application.Commands;

/// <summary>
/// Command yêu cầu đặt mới một đơn hàng cho khách hàng, bao gồm địa chỉ giao hàng và danh sách sản phẩm cần mua.
/// Command này được người gửi từ API hoặc orchestrator dùng để tạo Order aggregate từ input hợp lệ.
/// </summary>
public sealed record PlaceOrderCommand(
    Guid CustomerId,
    string Street,
    string City,
    string Country,
    string? PostalCode,
    List<PlaceOrderItem> Items) : IRequest<OrderDto>;

/// <summary>
/// Dòng hàng được đặt trong một order, gồm mã sản phẩm, số lượng và đơn giá theo tiền tệ cụ thể.
/// Item này không gắn với Entity của domain trực tiếp mà sẽ được map thành OrderItem ở handler.
/// </summary>
public sealed record PlaceOrderItem(Guid ProductId, int Quantity, decimal UnitPrice, string Currency);
