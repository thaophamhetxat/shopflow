// File này chứa EF Core DbContext cho OrderService.Infrastructure.
// Tạo ở Day 42 để map Aggregate Order và OrderItem sang PostgreSQL, phục vụ lưu trữ thực tế cho Day 45-47.
// Layer Infrastructure này chịu trách nhiệm cấu hình model và migrations, không chứa nghiệp vụ giống như Application/Domain.

using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderService.Application.Sagas;
using OrderService.Domain.Aggregates;
using OrderService.Domain.Entities;
using OrderService.Domain.Enums;
using OrderService.Domain.ValueObjects;

namespace OrderService.Infrastructure.Persistence;

/// <summary>
/// DbContext cho OrderService, chịu trách nhiệm ánh xạ Aggregate Order sang table orders và collection items sang table order_items trong PostgreSQL.
/// </summary>
public sealed class OrderDbContext : DbContext
{
    /// <summary>
    /// Khởi tạo DbContext với options đã được cấu hình bởi DI hoặc design time factory.
    /// </summary>
    /// <param name="options">Options để EF Core biết provider, connection string và các cài đặt.</param>
    public OrderDbContext(DbContextOptions<OrderDbContext> options)
        : base(options)
    {
    }

    /// <summary>
    /// Set chứa toàn bộ Order aggregate đang được lưu trong database.
    /// </summary>
    public DbSet<Order> Orders => Set<Order>();

    /// <summary>
    /// Set chứa các message đã được ghi vào outbox để chờ processor phát đi sau khi Order được lưu thành công.
    /// </summary>
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    /// <summary>
    /// Set lưu trạng thái công việc của Saga theo từng order để worker có thể recover sau restart.
    /// </summary>
    public DbSet<SagaState> SagaStates => Set<SagaState>();

    /// <summary>
    /// Set lưu từng item đã reserve thành công trong order để cleanup idempotent và skip double-reserve nếu saga crash giữa chừng.
    /// </summary>
    public DbSet<SagaReservation> SagaReservations => Set<SagaReservation>();

    /// <summary>
    /// Set lưu stream event của từng order, phục vụ event sourcing và replay aggregate từ log sự kiện.
    /// </summary>
    public DbSet<OrderEvent> OrderEvents => Set<OrderEvent>();

    /// <summary>
    /// Set lưu snapshot định kỳ của aggregate Order để giảm chi phí replay khi stream dài.
    /// </summary>
    public DbSet<OrderSnapshot> OrderSnapshots => Set<OrderSnapshot>();

    /// <summary>
    /// Ghi sự kiện domain ra outbox và event store trong cùng transaction với Order để đảm bảo dữ liệu không bị lệch giữa lưu đơn, lưu stream và publish downstream.
    /// </summary>
    /// <param name="cancellationToken">Token hủy khi `SaveChangesAsync` đang chạy.</param>
    /// <returns>Số lượng bản ghi đã được persist trong transaction này.</returns>
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var trackedOrders = ChangeTracker.Entries<Order>().ToList();

        foreach (var entry in trackedOrders)
        {
            var newEvents = entry.Entity.DomainEvents.ToList();
            if (newEvents.Count == 0)
            {
                continue;
            }

            var expectedVersion = entry.Entity.Version - newEvents.Count;
            var eventStore = new EventStore(this);
            await eventStore.AppendAsync(entry.Entity.Id, newEvents, expectedVersion, cancellationToken);

            foreach (var domainEvent in newEvents)
            {
                OutboxMessages.Add(OutboxMessage.Create(entry.Entity.Id.ToString(), entry.Entity.GetType().Name, domainEvent));
            }
        }

        var result = await base.SaveChangesAsync(cancellationToken);

        foreach (var entry in ChangeTracker.Entries<Order>())
        {
            entry.Entity.ClearDomainEvents();
        }

