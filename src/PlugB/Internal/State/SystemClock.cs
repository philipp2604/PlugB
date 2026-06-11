namespace PlugB.Internal.State;

internal class SystemClock : IClock
{
    public long UtcNowMilliseconds() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}