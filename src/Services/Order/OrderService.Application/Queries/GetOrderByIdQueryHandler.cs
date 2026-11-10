// File này chứa handler khung cho query GetOrderByIdQuery.
// Tạo ở Day 41 để định nghĩa thứ tự xử lý đọc dữ liệu đơn hàng khi repository chưa được thêm vào.
// Application layer giữ handler như một contract rõ ràng cho Day 42 và các ngày sau.

using MediatR;

namespace OrderService.Application.Queries;

/// <summary>
/// Handler khung cho GetOrderByIdQuery, sẽ đọc Order theo OrderId và map sang OrderDto khi repository có mặt ở Day 42.
/// </summary>
public sealed class GetOrderByIdQueryHandler : IRequestHandler<GetOrderByIdQuery, OrderDto?>
{
    public Task<OrderDto?> Handle(GetOrderByIdQuery request, CancellationToken cancellationToken)
    {
        // Chưa thể đọc dữ liệu khi Day 41 chưa có repository; ném lỗi rõ ràng thay vì trả một DTO rỗng giả.
        throw new NotImplementedException("Chưa có repository để đọc Order — Day 42 sẽ hoàn thiện handler này.");
    }
}
