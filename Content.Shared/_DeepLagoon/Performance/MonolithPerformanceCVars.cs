// Adapted from Lua-Frontier/Monolith-DS (a51b8f502a4b4fc7a949eb97a5bbb3f9e9dcdce3).
using Robust.Shared.Configuration;

namespace Content.Shared._DeepLagoon.Performance;

[CVarDefs]
public sealed class MonolithPerformanceCVars
{
    public static readonly CVarDef<bool> IdleHelmsSkipWorldgen =
        CVarDef.Create("deeplagoon.worldgen.idle_helms_skip", true, CVar.SERVERONLY);
    public static readonly CVarDef<float> ChunkUnloadGrace =
        CVarDef.Create("deeplagoon.worldgen.unload_grace", 10f, CVar.SERVERONLY);
    // Experimental: the fast path does not reproduce full explosion propagation or visuals.
    public static readonly CVarDef<bool> MicroExplosions =
        CVarDef.Create("deeplagoon.battle_load.micro_explosions", false, CVar.SERVERONLY);
    public static readonly CVarDef<float> MicroExplosionMaxTotal =
        CVarDef.Create("deeplagoon.battle_load.micro_max_total", 10f, CVar.SERVERONLY);
    public static readonly CVarDef<float> MicroExplosionMaxTile =
        CVarDef.Create("deeplagoon.battle_load.micro_max_tile", 2f, CVar.SERVERONLY);
    public static readonly CVarDef<bool> ExplosionMerge =
        CVarDef.Create("deeplagoon.battle_load.explosion_merge", false, CVar.SERVERONLY);
    public static readonly CVarDef<float> ExplosionMergeDistance =
        CVarDef.Create("deeplagoon.battle_load.merge_distance", 3f, CVar.SERVERONLY);
    public static readonly CVarDef<float> ExplosionMergeMaxTotal =
        CVarDef.Create("deeplagoon.battle_load.merge_max_total", 150f, CVar.SERVERONLY);
}