        return result;
    }

    /// <summary>
    /// Cấu hình model EF Core cho Order aggregate, outbox messages và các value object/owned entity bên trong.
    /// </summary>
    /// <param name="modelBuilder">Builder dùng để khai báo mapping và index.</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<OutboxMessage>(outboxBuilder =>
        {
            outboxBuilder.ToTable("outbox_messages");
            outboxBuilder.HasKey(x => x.Id);

            outboxBuilder.Property(x => x.Id)
                .HasColumnName("id")
                .ValueGeneratedNever();

            outboxBuilder.Property(x => x.AggregateType)
                .HasColumnName("aggregate_type")
                .IsRequired();

            outboxBuilder.Property(x => x.AggregateId)
                .HasColumnName("aggregate_id")
                .IsRequired();

            outboxBuilder.Property(x => x.EventType)
                .HasColumnName("event_type")
                .IsRequired();

            outboxBuilder.Property(x => x.EventData)
                .HasColumnName("event_data")
                .IsRequired();

            outboxBuilder.Property(x => x.OccurredOn)
                .HasColumnName("occurred_on")
                .IsRequired();

            outboxBuilder.Property(x => x.ProcessedAt)
                .HasColumnName("processed_at")
                .IsRequired(false);

            outboxBuilder.HasIndex(x => x.ProcessedAt)
                .HasDatabaseName("ix_outbox_messages_processed_at");
        });

        modelBuilder.Entity<SagaState>(sagaBuilder =>
        {
            sagaBuilder.ToTable("saga_state");
            sagaBuilder.HasKey(x => x.OrderId);

            sagaBuilder.Property(x => x.OrderId)
                .HasColumnName("order_id")
                .ValueGeneratedNever();

            sagaBuilder.Property(x => x.State)
                .HasColumnName("state")
                .IsRequired();

            sagaBuilder.Property(x => x.ErrorMessage)
                .HasColumnName("error_message")
                .IsRequired(false);

            sagaBuilder.Property(x => x.CreatedAt)
                .HasColumnName("created_at")
                .IsRequired();

            sagaBuilder.Property(x => x.UpdatedAt)
                .HasColumnName("updated_at")
                .IsRequired();
        });

        modelBuilder.Entity<SagaReservation>(reservationBuilder =>
        {
            reservationBuilder.ToTable("saga_reservations");
            reservationBuilder.HasKey(x => new { x.OrderId, x.ProductId });

            reservationBuilder.Property(x => x.OrderId)
                .HasColumnName("order_id")
                .ValueGeneratedNever();

            reservationBuilder.Property(x => x.ProductId)
                .HasColumnName("product_id")
                .ValueGeneratedNever();

            reservationBuilder.Property(x => x.Quantity)
                .HasColumnName("quantity")
                .IsRequired();

            reservationBuilder.Property(x => x.Status)
                .HasColumnName("status")
                .IsRequired()
                .HasConversion(
                    status => (int)status,
                    value => (SagaReservationStatus)value);

            reservationBuilder.Property(x => x.CreatedAt)
                .HasColumnName("created_at")
                .IsRequired();

            reservationBuilder.Property(x => x.UpdatedAt)
                .HasColumnName("updated_at")
                .IsRequired();
        });

        modelBuilder.Entity<OrderEvent>(eventBuilder =>
        {
            eventBuilder.ToTable("order_events");
            eventBuilder.HasKey(x => new { x.OrderId, x.Version });

            eventBuilder.Property(x => x.OrderId)
                .HasColumnName("order_id")
                .ValueGeneratedNever();

            eventBuilder.Property(x => x.Version)
                .HasColumnName("version")
                .IsRequired();

            eventBuilder.Property(x => x.EventType)
                .HasColumnName("event_type")
                .IsRequired();

            eventBuilder.Property(x => x.EventData)
                .HasColumnName("event_data")
                .IsRequired();

            eventBuilder.Property(x => x.OccurredOn)
                .HasColumnName("occurred_on")
                .IsRequired();
        });

        modelBuilder.Entity<OrderSnapshot>(snapshotBuilder =>
        {
            snapshotBuilder.ToTable("order_snapshots");
            snapshotBuilder.HasKey(x => x.OrderId);

            snapshotBuilder.Property(x => x.OrderId)
                .HasColumnName("order_id")
                .ValueGeneratedNever();

            snapshotBuilder.Property(x => x.Version)
                .HasColumnName("version")
                .IsRequired();

            snapshotBuilder.Property(x => x.SnapshotData)
                .HasColumnName("snapshot_data")
                .IsRequired();

            snapshotBuilder.Property(x => x.CreatedAt)
                .HasColumnName("created_at")
                .IsRequired();
        });

        EntityTypeBuilder<Order> orderBuilder = modelBuilder.Entity<Order>();
        orderBuilder.ToTable("orders");
        orderBuilder.HasKey(x => x.Id);

        // OrderId là value object bọc Guid nên cần converter để lưu xuống cột UUID của PostgreSQL, tránh lưu chuỗi nhầm định dạng.
        orderBuilder.Property(x => x.Id)
            .HasConversion(
                id => id.Value,
                value => OrderId.From(value))
            .HasColumnName("id")
            .ValueGeneratedNever();

        // CustomerId là identity của người mua, cần lưu trong cột riêng để query theo khách hàng trong future flow và đặt index từ đầu.
        orderBuilder.Property(x => x.CustomerId)
            .HasConversion(
                customerId => customerId.Value,
                value => CustomerId.From(value))
            .HasColumnName("customer_id");

        // ShippingAddress là value object gồm street/city/country/postalCode thuộc cùng một aggregate, nên lưu bằng converter thành chuỗi để tránh tạo bảng riêng cho địa chỉ.
        orderBuilder.Property(x => x.ShippingAddress)
            .HasConversion(
                address => SerializeAddress(address),
                value => DeserializeAddress(value))
            .HasColumnName("shipping_address");

        // Status là enum theo trạng thái nghiệp vụ của order, chuyển sang int trong DB để PostgreSQL dễ filter và index trên status.
        orderBuilder.Property(x => x.Status)
            .HasField("_status")
            .HasColumnName("status")
            .HasConversion(
                status => (int)status,
                value => (OrderStatus)value)
            .IsRequired();

        // CreatedAt lưu thời điểm tạo để hỗ trợ truy vấn theo thời gian và timeline cho các day sau; index trên timestamp giúp lấy tập dữ liệu theo ngày/ngày tạo.
        orderBuilder.Property(x => x.CreatedAt)
            .HasField("_createdAt")
            .HasColumnName("created_at")
            .IsRequired();

        // Version của stream event phải được lưu xuống cột `version` để EF load lại aggregate theo đúng stream state và phát hiện xung đột khi dua writer append cùng lúc.
        orderBuilder.Property(x => x.Version)
            .HasColumnName("version")
            .IsRequired();

        // OrderItem là owned-entity: nó không có đời sống riêng ngoài Order, xóa Order là xóa luôn item, nên map như một collection có khóa ngoại về phía Order.
        orderBuilder.HasMany(x => x.Items)
            .WithOne()
            .HasForeignKey("OrderId")
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<OrderItem>(itemBuilder =>
        {
            itemBuilder.ToTable("order_items");
            itemBuilder.HasKey(x => x.Id);

            itemBuilder.Property(x => x.Id)
                .HasColumnName("id")
                .ValueGeneratedNever();

            itemBuilder.Property(x => x.ProductId).HasColumnName("product_id");
            itemBuilder.Property(x => x.Quantity).HasColumnName("quantity");

            // UnitPrice là Money value object, nên lưu bằng converter thành chuỗi gọn trong cùng bảng order_items để không cần table riêng cho mỗi đơn vị tiền.
            itemBuilder.Property(x => x.UnitPrice)
                .HasConversion(
                    price => SerializeMoney(price),
                    value => DeserializeMoney(value))
                .HasColumnName("unit_price");

            itemBuilder.Ignore(x => x.LineTotal);
        });

        // Total là giá trị tính toán từ Items; lưu xuống DB sẽ lệch khi Items đổi nên chỉ tính lúc đọc, không cần mapping cột lưu trữ.
        orderBuilder.Ignore(x => x.Total);
        orderBuilder.Ignore(x => x.DomainEvents);

        // Index CustomerId: Day 46 sẽ query ListOrdersByCustomer theo CustomerId nên phải có index từ đầu.
        orderBuilder.HasIndex(x => x.CustomerId).HasDatabaseName("ix_orders_customer_id");

        // Index Status: lọc theo trạng thái là nhu cầu rất phổ biến cho dashboard, cần index ngay khi Order được lưu.
        orderBuilder.HasIndex(x => x.Status).HasDatabaseName("ix_orders_status");

        // Index CreatedAt: sắp xếp và lọc theo thời gian tạo nhằm phục vụ báo cáo và timeline ở các day sau.
        orderBuilder.HasIndex(x => x.CreatedAt).HasDatabaseName("ix_orders_created_at");

        // Index keyset composite cho list order theo customer: customerId làm bộ lọc chính, CreatedAt giảm dần làm key chính, Id giảm dần làm tie-breaker khi cùng timestamp.
        orderBuilder.HasIndex(x => new { x.CustomerId, x.CreatedAt, x.Id })
            .HasDatabaseName("ix_orders_customer_created_at_id")
            .IsDescending(false, true, true);
    }

    private static string SerializeAddress(Address address)
    {
        return string.Join("||", address.Street, address.City, address.Country, address.PostalCode ?? string.Empty);
    }

    private static Address DeserializeAddress(string value)
    {
        var parts = value.Split("||", StringSplitOptions.None);
        if (parts.Length < 3)
        {
            throw new InvalidOperationException("Dữ liệu địa chỉ không hợp lệ để deserialize.");
        }

        return new Address(parts[0], parts[1], parts[2], parts.Length > 3 ? parts[3] : null);
    }

    private static string SerializeMoney(Money money)
    {
        return string.Join(";", money.Amount.ToString(CultureInfo.InvariantCulture), money.Currency);
    }

    private static Money DeserializeMoney(string value)
    {
        var parts = value.Split(';', StringSplitOptions.None);
        if (parts.Length != 2)
        {
            throw new InvalidOperationException("Dữ liệu tiền tệ không hợp lệ để deserialize.");
        }

        return new Money(decimal.Parse(parts[0], CultureInfo.InvariantCulture), parts[1]);
    }
}
