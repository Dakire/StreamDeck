using System;
using Xunit;

namespace StreamDak.Tests;

/// <summary>Audit 3 : une erreur fatale doit vider la file d'écriture du journal avant la mort du processus.</summary>
public class LogFlushTests
{
    sealed class FlushSink : ILogSink
    {
        public int Flushes;
        public void Write(LogSeverity severity, string message, Exception? exception) { }
        public void Flush() => Flushes++;
    }

    sealed class ThrowingSink : ILogSink
    {
        public void Write(LogSeverity severity, string message, Exception? exception) { }
        public void Flush() => throw new InvalidOperationException("disque plein");
    }

    [Fact]
    public void FlushReachesTheSink()
    {
        var previous = Log.Sink;
        var sink = new FlushSink();
        try { Log.Sink = sink; Log.Flush(); Assert.Equal(1, sink.Flushes); }
        finally { Log.Sink = previous; }
    }

    [Fact]
    public void FailingFlushNeverThrows()
    {
        var previous = Log.Sink;
        try { Log.Sink = new ThrowingSink(); Log.Flush(); }
        finally { Log.Sink = previous; }
    }
}
