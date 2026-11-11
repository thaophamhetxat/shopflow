// File này chứa validator cho command ReserveStock của InventoryService Application.
// Tạo ở Day 48 để từ chối số lượng không dương trước khi handler đọc hoặc thay đổi tồn kho.
// API Day 48 ánh xạ lỗi validation thành 422; Saga Day 52 dùng cùng quy tắc đầu vào.

using FluentValidation;

namespace InventoryService.Application.Commands;

/// <summary>
/// Kiểm tra số lượng của command ReserveStock.
/// </summary>
public sealed class ReserveStockCommandValidator : AbstractValidator<ReserveStockCommand>
{
    /// <summary>
    /// Khai báo quy tắc số lượng Reserve phải lớn hơn không.
    /// </summary>
    public ReserveStockCommandValidator()
    {
        RuleFor(command => command.Quantity)
            .GreaterThan(0)
            .WithMessage("Số lượng cần giữ phải lớn hơn 0.");
    }
}
