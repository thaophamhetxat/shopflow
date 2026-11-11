// File này chứa DTO đọc tổng quan cho list order của OrderService.Application.
// Tạo ở Day 46 để project Day 47 có thể trả dữ liệu list cho API mà không cần nạp cả aggregate Order về.
// Đây là read model riêng, không thay thế DTO Day 41, chỉ phục vụ một trường hợp đọc nhanh và tập trung cho dashboard/report.

namespace OrderService.Application.Queries;

/// <summary>
/// DTO đọc cho danh sách đơn hàng theo khách hàng, chỉ giữ các cột cần thiết cho summary list và không tải dữ liệu chi tiết từng item.
/// DTO này được dùng trong query list keyset để trả nhanh trên bảng orders mà không cần load cả aggregate hay danh sách item.
/// </summary>
public sealed record OrderSummaryDto(
    Guid OrderId,
    Guid CustomerId,
    string Status,
    decimal TotalAmount,
    string TotalCurrency,
    int ItemCount,
    DateTime CreatedAt);
