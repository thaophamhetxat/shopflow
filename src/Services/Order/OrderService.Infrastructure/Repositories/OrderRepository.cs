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
public sealed class OrderRepository : IOrderRepository
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
    public async Task<object?> GetOrderDetailAsync(OrderId id, CancellationToken cancellationToken = default)
    {
        var order = await _dbContext.Orders
            .AsNoTracking()
            .Include(currentOrder => currentOrder.Items)
            .SingleOrDefaultAsync(currentOrder => currentOrder.Id == id, cancellationToken);

        if (order is null)
        {
            return null;
        }

        var itemDtos = order.Items
            .Select(item => new OrderDetailItemDto(
                item.ProductId,
                item.Quantity,
                item.UnitPrice.Amount,
                item.UnitPrice.Currency,
                item.LineTotal.Amount))
            .ToList();

        var totalAmount = itemDtos.Sum(item => item.LineTotalAmount);
        var totalCurrency = itemDtos.FirstOrDefault()?.UnitPriceCurrency ?? string.Empty;

        return new OrderDetailDto(
            order.Id.Value,
            order.CustomerId.Value,
            order.Status.ToString(),
            totalAmount,
            totalCurrency,
            order.CreatedAt,
            itemDtos);
    }

    /// <summary>
    /// Đọc danh sách summary theo customer bằng keyset cursor, không dùng OFFSET vì sau trang đầu phía sau cần ổn định khi có đơn mới chèn vào giữa lúc phân trang.
    /// </summary>
    /// <param name="customerId">CustomerId cần lấy list order.</param>
    /// <param name="cursor">OrderId của đơn cuối trang trước, null nếu là trang đầu.</param>
    /// <param name="pageSize">Kích thước trang tối đa, được clamp ở handler và trong method này để giữ an toàn.</param>
    /// <param name="cancellationToken">Token hủy nếu request bị hủy.</param>
    /// <returns>Danh sách OrderSummaryDto theo thứ tự CreatedAt giảm dần, OrderId giảm dần, tối đa pageSize item.</returns>
    public async Task<IReadOnlyList<object>> ListOrderSummariesByCustomerAsync(CustomerId customerId, OrderId? cursor, int pageSize, CancellationToken cancellationToken = default)
    {
        var boundedPageSize = Math.Clamp(pageSize, 1, 50);

        IQueryable<Order> query = _dbContext.Orders
            .AsNoTracking()
            .Where(currentOrder => currentOrder.CustomerId == customerId)
            .OrderByDescending(currentOrder => currentOrder.CreatedAt)
            .ThenByDescending(currentOrder => currentOrder.Id);

        DateTime? cursorCreatedAt = null;
        OrderId? cursorOrderIdForFiltering = null;
        if (cursor is OrderId cursorOrderId)
        {
            cursorOrderIdForFiltering = cursorOrderId;

            // Keyset thay cho OFFSET: cursor là OrderId của đơn cuối trang trước, trang sau chỉ lấy đơn "nhỏ hơn" cursor theo thứ tự sắp xếp, không trùng đơn trước đó.
            // Cách này tránh lệch dữ liệu khi có đơn mới được insert vào khoảng giữa các trang và không cần dùng Skip/Take kiểu OFFSET.
            cursorCreatedAt = await _dbContext.Orders
                .AsNoTracking()
                .Where(currentOrder => currentOrder.Id == cursorOrderId)
                .Select(currentOrder => currentOrder.CreatedAt)
                .SingleOrDefaultAsync(cancellationToken);
        }

        var orders = await query
            .Include(currentOrder => currentOrder.Items)
            .ToListAsync(cancellationToken);

        if (cursorOrderIdForFiltering is OrderId effectiveCursorOrderId && cursorCreatedAt.HasValue)
        {
            orders = orders
                .Where(currentOrder =>
                    currentOrder.CreatedAt < cursorCreatedAt.Value ||
                    (currentOrder.CreatedAt == cursorCreatedAt.Value && currentOrder.Id.Value < effectiveCursorOrderId.Value))
                .ToList();
        }

        var orderSummaries = orders
            .Take(boundedPageSize)
            .ToList();

        if (orderSummaries.Count == 0)
        {
            return Array.Empty<object>();
        }

        return orderSummaries
            .Select(currentOrder =>
            {
                var itemCount = currentOrder.Items.Count;
                var totalAmount = currentOrder.Items.Sum(item => item.LineTotal.Amount);
                var totalCurrency = currentOrder.Items.FirstOrDefault()?.UnitPrice.Currency ?? string.Empty;

                return (object)new OrderSummaryDto(
                    currentOrder.Id.Value,
                    currentOrder.CustomerId.Value,
                    currentOrder.Status.ToString(),
                    totalAmount,
                    totalCurrency,
                    itemCount,
                    currentOrder.CreatedAt);
            })
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
