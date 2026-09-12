using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ExusiAI.Infrastructure;

public static class LoggingExtensions
{
    public static ILoggingBuilder AddExusiAIFileLogging(this ILoggingBuilder builder)
    {
        builder.Services.AddSingleton<ILoggerProvider, FileLoggerProvider>();
        return builder;
    }
}
