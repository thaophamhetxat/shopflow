// File này chứa implementation repository cho Order trong Infrastructure của OrderService.
// Tạo ở Day 42 để đưa lưu trữ Aggregate Order xuống PostgreSQL thật bằng EF Core, phục vụ việc đọc lại order trong Day 45-47.
// Day 46 bổ sung read model detail/list; Day 60 bảo đảm mỗi query đọc chỉ chạy một SQL command và map các value object ở memory.

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
/// Read model detail/list dùng AsNoTracking và map các giá trị không dịch được sang SQL ở memory sau khi dữ liệu đã materialize.
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
        // Saga và CancelOrder sửa aggregate được load ở đây, vì vậy entity phải được EF theo dõi.
        return await _dbContext.Orders
            .Include(order => order.Items)
            .FirstOrDefaultAsync(order => order.Id == id, cancellationToken);
    }

    /// <summary>
    /// Đọc order cùng collection items bằng một query không tracking, rồi map DTO trong memory theo mapping Money của hệ thống.
    /// Trả về null khi không tồn tại order với id tương ứng.
    /// </summary>
    /// <param name="id">OrderId cần tìm.</param>
    /// <param name="cancellationToken">Token hủy nếu request bị hủy.</param>
    /// <returns>OrderDetailDto nếu order tồn tại, otherwise null.</returns>
    public async Task<OrderDetailDto?> GetOrderDetailAsync(OrderId id, CancellationToken cancellationToken = default)
    {
        var order = await _dbContext.Orders
            .AsNoTracking()
            .Include(currentOrder => currentOrder.Items)
            .FirstOrDefaultAsync(currentOrder => currentOrder.Id == id, cancellationToken);

        if (order is null)
        {
            return null;
        }

        var items = order.Items
            .OrderBy(item => item.ProductId)
            .ToList();

        var mappedItems = items
            .Select(item => new OrderDetailItemDto(
                item.ProductId,
                item.Quantity,
                item.UnitPrice.Amount,
                item.UnitPrice.Currency,
                item.LineTotal.Amount))
            .ToList();

        // Money được DB map thành chuỗi duy nhất `amount|currency`, `LineTotal` bị Ignore và `Status` map sang int nên không thể dịch `Total`/`LineTotal`/`ToString()` về SQL.
        // Do đó phần map DTO còn lại phải làm ở memory sau khi query `orders` theo Id đã materialize cùng với `Items` trong một câu SQL duy nhất.
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

        var query = _dbContext.Orders
            .AsNoTracking()
            .Include(currentOrder => currentOrder.Items)
            .Where(currentOrder => currentOrder.CustomerId == customerId);

        if (cursor is OrderId cursorOrderId)
        {
            // SQL chỉ so sánh keyset `CreatedAt` trong subquery này; tie-breaker `Guid.CompareTo` phải được lọc ở memory sau khi materialize.
            query = query.Where(currentOrder =>
                currentOrder.CreatedAt <= _dbContext.Orders
                    .AsNoTracking()
                    .Where(current => current.Id == cursorOrderId)
                    .Select(current => current.CreatedAt)
                    .FirstOrDefault());
        }

        var matchedOrders = await query
            .OrderByDescending(currentOrder => currentOrder.CreatedAt)
            .ThenByDescending(currentOrder => currentOrder.Id)
            .Take(boundedPageSize + 1)
            .ToListAsync(cancellationToken);

        if (cursor is OrderId cursorOrderIdForMemory)
        {
            var cursorOrder = matchedOrders.FirstOrDefault(currentOrder => currentOrder.Id == cursorOrderIdForMemory);
            if (cursorOrder is null)
            {
                return Array.Empty<OrderSummaryDto>();
            }

            matchedOrders = matchedOrders
                .Where(currentOrder => currentOrder.CreatedAt < cursorOrder.CreatedAt ||
                    (currentOrder.CreatedAt == cursorOrder.CreatedAt && currentOrder.Id.Value.CompareTo(cursorOrderIdForMemory.Value) < 0))
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
