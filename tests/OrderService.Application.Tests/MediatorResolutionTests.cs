// File này chứa test chứng minh MediatR resolve đúng handler trong OrderService.Application.
// Tạo ở Day 41 để cung cấp Evidence rằng application layer đã đăng ký đúng assembly và mỗi use case có thể được resolve qua IMediator.
// Application layer này dùng test để khẳng định handler command/query được đăng ký đúng mục đích.

using MediatR;
using Microsoft.Extensions.DependencyInjection;
using OrderService.Application.Commands;
using OrderService.Application.Queries;
using OrderService.Domain.ValueObjects;
using Xunit;

namespace OrderService.Application.Tests;

public class MediatorResolutionTests
{
    // Test này chứng minh Evidence Day 41: AddApplication phải đăng ký đúng các handler command/query từ assembly OrderService.Application.
    [Fact]
    public void AddApplication_RegistersOrderHandlers()
    {
        var services = new ServiceCollection();
        services.AddApplication();

        using var provider = services.BuildServiceProvider();

        var mediator = provider.GetRequiredService<IMediator>();
        var placeHandler = provider.GetRequiredService<IRequestHandler<PlaceOrderCommand, OrderId>>();
        var cancelHandler = provider.GetRequiredService<IRequestHandler<CancelOrderCommand>>();
        var getByIdHandler = provider.GetRequiredService<IRequestHandler<GetOrderByIdQuery, OrderDetailDto?>>();

        Assert.NotNull(mediator);
        Assert.IsType<PlaceOrderCommandHandler>(placeHandler);
        Assert.IsType<CancelOrderCommandHandler>(cancelHandler);
        Assert.IsType<GetOrderByIdQueryHandler>(getByIdHandler);
    }

    // Test này chứng minh Evidence Day 41: MediatR điều hướng PlaceOrderCommand tới handler và nhận lại OrderId hợp lệ.
    [Fact]
    public async Task Mediator_Resolves_PlaceOrderCommandHandler()
    {
        var services = new ServiceCollection();
        services.AddApplication();

        using var provider = services.BuildServiceProvider();

        var mediator = provider.GetRequiredService<IMediator>();
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

        var orderId = await mediator.Send(command);

        Assert.NotEqual(default(OrderId), orderId);
    }

    // Test này chứng minh Evidence Day 41: DI có thể resolve handler khung hủy đơn mà không thực thi phần chưa có repository.
    [Fact]
    public void Mediator_Resolves_CancelOrderCommandHandler()
    {
        var services = new ServiceCollection();
        services.AddApplication();

        using var provider = services.BuildServiceProvider();

        var handler = provider.GetRequiredService<IRequestHandler<CancelOrderCommand>>();

        Assert.NotNull(handler);
        Assert.IsType<CancelOrderCommandHandler>(handler);
    }

    // Test này chứng minh Evidence Day 41: DI có thể resolve handler khung đọc đơn với kết quả nullable.
    [Fact]
    public void Mediator_Resolves_GetOrderByIdQueryHandler()
    {
        var services = new ServiceCollection();
        services.AddApplication();

        using var provider = services.BuildServiceProvider();

        var handler = provider.GetRequiredService<IRequestHandler<GetOrderByIdQuery, OrderDetailDto?>>();

        Assert.NotNull(handler);
        Assert.IsType<GetOrderByIdQueryHandler>(handler);
    }
}
