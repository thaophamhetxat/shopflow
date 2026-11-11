using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OrderService.Application;
using OrderService.Application.Commands;
using OrderService.Application.Queries;
using OrderService.Infrastructure;
using OrderService.Infrastructure.Persistence;

var configuration = new ConfigurationBuilder()
    .AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["ConnectionStrings:OrderDb"] = "Host=localhost;Port=5432;Database=shopflow_order;Username=shopflow;Password=ShopFlow123!;",
        ["ConnectionStrings:Redis"] = "localhost:6379"
    })
    .Build();

var services = new ServiceCollection();
services.AddLogging();
services.AddApplication();
services.AddInfrastructure(configuration);

using var provider = services.BuildServiceProvider();
using (var scope = provider.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
    await db.Database.MigrateAsync();
    await db.Database.ExecuteSqlRawAsync("DELETE FROM outbox_messages;");
    await db.Database.ExecuteSqlRawAsync("DELETE FROM order_items;");
    await db.Database.ExecuteSqlRawAsync("DELETE FROM orders;");
}

var mediator = provider.GetRequiredService<MediatR.IMediator>();
var customerId = Guid.NewGuid();
var orderId = await mediator.Send(new PlaceOrderCommand(customerId,"Test","A","Viet Nam","100000",new List<PlaceOrderItem>{new(Guid.NewGuid(), 2, 25000m, "VND")},"probe"));
Console.WriteLine($"Placed: {orderId.Value}");
using (var scope = provider.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
    Console.WriteLine($"Orders: {await db.Orders.CountAsync()}");
    Console.WriteLine($"Items: {await db.Set<OrderService.Domain.Entities.OrderItem>().CountAsync()}");
}

var detail = await mediator.Send(new GetOrderByIdQuery(orderId.Value));
Console.WriteLine(detail is null ? "detail-null" : $"detail:{detail.OrderId} {detail.TotalAmount} {detail.Status} {detail.Items.Count}");
var page = await mediator.Send(new ListOrdersByCustomerQuery(customerId, null, 10));
Console.WriteLine($"page-count:{page.Count}");
foreach (var item in page)
{
    Console.WriteLine($"order:{item.OrderId} total:{item.TotalAmount} status:{item.Status}");
}