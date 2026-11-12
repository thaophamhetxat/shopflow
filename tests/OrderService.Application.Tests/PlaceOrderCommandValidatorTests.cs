// File này chứa test chứng minh validator chặn input sai trước khi Order được tạo.
// Tạo ở Day 41 để cung cấp Evidence rằng command đầu vào không được lọt qua khi CustomerId, địa chỉ hoặc item không hợp lệ.
// Application layer dùng validator này để bảo vệ aggregate khỏi dữ liệu lỗi ngay từ cổng vào.

using System.Diagnostics;
using OrderService.Application.Commands;
using OrderService.Application.Idempotency;
using OrderService.Application.Validators;
using OrderService.Domain.Aggregates;
using OrderService.Domain.Repositories;
using OrderService.Domain.ValueObjects;
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

    // Test này chứng minh: mã tiền tệ phải là 3 chữ cái viết hoa, lowercase không được phép qua validator.
    [Fact]
    public void Validator_Rejects_LowercaseCurrency()
    {
        var command = new PlaceOrderCommand(
            Guid.NewGuid(),
            "123 Lê Lợi",
            "Hà Nội",
            "Việt Nam",
            "100000",
            new List<PlaceOrderItem>
            {
                new(Guid.NewGuid(), 1, 150000m, "vnd")
            });

        var result = new PlaceOrderCommandValidator().Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, x => x.PropertyName.EndsWith(nameof(PlaceOrderItem.UnitPriceCurrency)));
    }

    // Test này chứng minh Evidence Day 64: ActivityListener cho source `ShopFlow` bắt được span `order.place` khi handler tạo order thật.
    [Fact]
    public async Task PlaceOrder_StartsOrderPlaceSpan()
    {
        var spans = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "ShopFlow",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => spans.Add(activity)
        };

        ActivitySource.AddActivityListener(listener);

        var handler = new PlaceOrderCommandHandler(new FakeOrderRepository(), new FakeIdempotencyStore());
        var command = new PlaceOrderCommand(
            Guid.NewGuid(),
            "123 Lê Lợi",
            "Hà Nội",
            "Việt Nam",
            "100000",
            new List<PlaceOrderItem>
            {
                new(Guid.NewGuid(), 1, 110000m, "VND")
            },
            "trace-key");

        var orderId = await handler.Handle(command, CancellationToken.None);

        var span = Assert.Single(spans.Where(s => s.OperationName == "order.place"));
        Assert.NotEqual(default, span.TraceId);
        Assert.Equal(orderId.Value.ToString(), span.Tags.First(t => t.Key == "order.id").Value.ToString());
        Assert.Equal(command.CustomerId.ToString(), span.Tags.First(t => t.Key == "customer.id").Value.ToString());
    }

    private sealed class FakeOrderRepository : IOrderRepository
    {
        public Task AddAsync(Order order, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<Order?> GetByIdAsync(OrderId id, CancellationToken cancellationToken = default) => Task.FromResult<Order?>(null);
        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeIdempotencyStore : IIdempotencyStore
    {
        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) => Task.FromResult(default(T?));
        public Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
