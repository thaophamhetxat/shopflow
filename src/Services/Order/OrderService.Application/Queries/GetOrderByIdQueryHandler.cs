// File này chứa handler query lấy chi tiết đơn hàng của OrderService.Application.
// Tạo ở Day 45 để phản ánh repository thực tế và map data từ aggregate sang DTO nghiêm ngặt.
// Day 46 cập nhật handler này để đọc bằng projection read model tách biệt, không nạp aggregate Order vào memory rồi map tay.

using MediatR;
using OrderService.Domain.Repositories;
using OrderService.Domain.ValueObjects;

namespace OrderService.Application.Queries;

/// <summary>
/// Handler cho GetOrderByIdQuery, chỉ gọi repository projection để đọc OrderDetailDto đã được thiết kế riêng cho read model.
/// Nếu không tìm thấy đơn thì trả null theo quy tắc query: không tìm thấy là kết quả hợp lệ, không phải exception nghiệp vụ.
/// </summary>
public sealed class GetOrderByIdQueryHandler : IRequestHandler<GetOrderByIdQuery, OrderDetailDto?>
{
    private readonly IOrderRepository _orderRepository;

    /// <summary>
    /// Khởi tạo handler với repository truy vấn Order bằng read model projection thay cho aggregate entity.
    /// </summary>
    /// <param name="orderRepository">Repository dùng để đọc OrderDetailDto trực tiếp từ PostgreSQL.</param>
    public GetOrderByIdQueryHandler(IOrderRepository orderRepository)
    {
        _orderRepository = orderRepository;
    }

    /// <summary>
    /// Tìm Order theo id và trả về OrderDetailDto nếu tồn tại; null nếu không tìm thấy.
    /// </summary>
    /// <param name="request">Query yêu cầu đọc đơn theo OrderId.</param>
    /// <param name="cancellationToken">Token hủy nếu request bị cancel.</param>
    /// <returns>OrderDetailDto đầy đủ nếu đơn tồn tại, ngược lại null.</returns>
    public async Task<OrderDetailDto?> Handle(GetOrderByIdQuery request, CancellationToken cancellationToken)
    {
        var orderId = OrderId.From(request.OrderId);

        // Projection bằng Select: lấy đúng dữ liệu cần cho DTO, không còn Include hoặc map từ aggregate vào memory để tránh tải cả Order + item cùng lúc.
        return await _orderRepository.GetOrderDetailAsync(orderId, cancellationToken);
    }
}
