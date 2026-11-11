using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OrderService.Application.Sagas;
using OrderService.Infrastructure.Sagas;
using Xunit;

namespace OrderService.Infrastructure.Tests;

public sealed class DependencyInjectionTests
{
    // Test này chứng minh fix Day 53: container phải đăng ký `ISagaReservationRepository` thực tế để saga lưu row `saga_reservations` trong DB,
    // chứ không được rơi lại vào fallback `new InMemorySagaReservationRepository()` của constructor 5 tham số khi chạy ngoài môi trường unit test.
    [Fact]
    public void AddInfrastructure_RegistersSagaReservationRepository()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(BuildConfiguration());

        using var provider = services.BuildServiceProvider();
        var repository = provider.GetRequiredService<ISagaReservationRepository>();

        Assert.NotNull(repository);
        Assert.IsType<SagaReservationRepository>(repository);
    }

    // Test này chứng minh fix Day 53: khi resolve `IOrderSaga` từ container, saga phải giữ repository đã đăng ký thật,
    // vì nếu không thì constructor 5 tham số sẽ dùng repo in-memory và retry/crash sau đó không đọc được row DB. Test cũ không bắt được vì chúng mock trực tiếp repository.
    [Fact]
    public void ResolveOrderSaga_UsesRegisteredSagaReservationRepository()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(BuildConfiguration());

        using var provider = services.BuildServiceProvider();
        var registeredRepository = provider.GetRequiredService<ISagaReservationRepository>();
        var saga = provider.GetRequiredService<IOrderSaga>();

        var field = typeof(OrderSaga).GetField("_sagaReservationRepository", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);

        var sagaRepository = field!.GetValue(saga);
        Assert.NotNull(sagaRepository);
        Assert.Same(registeredRepository, sagaRepository);
        Assert.IsType<SagaReservationRepository>(sagaRepository);
    }

    private static IConfiguration BuildConfiguration()
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:OrderDb"] = "Host=localhost;Port=5432;Database=shopflow_order;Username=shopflow;Password=shopflow;",
                ["ConnectionStrings:Redis"] = "localhost:6379"
            })
            .Build();
    }
}
