// File này chứa test chứng minh DTO v2 hiện có EstimatedDelivery nhưng không phá client v1, vì field mới được thêm sau cùng và không xâm phạm schema cũ.
// Tạo ở Day 47 để chứng minh client v1 đọc response v2 không break, đồng thời v2 vẫn giữ thêm 1 field demo cho versioning.
using System.Text.Json;
using OrderService.API.Dtos;
using OrderService.Application.Queries;

namespace OrderService.Api.Tests;

public class OrderDetailV2DtoTests
{
    // Test này chứng minh Evidence Day 47: EstimatedDelivery của v2 luôn bằng CreatedAt + 3 ngày theo quy tắc demo, không chạm vào domain hay application.
    [Fact]
    public void V2Dto_EstimatedDelivery_IsCreatedAtPlus3Days()
    {
        var createdAt = new DateTime(2026, 11, 11, 9, 0, 0, DateTimeKind.Utc);
        var detail = new OrderDetailDto(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            "Pending",
            250000m,
            "VND",
            createdAt,
            new[]
            {
                new OrderDetailItemDto(
                    Guid.Parse("33333333-3333-3333-3333-333333333333"),
                    2,
                    125000m,
                    "VND",
                    250000m)
            });

        var v2 = OrderDetailV2Dto.From(detail);

        Assert.Equal(detail.CreatedAt.AddDays(3), v2.EstimatedDelivery);
        Assert.Equal(detail.OrderId, v2.OrderId);
        Assert.Equal(detail.CustomerId, v2.CustomerId);
        Assert.Equal(detail.Status, v2.Status);
        Assert.Equal(detail.TotalAmount, v2.TotalAmount);
        Assert.Equal(detail.TotalCurrency, v2.TotalCurrency);
        Assert.Equal(detail.CreatedAt, v2.CreatedAt);
        Assert.Equal(detail.Items, v2.Items);
    }

    // Test này chứng minh Evidence Day 47: client v1 có thể deserialize response v2 mà bỏ qua field mới EstimatedDelivery, và v1 JSON không chứa field v2.
    [Fact]
    public void V1Client_CanDeserializeV2Response()
    {
        var createdAt = new DateTime(2026, 11, 11, 9, 0, 0, DateTimeKind.Utc);
        var v2 = new OrderDetailV2Dto(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            "Confirmed",
            250000m,
            "VND",
            createdAt,
            new[]
            {
                new OrderDetailItemDto(
                    Guid.Parse("33333333-3333-3333-3333-333333333333"),
                    2,
                    125000m,
                    "VND",
                    250000m)
            },
            createdAt.AddDays(3));

        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var v2Json = JsonSerializer.Serialize(v2, options);
        var v1 = JsonSerializer.Deserialize<OrderDetailDto>(v2Json, options);

        Assert.NotNull(v1);
        Assert.Equal(v2.OrderId, v1!.OrderId);
        Assert.Equal(v2.CustomerId, v1.CustomerId);
        Assert.Equal(v2.Status, v1.Status);
        Assert.Equal(v2.TotalAmount, v1.TotalAmount);
        Assert.Equal(v2.TotalCurrency, v1.TotalCurrency);
        Assert.Equal(v2.CreatedAt, v1.CreatedAt);
        Assert.Equal(v2.Items.Count, v1.Items.Count);

        var v1Json = JsonSerializer.Serialize(new OrderDetailDto(
            v2.OrderId,
            v2.CustomerId,
            v2.Status,
            v2.TotalAmount,
            v2.TotalCurrency,
            v2.CreatedAt,
            v2.Items), options);

        Assert.DoesNotContain("estimatedDelivery", v1Json, StringComparison.OrdinalIgnoreCase);
    }
}
