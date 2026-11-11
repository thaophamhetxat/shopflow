// File này chứa query đọc thông tin một đơn hàng theo OrderId.
// Tạo ở Day 41 để chuẩn hóa cách application layer đọc đơn hàng khi chưa có repository và chưa có API thực thi.
// Day 46 cập nhật query này để trả về read model projection phân biệt với DTO Day 41 và không kéo aggregate khỏi database.

using MediatR;

namespace OrderService.Application.Queries;

/// <summary>
/// Query yêu cầu lấy chi tiết đơn hàng theo mã OrderId, dùng cho UI, API hoặc workflow cần đọc dữ liệu rõ ràng nhưng không lộ trực tiếp aggregate Domain.
/// Kết quả có thể null nếu chưa tìm thấy đơn hàng.
/// </summary>
public sealed record GetOrderByIdQuery(Guid OrderId) : IRequest<OrderDetailDto?>;
