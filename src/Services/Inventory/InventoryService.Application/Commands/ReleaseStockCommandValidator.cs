// File này chứa validator cho command ReleaseStock của InventoryService Application.
// Tạo ở Day 48 để số lượng không hợp lệ bị từ chối trước khi handler nạp Product.
// Saga Day 52 sẽ dùng cùng quy tắc khi bù trừ thao tác giữ kho.

using FluentValidation;

namespace InventoryService.Application.Commands;

/// <summary>
/// Kiểm tra số lượng của command ReleaseStock.
/// </summary>
public sealed class ReleaseStockCommandValidator : AbstractValidator<ReleaseStockCommand>
{
    /// <summary>
    /// Khai báo quy tắc số lượng Release phải lớn hơn không.
    /// </summary>
    public ReleaseStockCommandValidator()
    {
        RuleFor(command => command.Quantity)
            .GreaterThan(0)
            .WithMessage("Số lượng cần trả kho phải lớn hơn 0.");
    }
}
