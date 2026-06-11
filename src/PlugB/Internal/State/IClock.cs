namespace PlugB.Internal.State;

internal interface IClock
{
    long UtcNowMilliseconds();
}