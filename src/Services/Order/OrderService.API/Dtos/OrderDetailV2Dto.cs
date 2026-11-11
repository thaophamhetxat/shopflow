// File này chứa DTO contract cho phiên bản v2 của chi tiết order, thêm EstimatedDelivery mà v1 không có để demo contract versioning.
// Tạo ở Day 47 để client v2 có thể gọi GET /api/v2/orders/{id} và đọc thêm ngày giao ước tính mà không thay đổi schema v1 cũ.
// Day sau (Saga Day 52 / Inventory Day 48) có thể thay bằng ngày thật từ kho hoặc vận chuyển, lúc đó chỉ sửa đúng chỗ này.
using OrderService.Application.Queries;

namespace OrderService.API.Dtos;

/// <summary>
/// DTO v2 cho chi tiết order, kế thừa cấu trúc v1 và thêm EstimatedDelivery là mốc demo chỉ để test versioning.
/// Quy tắc tạm của Day 47 là CreatedAt + 3 ngày, không dùng trong domain hay application, chỉ tồn tại ở tầng API.
/// </summary>
/// <param name="OrderId">Mã đơn hàng.</param>
/// <param name="CustomerId">Mã khách hàng mua hàng.</param>
/// <param name="Status">Trạng thái hiện tại của đơn.</param>
/// <param name="TotalAmount">Tổng tiền đơn hàng.</param>
/// <param name="TotalCurrency">Mã tiền tệ của tổng tiền.</param>
/// <param name="CreatedAt">Thời điểm tạo đơn.</param>
/// <param name="Items">Danh sách dòng hàng trong đơn.</param>
/// <param name="EstimatedDelivery">Ngày giao ước tính demo, luôn bằng CreatedAt + 3 ngày.</param>
public sealed record OrderDetailV2Dto(
    Guid OrderId,
    Guid CustomerId,
    string Status,
    decimal TotalAmount,
    string TotalCurrency,
    DateTime CreatedAt,
    IReadOnlyList<OrderDetailItemDto> Items,
    DateTime EstimatedDelivery)
{
    /// <summary>
    /// Chuyển đổi từ OrderDetailDto v1 sang OrderDetailV2Dto v2 bằng cách copy 7 field đầu và bổ sung EstimatedDelivery theo quy tắc demo Day 47.
    /// </summary>
    /// <param name="detail">DTO v1 cần nâng cấp sang v2.</param>
    /// <returns>Phiên bản v2 với trường EstimatedDelivery cộng thêm 3 ngày.</returns>
    public static OrderDetailV2Dto From(OrderDetailDto detail)
    {
        ArgumentNullException.ThrowIfNull(detail);

        return new OrderDetailV2Dto(
            detail.OrderId,
            detail.CustomerId,
            detail.Status,
            detail.TotalAmount,
            detail.TotalCurrency,
            detail.CreatedAt,
            detail.Items,
            detail.CreatedAt.AddDays(3));
    }
}
