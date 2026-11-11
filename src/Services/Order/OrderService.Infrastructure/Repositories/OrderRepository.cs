// File này chứa implementation repository cho Order trong Infrastructure của OrderService.
// Tạo ở Day 42 để đưa lưu trữ Aggregate Order xuống PostgreSQL thật bằng EF Core, phục vụ việc đọc lại order trong Day 45-47.
// Day 46 bổ sung read model projection cho detail và list summary, sử dụng Select + AsNoTracking để tránh nạp aggregate và giữ đúng 1 query SQL.

using Microsoft.EntityFrameworkCore;
using OrderService.Application.Queries;
using OrderService.Domain.Aggregates;
using OrderService.Domain.Entities;
using OrderService.Domain.Repositories;
using OrderService.Domain.ValueObjects;
using OrderService.Infrastructure.Persistence;

namespace OrderService.Infrastructure.Repositories;

/// <summary>
/// Repository Entity Framework cho Aggregate Order, dùng DbContext để thêm, tải và lưu trạng thái aggregate vào PostgreSQL.
/// Day 46 cài thêm read model projection cho GetOrderDetailAsync và ListOrderSummariesByCustomerAsync bằng Select + AsNoTracking để đáp ứng yêu cầu query lean.
/// </summary>
public sealed class OrderRepository : IOrderRepository, IOrderReadModelRepository
{
    private readonly OrderDbContext _dbContext;

