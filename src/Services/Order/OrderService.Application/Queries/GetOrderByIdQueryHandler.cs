// File này chứa handler khung cho query GetOrderByIdQuery.
// Tạo ở Day 41 để định nghĩa thứ tự xử lý đọc dữ liệu đơn hàng khi repository chưa được thêm vào.
// Application layer giữ handler như một contract rõ ràng cho Day 42 và các ngày sau.

using MediatR;

namespace OrderService.Application.Queries;

/// <summary>
/// Handler khung cho GetOrderByIdQuery: bước đầu tìm order theo OrderId, bước sau map dữ liệu sang OrderDto, cuối cùng trả về DTO đã chuẩn hóa.
/// Hiện tại chưa có repository nên handler giữ trạng thái khung và chờ triển khai ở các day sau.
/// </summary>
public sealed class GetOrderByIdQueryHandler : IRequestHandler<GetOrderByIdQuery, OrderDto>
{
    public Task<OrderDto> Handle(GetOrderByIdQuery request, CancellationToken cancellationToken)
    {
        var emptyOrder = new OrderDto(
            Guid.Empty,
            Guid.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            null,
            0m,
            "USD",
            Array.Empty<OrderItemDto>());

        return Task.FromResult(emptyOrder);
    }
}
