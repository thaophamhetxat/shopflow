// File này chứa validator cho PlaceOrderCommand, dùng để chặn dữ liệu đầu vào không hợp lệ trước khi tạo Order.
// Tạo ở Day 41 để đảm bảo các rule nghiệp vụ được kiểm tra ngay ở cổng vào, tránh đơn hàng không rõ chủ, rỗng hoặc trống item.
// Application layer giữ danh sách rule rõ ràng để kiểm soát input trước khi map sang domain.

using FluentValidation;
using OrderService.Application.Commands;

namespace OrderService.Application.Validators;

/// <summary>
/// Validator cho command đặt hàng, đảm bảo khách hàng, địa chỉ và danh sách item đều hợp lệ trước khi tạo Order aggregate.
/// Mỗi rule được viết để chặn input sai khỏi domain và giảm nguy cơ exception xuất hiện ở tầng sâu hơn.
/// </summary>
public sealed class PlaceOrderCommandValidator : AbstractValidator<PlaceOrderCommand>
{
    public PlaceOrderCommandValidator()
    {
        // CustomerId rỗng nghĩa là không biết đơn của ai, phải chặn trước khi tạo Order để tránh đơn mồ côi không tra được chủ.
        RuleFor(x => x.CustomerId)
            .NotEmpty();

        // Street rỗng nghĩa là địa chỉ nhận hàng thiếu thông tin cần thiết, nên phải chặn trước khi tạo Order để không phát sinh đơn không giao được.
        RuleFor(x => x.Street)
            .NotEmpty();

        // City rỗng nghĩa là không có địa điểm giao hàng rõ ràng, phải chặn ngay để tránh thất bại ở vận chuyển hoặc xác định khu vực.
        RuleFor(x => x.City)
            .NotEmpty();

        // Country trống nghĩa là không xác định được thị trường và chính sách vận chuyển, nên phải chặn ở cổng vào để tránh kiện dữ liệu sai.
        RuleFor(x => x.Country)
            .NotEmpty();

        // Items rỗng nghĩa là đơn hàng không có giá trị kinh doanh, Aggregate Day 40 đã cấm Confirm đơn rỗng nên validator phải chặn từ đầu, đừng để lọt xuống domain mới lỗi.
        RuleFor(x => x.Items)
            .NotNull()
            .NotEmpty();

        // Dòng hàng thiếu ProductId nghĩa là không biết bán cái gì, nên phải chặn trước khi kiểm kho hoặc thanh toán.
        RuleForEach(x => x.Items)
            .ChildRules(item =>
            {
                item.RuleFor(x => x.ProductId)
                    .NotEmpty();

                // Quantity <= 0 nghĩa là không có hàng thực sự được đặt, nên phải chặn để tránh tạo đơn rỗng hoặc sai số lượng.
                item.RuleFor(x => x.Quantity)
                    .GreaterThan(0);

                // UnitPriceAmount <= 0 nghĩa là giá trị đặt hàng không có ý nghĩa thương mại, phải chặn để tránh đơn không có giá trị hoặc sai báo giá.
                item.RuleFor(x => x.UnitPriceAmount)
                    .GreaterThan(0m);

                // Currency rỗng hoặc sai định dạng làm tổng tiền không thể hợp lệ, nên phải chặn trước khi map sang Money trong domain.
                item.RuleFor(x => x.UnitPriceCurrency)
                    .NotEmpty()
                    .Must(currency => currency.Length == 3 && currency.All(char.IsLetter));
            });
    }
}
