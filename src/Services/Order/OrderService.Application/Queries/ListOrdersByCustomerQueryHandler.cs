// File này xử lý query keyset list order tại Application layer.
// Tạo ở Day 46; Day 61 giải mã Base64Url và chuyển cặp cursor cho read-model repository.
// API dùng kết quả validation để giữ status 422 cho cursor không hợp lệ.

using FluentValidation;
using MediatR;
using OrderService.Domain.ValueObjects;

namespace OrderService.Application.Queries;

/// <summary>
/// Handler cho ListOrdersByCustomerQuery, nhận list summary theo customer bằng repository read model projection và giữ logic paging ở tầng query.
/// Handler này không ném exception khi khách hàng chưa có đơn nào; nó trả list rỗng theo quy tắc query đọc dữ liệu.
/// </summary>
public sealed class ListOrdersByCustomerQueryHandler : IRequestHandler<ListOrdersByCustomerQuery, IReadOnlyList<OrderSummaryDto>>
{
    private readonly IOrderReadModelRepository _orderRepository;

    /// <summary>
    /// Khởi tạo handler với repository đọc projection cho danh sách order theo customer.
    /// </summary>
    /// <param name="orderRepository">Repository thực thi query list summary bằng projection SQL.</param>
    public ListOrdersByCustomerQueryHandler(IOrderReadModelRepository orderRepository)
    {
        _orderRepository = orderRepository;
    }

    /// <summary>
    /// Lấy trang list order của khách hàng theo keyset cursor, sau khi clamp pageSize về 1–50 để tránh client gởi số quá lớn hoặc quá nhỏ.
    /// </summary>
    /// <param name="request">Query yêu cầu khách hàng và cursor trang hiện tại.</param>
    /// <param name="cancellationToken">Token hủy khi handler đang chạy.</param>
    /// <returns>Danh sách summary theo trang, rỗng nếu khách hàng chưa có đơn.</returns>
    public async Task<IReadOnlyList<OrderSummaryDto>> Handle(ListOrdersByCustomerQuery request, CancellationToken cancellationToken)
    {
        // Clamp PageSize 1–50: giới hạn nhỏ để tránh request phá database với pageSize quá lớn, đồng thời giữ đồng nhất với quy tắc day 46 chỉ cho phép tối đa 50 item/trang.
        var boundedPageSize = Math.Clamp(request.PageSize, 1, 50);

        var customerId = CustomerId.From(request.CustomerId);

        DateTime cursorCreatedAt = default;
        Guid cursorOrderId = Guid.Empty;
        var hasValidCursor = false;

        if (!string.IsNullOrWhiteSpace(request.Cursor))
        {
            if (!OrderListCursor.TryDecode(request.Cursor, out cursorCreatedAt, out cursorOrderId))
            {
                throw new ValidationException(new[]
                {
                    new FluentValidation.Results.ValidationFailure("cursor", "cursor không hợp lệ.")
                });
            }

            hasValidCursor = true;
        }

        var decodedCursorCreatedAt = hasValidCursor ? (DateTime?)cursorCreatedAt : null;
        var decodedCursorOrderId = hasValidCursor ? (OrderId?)OrderId.From(cursorOrderId) : null;

        return await _orderRepository.ListOrderSummariesByCustomerAsync(
            customerId,
            decodedCursorCreatedAt,
            decodedCursorOrderId,
            boundedPageSize,
            cancellationToken);
    }
}
