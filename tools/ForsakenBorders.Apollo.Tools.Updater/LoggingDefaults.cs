using Serilog.Events;

namespace ForsakenBorders.Apollo.Tools.Updater
{
    public sealed record LoggingDefaults
    {
        public string Format { get; init; } = "[{Timestamp:O}] [{Level:u4}] {SourceContext}: {Message:lj}{NewLine}{Exception}";
        public LogEventLevel LogLevel { get; init; } = LogEventLevel.Information;
    }
}
