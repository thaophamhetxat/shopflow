// File này chứa giao diện marker cho các sự kiện nghiệp vụ của Domain Order.
// Tạo ở Day 41 để Order aggregate có thể phát ra event theo mô hình DDD và chuẩn bị cho Outbox/Saga ở các day sau.
// Domain này giữ event như một thể hiện của tác động nghiệp vụ, không gắn trực tiếp với infrastructure hay database.

namespace OrderService.Domain;

/// <summary>
/// Giao diện đánh dấu một sự kiện nghiệp vụ do Aggregate Order phát ra khi trạng thái quan trọng thay đổi.
/// Sự kiện này chỉ là thông báo nghiệp vụ, còn việc lưu vào outbox hay dispatch sang các service khác sẽ do layer phía sau xử lý.
/// </summary>
public interface IDomainEvent
{
}
