// File này chứa test chứng minh MediatR resolve đúng handler trong OrderService.Application.
// Tạo ở Day 41 để cung cấp Evidence rằng application layer đã đăng ký đúng assembly và mỗi use case có thể được resolve qua IMediator.
// Application layer này dùng test để khẳng định handler command/query được đăng ký đúng mục đích.

using MediatR;
using Microsoft.Extensions.DependencyInjection;
using OrderService.Application.Commands;
using OrderService.Application.Queries;
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
        var placeHandler = provider.GetRequiredService<IRequestHandler<PlaceOrderCommand, OrderDto>>();
        var cancelHandler = provider.GetRequiredService<IRequestHandler<CancelOrderCommand, bool>>();
        var getByIdHandler = provider.GetRequiredService<IRequestHandler<GetOrderByIdQuery, OrderDto>>();

        Assert.NotNull(mediator);
        Assert.IsType<PlaceOrderCommandHandler>(placeHandler);
        Assert.IsType<CancelOrderCommandHandler>(cancelHandler);
        Assert.IsType<GetOrderByIdQueryHandler>(getByIdHandler);
    }
}
