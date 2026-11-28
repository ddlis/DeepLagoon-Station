using Robust.Shared.Configuration;

namespace Content.Shared._DeepLagoon.CCVar;

[CVarDefs]
public sealed class DeepLagoonImpactCVars
{
    public static readonly CVarDef<bool> MetalImpactEffects =
        CVarDef.Create(
            "deeplagoon.metal_impact_effects",
            true,
            CVar.CLIENTONLY | CVar.ARCHIVE);
}
