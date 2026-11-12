// File này triển khai event store cho OrderService.Infrastructure trên PostgreSQL.
// Day 55 tạo snapshot/replay; Day 60 xác nhận các truy vấn đọc event và snapshot không tracking.
// Snapshot được tracking riêng trong AppendAsync khi cần cập nhật trong đường ghi.

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OrderService.Domain;
using OrderService.Domain.Aggregates;
using OrderService.Domain.Enums;
using OrderService.Domain.Events;
using OrderService.Domain.Exceptions;
using OrderService.Domain.Repositories;
using OrderService.Domain.ValueObjects;

namespace OrderService.Infrastructure.Persistence;

/// <summary>
/// Event store cho OrderService lưu event stream vào bảng `order_events` và snapshot định kỳ để tái dựng order nhanh hơn.
/// Snapshot chỉ là optimization, không phải source of truth; replay event stream vẫn là cách duy nhất để xác định trạng thái cuối cùng.
/// </summary>
public sealed class EventStore : IEventStore
{
    private readonly OrderDbContext _dbContext;

    public EventStore(OrderDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AppendAsync(OrderId orderId, IReadOnlyCollection<IDomainEvent> events, int expectedVersion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(events);

        var currentVersion = await _dbContext.OrderEvents
            .AsNoTracking()
            .Where(item => item.OrderId == orderId.Value)
            .Select(item => (int?)item.Version)
            .DefaultIfEmpty()
            .MaxAsync(cancellationToken);

        var actualVersion = currentVersion ?? 0;
        if (actualVersion != expectedVersion)
        {
            throw new ConcurrencyException($"Xung đột optimistic concurrency: order '{orderId}' yêu cầu expectedVersion = {expectedVersion} nhưng version thực tế hiện tại là {actualVersion}.");
        }

        if (events.Count == 0)
        {
            return;
        }

        var eventList = events.ToList();
        var nextVersion = expectedVersion;
        var stagedRows = new List<OrderEvent>();

        foreach (var domainEvent in eventList)
        {
            nextVersion++;
            stagedRows.Add(new OrderEvent
            {
                OrderId = orderId.Value,
                Version = nextVersion,
                EventType = domainEvent.GetType().FullName ?? domainEvent.GetType().Name,
                EventData = JsonSerializer.Serialize(domainEvent, domainEvent.GetType(), OrderJsonSerializerOptions.Default),
                OccurredOn = GetOccurredOn(domainEvent)
            });
        }

        _dbContext.OrderEvents.AddRange(stagedRows);

        if (nextVersion % 10 == 0)
        {
            var trackedOrder = _dbContext.ChangeTracker.Entries<Order>()
                .FirstOrDefault(entry => entry.Entity.Id == orderId)?.Entity;
            var order = trackedOrder ?? await LoadAsync(orderId, cancellationToken);
            if (order is not null)
            {
                var snapshotData = OrderSnapshotData.FromOrder(order);
                // Snapshot được cập nhật hoặc thêm ngay sau lần đọc này nên cần tracking trong đường ghi AppendAsync.
                var snapshot = await _dbContext.OrderSnapshots.FirstOrDefaultAsync(item => item.OrderId == orderId.Value, cancellationToken);
                if (snapshot is null)
                {
                    _dbContext.OrderSnapshots.Add(new OrderSnapshot
                    {
                        OrderId = orderId.Value,
                        Version = order.Version,
                        SnapshotData = JsonSerializer.Serialize(snapshotData, OrderJsonSerializerOptions.Default),
                        CreatedAt = DateTime.UtcNow
                    });
                }
                else
                {
                    snapshot.Version = order.Version;
                    snapshot.SnapshotData = JsonSerializer.Serialize(snapshotData, OrderJsonSerializerOptions.Default);
                    snapshot.CreatedAt = DateTime.UtcNow;
                }
            }
        }
    }

    public async Task<Order?> LoadAsync(OrderId orderId, CancellationToken cancellationToken = default)
    {
        var latestSnapshot = await _dbContext.OrderSnapshots
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.OrderId == orderId.Value, cancellationToken);

        Order? order = null;
        var fromVersion = 0;

        if (latestSnapshot is not null)
        {
            var snapshotData = JsonSerializer.Deserialize<OrderSnapshotData>(latestSnapshot.SnapshotData, OrderJsonSerializerOptions.Default);
            order = Order.FromSnapshot(snapshotData!);
            fromVersion = latestSnapshot.Version;
        }

        var pendingEvents = await GetEventsAsync(orderId, fromVersion, cancellationToken);
        foreach (var domainEvent in pendingEvents)
        {
            if (order is null)
            {
                order = RehydrateFromFirstEvent(domainEvent);
            }
            else
            {
                order.Apply(domainEvent);
            }
        }

        return order;
    }

    public async Task<IReadOnlyList<IDomainEvent>> GetEventsAsync(OrderId orderId, int fromVersion, CancellationToken cancellationToken = default)
    {
        var rows = await _dbContext.OrderEvents
            .AsNoTracking()
            .Where(item => item.OrderId == orderId.Value && item.Version > fromVersion)
            .OrderBy(item => item.Version)
            .ToListAsync(cancellationToken);

        var events = new List<IDomainEvent>(rows.Count);
        foreach (var row in rows)
        {
            var eventType = ResolveEventType(row.EventType);
            var deserialized = JsonSerializer.Deserialize(row.EventData, eventType, OrderJsonSerializerOptions.Default);
            if (deserialized is not IDomainEvent domainEvent)
            {
                throw new OrderDomainException($"Dữ liệu event '{row.EventType}' không deserialize được thành IDomainEvent hợp lệ.");
            }

            events.Add(domainEvent);
        }

        return events;
    }

    private static Order RehydrateFromFirstEvent(IDomainEvent domainEvent)
    {
        return domainEvent switch
        {
            OrderPlacedEvent placedEvent => Order.FromSnapshot(new OrderSnapshotData(
                placedEvent.OrderId.Value,
                placedEvent.CustomerId.Value,
                placedEvent.Items,
                placedEvent.ShippingAddress,
                OrderStatus.Pending,
                placedEvent.Total.Amount,
                placedEvent.Total.Currency,
                1,
                placedEvent.OccurredOn)),
            _ => throw new OrderDomainException($"Không thể khởi tạo aggregate từ event '{domainEvent.GetType().FullName}' vì nó không phải OrderPlacedEvent.")
        };
    }

    private static Type ResolveEventType(string eventType)
    {
        var type = Type.GetType(eventType, throwOnError: false);
        if (type is not null)
        {
            return type;
        }

        type = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(assembly => assembly.GetTypes())
            .FirstOrDefault(candidate => string.Equals(candidate.FullName, eventType, StringComparison.Ordinal)
                || string.Equals(candidate.Name, eventType, StringComparison.Ordinal));

        if (type is null)
        {
            throw new OrderDomainException($"Không hỗ trợ deserialize event type '{eventType}' trong event store.");
        }

        return type;
    }

    private static DateTime GetOccurredOn(IDomainEvent domainEvent)
    {
        var property = domainEvent.GetType().GetProperty(nameof(OrderPlacedEvent.OccurredOn));
        if (property is null)
        {
            return DateTime.UtcNow;
        }

        var value = property.GetValue(domainEvent);
        return value is DateTime dateTime ? dateTime : DateTime.UtcNow;
    }
}
