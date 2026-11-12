// File này khai báo MediatR query cho danh sách đơn theo khách hàng.
// Tạo ở Day 46; Day 61 chuyển cursor từ Guid thô sang Base64Url chứa CreatedAt và OrderId.
// Endpoint OrderService.API gửi cùng query mà không thay đổi response shape của list.

using MediatR;

namespace OrderService.Application.Queries;

/// <summary>
/// Query yêu cầu lấy danh sách đơn hàng theo khách hàng theo kiểu keyset, trong đó cursor mang cả CreatedAt và OrderId để duy trì thứ tự ổn định khi nhiều đơn cùng timestamp.
/// Cursor dạng base64 URL-safe được mã hóa ở `OrderListCursor` và gửi qua query string, không giữ nguyên raw Guid như Day 60.
/// </summary>
public sealed record ListOrdersByCustomerQuery(Guid CustomerId, string? Cursor, int PageSize = 20) : IRequest<IReadOnlyList<OrderSummaryDto>>;
