// File này chứa test chứng minh validator chặn input sai trước khi Order được tạo.
// Tạo ở Day 41 để cung cấp Evidence rằng command đầu vào không được lọt qua khi CustomerId, địa chỉ hoặc item không hợp lệ.
// Application layer dùng validator này để bảo vệ aggregate khỏi dữ liệu lỗi ngay từ cổng vào.

using OrderService.Application.Commands;
using OrderService.Application.Validators;
using Xunit;

namespace OrderService.Application.Tests;

public class PlaceOrderCommandValidatorTests
{
    // Test này chứng minh Evidence Day 41: command với CustomerId rỗng phải bị từ chối ngay, không được tạo Order mồ côi không biết chủ.
    [Fact]
    public void Validate_WhenCustomerIdEmpty_ReturnsFailure()
    {
        var validator = new PlaceOrderCommandValidator();
        var command = new PlaceOrderCommand(
            Guid.Empty,
            "123 Lê Lợi",
            "Hà Nội",
            "Việt Nam",
            "100000",
            new List<PlaceOrderItem>
            {
                new(Guid.NewGuid(), 2, 150000m, "VND")
            });

        var result = validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, x => x.PropertyName == nameof(PlaceOrderCommand.CustomerId));
    }

    // Test này chứng minh Evidence Day 41: thân đơn có item không hợp lệ hoặc rỗng phải bị từ chối để tránh tạo đơn không có giá trị kinh doanh.
    [Fact]
    public void Validate_WhenItemsEmpty_ReturnsFailure()
    {
        var validator = new PlaceOrderCommandValidator();
        var command = new PlaceOrderCommand(
            Guid.NewGuid(),
            "123 Lê Lợi",
            "Hà Nội",
            "Việt Nam",
            "100000",
            new List<PlaceOrderItem>());

        var result = validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, x => x.PropertyName == nameof(PlaceOrderCommand.Items));
    }

    // Test này chứng minh Evidence Day 41: địa chỉ thiếu Street phải bị validator từ chối trước khi handler tạo Order.
    [Fact]
    public void Validator_Rejects_EmptyAddress()
    {
        var command = new PlaceOrderCommand(
            Guid.NewGuid(),
            string.Empty,
            "Hà Nội",
            "Việt Nam",
            "100000",
            new List<PlaceOrderItem>
            {
                new(Guid.NewGuid(), 1, 150000m, "VND")
            });

        var result = new PlaceOrderCommandValidator().Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, x => x.PropertyName == nameof(PlaceOrderCommand.Street));
    }

    // Test này chứng minh Evidence Day 41: command đủ khách hàng, địa chỉ và dòng hàng hợp lệ được validator chấp nhận.
    [Fact]
    public void Validator_Accepts_ValidCommand()
    {
        var command = new PlaceOrderCommand(
            Guid.NewGuid(),
            "123 Lê Lợi",
            "Hà Nội",
            "Việt Nam",
            "100000",
            new List<PlaceOrderItem>
            {
                new(Guid.NewGuid(), 1, 150000m, "VND")
            });

        var result = new PlaceOrderCommandValidator().Validate(command);

        Assert.True(result.IsValid);
    }
}
