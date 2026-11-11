// File này chứa extension method đăng ký MediatR cho OrderService.Application.
// Tạo ở Day 41 để layer Application có thể tự đăng ký handler và query/command khi service bắt đầu chạy.
// Application layer này giữ các use case và DI, không chứa logic persistence.

using System.Collections.Concurrent;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using OrderService.Application.Behaviors;
using OrderService.Application.Idempotency;
using OrderService.Application.Queries;
using OrderService.Domain.Aggregates;
using OrderService.Domain.Repositories;
using OrderService.Domain.ValueObjects;

namespace OrderService.Application;

/// <summary>
/// File này chứa extension method đăng ký toàn bộ pipeline của OrderService.Application.
/// Layer Application dùng nó để nối MediatR, validation và logging trước khi handler chạy thực tế.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Đăng ký MediatR, FluentValidation và các behavior mở rộng để tất cả command/query đi qua validation và logging trước khi tới handler.
    /// </summary>
    /// <param name="services">Container DI hiện tại.</param>
    /// <returns>Container DI đã có pipeline xử lý cho OrderService.Application.</returns>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddLogging();

        var inMemoryOrderRepository = new InMemoryOrderRepository();
        services.AddSingleton<IOrderRepository>(inMemoryOrderRepository);
        services.AddSingleton<IOrderReadModelRepository>(inMemoryOrderRepository);
        services.AddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();

        foreach (var validatorType in typeof(DependencyInjection).Assembly
                     .GetTypes()
                     .Where(type => type.IsClass && !type.IsAbstract && type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IValidator<>))))
        {
            foreach (var implementedInterface in validatorType.GetInterfaces()
                         .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IValidator<>)))
            {
                services.AddTransient(implementedInterface, validatorType);
            }
        }

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(typeof(DependencyInjection).Assembly);
            cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });

        return services;
    }

    private sealed class InMemoryOrderRepository : IOrderRepository, IOrderReadModelRepository
    {
        private readonly ConcurrentDictionary<OrderId, Order> _orders = new();

        public Task AddAsync(Order order, CancellationToken cancellationToken = default)
        {
            _orders[order.Id] = order;
            return Task.CompletedTask;
        }

        public Task<Order?> GetByIdAsync(OrderId id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_orders.TryGetValue(id, out var order) ? order : null);
        }

        public Task<OrderDetailDto?> GetOrderDetailAsync(OrderId id, CancellationToken cancellationToken = default)
        {
            var order = _orders.TryGetValue(id, out var foundOrder) ? foundOrder : null;
            if (order is null)
            {
                return Task.FromResult<OrderDetailDto?>(null);
            }

            return Task.FromResult<OrderDetailDto?>(new OrderDetailDto(
                order.Id.Value,
                order.CustomerId.Value,
                order.Status.ToString(),
                order.Total.Amount,
                order.Total.Currency,
                order.CreatedAt,
                order.Items.Select(item => new OrderDetailItemDto(
                    item.ProductId,
                    item.Quantity,
                    item.UnitPrice.Amount,
                    item.UnitPrice.Currency,
                    item.LineTotal.Amount)).ToList()));
        }

        public Task<IReadOnlyList<OrderSummaryDto>> ListOrderSummariesByCustomerAsync(CustomerId customerId, OrderId? cursor, int pageSize, CancellationToken cancellationToken = default)
        {
            var items = _orders.Values
                .Where(order => order.CustomerId == customerId)
                .OrderByDescending(order => order.CreatedAt)
                .ThenByDescending(order => order.Id)
                .ToList();

            if (cursor is OrderId cursorOrderId)
            {
                var cursorOrder = items.FirstOrDefault(order => order.Id == cursorOrderId);
                if (cursorOrder is not null)
                {
                    items = items
                        .Where(order => order.CreatedAt < cursorOrder.CreatedAt ||
                            (order.CreatedAt == cursorOrder.CreatedAt && order.Id.Value < cursorOrderId.Value))
                        .ToList();
                }
            }

            var summaries = items
                .Take(Math.Clamp(pageSize, 1, 50))
                .Select(order => new OrderSummaryDto(
                    order.Id.Value,
                    order.CustomerId.Value,
                    order.Status.ToString(),
                    order.Total.Amount,
                    order.Total.Currency,
                    order.Items.Count,
                    order.CreatedAt))
                .ToList();

            return Task.FromResult<IReadOnlyList<OrderSummaryDto>>(summaries);
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryIdempotencyStore : IIdempotencyStore
    {
        private readonly ConcurrentDictionary<string, object?> _entries = new();

        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
        {
            if (_entries.TryGetValue(key, out var value))
            {
                return Task.FromResult((T?)value);
            }

            return Task.FromResult(default(T));
        }

        public Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken = default)
        {
            _entries[key] = value;
            return Task.CompletedTask;
        }
    }
}
