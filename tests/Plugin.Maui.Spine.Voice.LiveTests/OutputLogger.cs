using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace Plugin.Maui.Spine.Voice.LiveTests;

/// <summary>The session's log in the test's output, so a failed live run says what the session saw.</summary>
internal sealed class OutputLogger(ITestOutputHelper output, DateTime started) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Debug;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        try
        {
            output.WriteLine($"{(DateTime.UtcNow - started).TotalSeconds,6:0.0} s  [{logLevel}] {formatter(state, exception)}{(exception is null ? "" : $" ({exception.Message})")}");
        }
        catch (InvalidOperationException)
        {
            // The test has finished; the session's last words have nowhere to go.
        }
    }
}
