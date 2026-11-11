// File này chứa DTO tối giản dùng trong Saga để không cần phụ thuộc trực tiếp vào Project PaymentService.Application.
// Layer Application tập trung vào logic nghiệp vụ; chỉ cần `Status` `Succeeded`/`Failed` từ Payment API để quyết định tiếp tục.

namespace OrderService.Application.Sagas;

/// <summary>
/// Kết quả charge từ PaymentService sau khi endpoint `/payments` trả về JSON.
/// </summary>
/// <param name="Status">Trạng thái thanh toán đọc từ trường `status` trong JSON.</param>
public sealed record PaymentChargeResult(string Status);
