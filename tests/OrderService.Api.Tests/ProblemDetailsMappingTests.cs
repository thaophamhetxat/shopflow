// File này chứa test chứng minh mapper exception sang Problem Details đúng chuẩn Day 47 và không lộ dữ liệu nhạy cảm trong 500 responses.
// Tạo ở Day 47 để chứng minh API v1/v2 map 404/409/422/500 theo RFC 9457 và giữ safe message ở lỗi không mong muốn.
using FluentValidation;
using FluentValidation.Results;
using OrderService.API.Endpoints;
using OrderService.Application;
using OrderService.Domain.Exceptions;

namespace OrderService.Api.Tests;

public class ProblemDetailsMappingTests
{
    // Test này chứng minh Evidence Day 47: OrderNotFoundException phải map thành 404 Not Found theo RFC 9457 và detail của exception.
    [Fact]
    public void Map_OrderNotFoundException_Returns404()
    {
        var mapping = ProblemDetailsMapping.Map(new OrderNotFoundException(Guid.NewGuid()), "/api/v1/orders/123");

        Assert.Equal(404, mapping.StatusCode);
        Assert.Equal("Not Found", mapping.Title);
        Assert.Contains("Không tìm thấy đơn hàng", mapping.Detail);
    }

    // Test này chứng minh Evidence Day 47: OrderDomainException phải map thành 409 Conflict theo quy tắc nghiệp vụ không cho thao tác trạng thái sai.
    [Fact]
    public void Map_OrderDomainException_Returns409()
    {
        var mapping = ProblemDetailsMapping.Map(new OrderDomainException("Không thể hủy đơn đã hủy trước đó."), "/api/v1/orders/123");

        Assert.Equal(409, mapping.StatusCode);
        Assert.Equal("Conflict", mapping.Title);
        Assert.Equal("Không thể hủy đơn đã hủy trước đó.", mapping.Detail);
    }

    // Test này chứng minh Evidence Day 47: ValidationException phải gộp nhiều lỗi bằng "; " và gửi 422 với detail đầy đủ để client biết đâu sai.
    [Fact]
    public void Map_ValidationException_Returns422_WithErrorMessages()
    {
        var exception = new ValidationException(new[]
        {
            new ValidationFailure("customerId", "customerId không hợp lệ."),
            new ValidationFailure("pageSize", "pageSize phải là số nguyên hợp lệ.")
        });

        var mapping = ProblemDetailsMapping.Map(exception, "/api/v1/orders");

        Assert.Equal(422, mapping.StatusCode);
        Assert.Equal("Unprocessable Entity", mapping.Title);
        Assert.Contains("customerId không hợp lệ.", mapping.Detail);
        Assert.Contains("pageSize phải là số nguyên hợp lệ.", mapping.Detail);
    }

    // Test này chứng minh Evidence Day 47: lỗi kỹ thuật không được lộ thông tin nhạy cảm như password hoặc connection string, chỉ trả message an toàn cố định.
    [Fact]
    public void Map_UnknownException_Returns500_WithoutSensitiveDetail()
    {
        var exception = new InvalidOperationException("Connection string Host=server;Password=super-secret;User=admin");

        var mapping = ProblemDetailsMapping.Map(exception, "/api/v1/orders");

        Assert.Equal(500, mapping.StatusCode);
        Assert.Equal("Internal Server Error", mapping.Title);
        Assert.Equal("Đã xảy ra lỗi không mong muốn.", mapping.Detail);
        Assert.DoesNotContain("secret", mapping.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", mapping.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("connection", mapping.Detail, StringComparison.OrdinalIgnoreCase);
    }
}
