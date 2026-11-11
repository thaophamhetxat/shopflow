using System.Text.Json;
using System.Text.Json.Serialization;
using OrderService.Domain.Entities;
using OrderService.Domain.ValueObjects;

namespace OrderService.Infrastructure.Persistence;

/// <summary>
/// Cấu hình serializer dùng cho event stream và snapshot của OrderService, đảm bảo value object sẵn sàng serialize/deserialize đầy đủ trong JSON.
/// Nếu chỉ dùng serializer mặc định, các struct như OrderId/CustomerId/Money/Address sẽ bị mất dữ liệu khi deserialize vì chúng không có constructor khởi tạo mặc định phù hợp.
/// </summary>
public static class OrderJsonSerializerOptions
{
    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNameCaseInsensitive = true
    };

    static OrderJsonSerializerOptions()
    {
        Default.Converters.Add(new OrderIdJsonConverter());
        Default.Converters.Add(new CustomerIdJsonConverter());
        Default.Converters.Add(new MoneyJsonConverter());
        Default.Converters.Add(new AddressJsonConverter());
        Default.Converters.Add(new OrderItemJsonConverter());
    }

    private sealed class OrderIdJsonConverter : JsonConverter<OrderId>
    {
        public override OrderId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var value = reader.GetGuid();
            return OrderId.From(value);
        }

        public override void Write(Utf8JsonWriter writer, OrderId value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.Value);
        }
    }

    private sealed class CustomerIdJsonConverter : JsonConverter<CustomerId>
    {
        public override CustomerId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var value = reader.GetGuid();
            return CustomerId.From(value);
        }

        public override void Write(Utf8JsonWriter writer, CustomerId value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.Value);
        }
    }

    private sealed class MoneyJsonConverter : JsonConverter<Money>
    {
        public override Money Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;
            var amount = root.GetProperty("amount").GetDecimal();
            var currency = root.GetProperty("currency").GetString() ?? string.Empty;
            return new Money(amount, currency);
        }

        public override void Write(Utf8JsonWriter writer, Money value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteNumber("amount", value.Amount);
            writer.WriteString("currency", value.Currency);
            writer.WriteEndObject();
        }
    }

    private sealed class AddressJsonConverter : JsonConverter<Address>
    {
        public override Address Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;
            var street = root.GetProperty("street").GetString() ?? string.Empty;
            var city = root.GetProperty("city").GetString() ?? string.Empty;
            var country = root.GetProperty("country").GetString() ?? string.Empty;
            var postalCode = root.TryGetProperty("postalCode", out var postal) ? postal.GetString() : null;
            return new Address(street, city, country, postalCode);
        }

        public override void Write(Utf8JsonWriter writer, Address value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("street", value.Street);
            writer.WriteString("city", value.City);
            writer.WriteString("country", value.Country);
            writer.WriteString("postalCode", value.PostalCode ?? string.Empty);
            writer.WriteEndObject();
        }
    }

    private sealed class OrderItemJsonConverter : JsonConverter<OrderItem>
    {
        public override OrderItem Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;
            var productId = root.GetProperty("productId").GetGuid();
            var quantity = root.GetProperty("quantity").GetInt32();
            var unitPrice = root.GetProperty("unitPrice").Deserialize<Money>(options);
            return new OrderItem(productId, quantity, unitPrice);
        }

        public override void Write(Utf8JsonWriter writer, OrderItem value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("id", value.Id);
            writer.WriteString("productId", value.ProductId);
            writer.WriteNumber("quantity", value.Quantity);
            writer.WritePropertyName("unitPrice");
            JsonSerializer.Serialize(writer, value.UnitPrice, options);
            writer.WriteEndObject();
        }
    }
}
