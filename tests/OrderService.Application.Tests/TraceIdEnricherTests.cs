// File này kiểm tra Serilog enricher thêm định danh Activity vào structured log.
// Layer Application.Tests xác nhận khả năng nối log với distributed trace của Day 64.
// Day 65 dùng test này làm regression guard cho log có Activity và log không có Activity.

using System.Diagnostics;
using Serilog.Core;
using Serilog.Events;
using Serilog.Parsing;
using ShopFlow.BuildingBlocks;
using Xunit;

namespace OrderService.Application.Tests;

/// <summary>
/// Unit tests cho enrichment TraceId/SpanId trong log event Serilog.
/// </summary>
public sealed class TraceIdEnricherTests
{
    [Fact]
    // Evidence Day 65: trace đang hoạt động được ghi vào log, còn log ngoài trace không nhận property giả.
    public void TraceEnricher_AddsTraceIdAndSpanId()
    {
        var previousActivity = Activity.Current;
        Activity.Current = null;

        try
        {
            using var listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == "ShopFlow",
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
            };
            ActivitySource.AddActivityListener(listener);

            var enricher = new TraceIdEnricher();
            var propertyFactory = new LogEventPropertyFactory();

            using (var activity = ShopFlowActivitySource.Source.StartActivity("test"))
            {
                Assert.NotNull(activity);
                var logEvent = CreateLogEvent();

                enricher.Enrich(logEvent, propertyFactory);

                Assert.Equal(activity.TraceId.ToHexString(), GetStringProperty(logEvent, "TraceId"));
                Assert.Equal(activity.SpanId.ToHexString(), GetStringProperty(logEvent, "SpanId"));
            }

            Activity.Current = null;
            var logWithoutActivity = CreateLogEvent();

            enricher.Enrich(logWithoutActivity, propertyFactory);

            Assert.False(logWithoutActivity.Properties.ContainsKey("TraceId"));
            Assert.False(logWithoutActivity.Properties.ContainsKey("SpanId"));
        }
        finally
        {
            Activity.Current = previousActivity;
        }
    }

    private static LogEvent CreateLogEvent()
    {
        var messageTemplate = new MessageTemplateParser().Parse("trace enrichment test");
        return new LogEvent(
            DateTimeOffset.UtcNow,
            LogEventLevel.Information,
            exception: null,
            messageTemplate,
            Array.Empty<LogEventProperty>());
    }

    private static string GetStringProperty(LogEvent logEvent, string name)
    {
        return Assert.IsType<ScalarValue>(logEvent.Properties[name]).Value as string
            ?? throw new InvalidOperationException($"Log property '{name}' is not a string.");
    }

    private sealed class LogEventPropertyFactory : ILogEventPropertyFactory
    {
        public LogEventProperty CreateProperty(string name, object? value, bool destructureObjects = false)
        {
            return new LogEventProperty(name, new ScalarValue(value));
        }
    }
}
