// Adapted from Lua-Frontier/Monolith-DS (a51b8f502a4b4fc7a949eb97a5bbb3f9e9dcdce3).
using Robust.Shared.Timing;

namespace Content.Shared._DeepLagoon.Performance;

public static class TickSpread
{
    public static int Period(IGameTiming timing, float seconds)
    {
        return Math.Max(1, (int) MathF.Round(seconds * timing.TickRate));
    }

    public static bool Due(EntityUid uid, GameTick tick, int period)
    {
        return period <= 1 || (uint) uid.Id % (uint) period == tick.Value % (uint) period;
    }
}
