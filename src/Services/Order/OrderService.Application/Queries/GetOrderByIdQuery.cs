// File này chứa query đọc thông tin một đơn hàng theo OrderId.
// Tạo ở Day 41 để chuẩn hóa cách application layer đọc đơn hàng khi chưa có repository và chưa có API thực thi.
// Application layer định nghĩa yêu cầu, còn phần truy xuất dữ liệu sẽ hoàn thiện ở Day 42.

using MediatR;

namespace OrderService.Application.Queries;

/// <summary>
/// Query yêu cầu lấy thông tin đơn hàng theo mã OrderId, dùng cho UI, API hoặc workflow cần đọc chi tiết đơn đang tồn tại.
/// Query này không lưu dữ liệu mà chỉ định nghĩa request phục vụ cho handler xử lý.
/// </summary>
public sealed record GetOrderByIdQuery(Guid OrderId) : IRequest<OrderDto>;