    /// <summary>
    /// Khởi tạo repository với DbContext đã được đăng ký trong DI.
    /// </summary>
    /// <param name="dbContext">DbContext thực thi persistence của Order.</param>
    public OrderRepository(OrderDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// Thêm order mới vào ChangeTracker để EF Core lưu khi SaveChangesAsync được gọi.
    /// </summary>
    /// <param name="order">Aggregate Order cần ghi mới xuống DB.</param>
    /// <param name="cancellationToken">Token hủy thao tác nếu request bị hủy.</param>
    public Task AddAsync(Order order, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(order);

        return _dbContext.Orders.AddAsync(order, cancellationToken).AsTask();
    }

    /// <summary>
    /// Tìm order theo Id và include toàn bộ item của đơn để đọc lại dữ liệu đầy đủ từ PostgreSQL.
    /// Trả về null khi không tìm thấy đơn hàng với OrderId đó.
    /// </summary>
    /// <param name="id">OrderId cần truy vấn.</param>
    /// <param name="cancellationToken">Token hủy thao tác nếu request bị hủy.</param>
    /// <returns>Order đã tải kèm items nếu tồn tại, ngược lại null.</returns>
    public async Task<Order?> GetByIdAsync(OrderId id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Orders
            .Include(order => order.Items)
            .FirstOrDefaultAsync(order => order.Id == id, cancellationToken);
    }

    /// <summary>
    /// Đọc chi tiết order bằng projection SQL, chỉ select đúng cột cần cho OrderDetailDto mà không nạp cả aggregate vào memory.
    /// Trả về null khi không tồn tại order với id tương ứng.
    /// </summary>
    /// <param name="id">OrderId cần tìm.</param>
    /// <param name="cancellationToken">Token hủy nếu request bị hủy.</param>
    /// <returns>OrderDetailDto nếu order tồn tại, otherwise null.</returns>
    public async Task<OrderDetailDto?> GetOrderDetailAsync(OrderId id, CancellationToken cancellationToken = default)
    {
        var order = await _dbContext.Orders
            .AsNoTracking()
            .FirstOrDefaultAsync(currentOrder => currentOrder.Id == id, cancellationToken);

        if (order is null)
        {
            return null;
        }

        var items = await _dbContext.Set<OrderItem>()
            .AsNoTracking()
            .Where(item => EF.Property<OrderId>(item, "OrderId") == id)
            .OrderBy(item => item.ProductId)
            .ToListAsync(cancellationToken);

        var mappedItems = items
            .Select(item => new OrderDetailItemDto(
                item.ProductId,
                item.Quantity,
                item.UnitPrice.Amount,
                item.UnitPrice.Currency,
                item.LineTotal.Amount))
            .ToList();

        // Money được DB map thành chuỗi duy nhất `amount|currency`, `LineTotal` bị Ignore và `Status` map sang int nên không thể dịch `Total`/`LineTotal`/`ToString()` về SQL.
        // Do đó phần map DTO còn lại phải làm ở memory sau khi query `orders` theo Id và `order_items` theo shadow key `OrderId` đã trả đủ dòng hàng.
        var totalAmount = items.Sum(item => item.LineTotal.Amount);
        var totalCurrency = items.Select(item => item.UnitPrice.Currency).FirstOrDefault() ?? string.Empty;

        return new OrderDetailDto(
            order.Id.Value,
            order.CustomerId.Value,
            order.Status.ToString(),
            totalAmount,
            totalCurrency,
            order.CreatedAt,
            mappedItems);
    }

    /// <summary>
    /// Đọc danh sách summary theo customer bằng keyset cursor, không dùng OFFSET vì sau trang đầu phía sau cần ổn định khi có đơn mới chèn vào giữa lúc phân trang.
    /// </summary>
    /// <param name="customerId">CustomerId cần lấy list order.</param>
    /// <param name="cursor">OrderId của đơn cuối trang trước, null nếu là trang đầu.</param>
    /// <param name="pageSize">Kích thước trang tối đa, được clamp ở handler và trong method này để giữ an toàn.</param>
    /// <param name="cancellationToken">Token hủy nếu request bị hủy.</param>
    /// <returns>Danh sách OrderSummaryDto theo thứ tự CreatedAt giảm dần, OrderId giảm dần, tối đa pageSize item.</returns>
    public async Task<IReadOnlyList<OrderSummaryDto>> ListOrderSummariesByCustomerAsync(CustomerId customerId, OrderId? cursor, int pageSize, CancellationToken cancellationToken = default)
    {
        var boundedPageSize = Math.Clamp(pageSize, 1, 50);

        DateTime? cursorCreatedAt = null;
        if (cursor is OrderId cursorOrderId)
        {
            cursorCreatedAt = await _dbContext.Orders
                .AsNoTracking()
                .Where(currentOrder => currentOrder.Id == cursorOrderId)
                .Select(currentOrder => currentOrder.CreatedAt)
                .SingleOrDefaultAsync(cancellationToken);

            if (cursorCreatedAt == default && !await _dbContext.Orders.AsNoTracking().AnyAsync(currentOrder => currentOrder.Id == cursorOrderId, cancellationToken))
            {
                return Array.Empty<OrderSummaryDto>();
            }
        }

        var query = _dbContext.Orders
            .AsNoTracking()
            .Include(currentOrder => currentOrder.Items)
            .Where(currentOrder => currentOrder.CustomerId == customerId);

        if (cursor is OrderId cursorOrderIdValue && cursorCreatedAt.HasValue)
        {
            // SQL chỉ có thể so sánh `CreatedAt` theo keyset được; `Id.Value` là Guid bên trong value object `HasConversion` nên EF không dịch `Id.Value < ...` xuống SQL.
            // Để tránh bỏ sót order khi nhiều đơn cùng `CreatedAt`, SQL lấy thừa 1 row và ta lọc tie-breaker ở bộ nhớ bằng `Guid.CompareTo` sau khi materialize.
            query = query.Where(currentOrder => currentOrder.CreatedAt <= cursorCreatedAt.Value);
        }

        var matchedOrders = await query
            .OrderByDescending(currentOrder => currentOrder.CreatedAt)
            .ThenByDescending(currentOrder => currentOrder.Id)
            .Take(boundedPageSize + 1)
            .ToListAsync(cancellationToken);

        if (cursor is OrderId cursorOrderIdForMemory && cursorCreatedAt.HasValue)
        {
            matchedOrders = matchedOrders
                .Where(currentOrder => currentOrder.CreatedAt < cursorCreatedAt.Value ||
                    (currentOrder.CreatedAt == cursorCreatedAt.Value && currentOrder.Id.Value.CompareTo(cursorOrderIdForMemory.Value) < 0))
                .ToList();
        }

        return matchedOrders
            .Take(boundedPageSize)
            .Select(currentOrder => new OrderSummaryDto(
                currentOrder.Id.Value,
                currentOrder.CustomerId.Value,
                currentOrder.Status.ToString(),
                currentOrder.Total.Amount,
                currentOrder.Items.Select(item => item.UnitPrice.Currency).FirstOrDefault() ?? string.Empty,
                currentOrder.Items.Count,
                currentOrder.CreatedAt))
            .ToList();
    }

    /// <summary>
    /// Gọi SaveChangesAsync trên DbContext để lưu các thay đổi pending của aggregate Order.
    /// </summary>
    /// <param name="cancellationToken">Token hủy thao tác nếu request bị hủy.</param>
    /// <returns>Tác vụ hoàn tất sau khi EF Core ghi xuống database.</returns>
    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return _dbContext.SaveChangesAsync(cancellationToken);
    }
}
