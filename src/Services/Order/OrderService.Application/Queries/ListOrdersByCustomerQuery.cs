using MediatR;

namespace OrderService.Application.Queries;

/// <summary>
/// Query yêu cầu lấy danh sách đơn hàng theo khách hàng theo kiểu keyset, cho phép client di chuyển cursor bằng OrderId của đơn cuối trang trước.
/// Nếu cursor null, handler trả trang đầu; nếu cursor có giá trị, handler trả các đơn tiếp theo theo thứ tự CreatedAt giảm dần rồi OrderId giảm dần.
/// </summary>
public sealed record ListOrdersByCustomerQuery(Guid CustomerId, Guid? CursorOrderId, int PageSize = 20) : IRequest<IReadOnlyList<OrderSummaryDto>>;
