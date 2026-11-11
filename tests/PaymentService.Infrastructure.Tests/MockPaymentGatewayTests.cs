// File này kiểm thử hai nhánh kết quả của MockPaymentGateway.
// Tạo ở Day 48 để cung cấp Evidence rằng gateway sinh cả kết quả thành công lẫn thất bại.
// Test dùng Random seed cố định, không cần database và không gọi HTTP thật.

using Microsoft.Extensions.Logging.Abstractions;
using PaymentService.Infrastructure.Gateways;

namespace PaymentService.Infrastructure.Tests;

/// <summary>
/// Kiểm tra mock gateway luôn có đường thành công và thất bại với seed đã cố định.
/// </summary>
public sealed class MockPaymentGatewayTests
{
    /// <summary>Chứng minh gateway sinh cả hai kết quả trong 100 lần gọi theo Evidence Day 48.</summary>
    [Fact]
    public async Task ChargeAsync_ReturnsBothOutcomes_In100Calls()
    {
        // Evidence Day 48: chứng minh 100 lần gọi với seed 42 có ít nhất một kết quả Succeeded và một Failed.
        var gateway = new MockPaymentGateway(NullLogger<MockPaymentGateway>.Instance, new Random(42));
        var results = new List<bool>();

        for (var index = 0; index < 100; index++)
        {
            var result = await gateway.ChargeAsync(Guid.NewGuid(), 100m, "VND");
            results.Add(result.Succeeded);
        }

        Assert.Contains(true, results);
        Assert.Contains(false, results);
    }
}
