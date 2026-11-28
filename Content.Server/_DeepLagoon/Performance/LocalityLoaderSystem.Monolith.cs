// Adapted from Lua-Frontier/Monolith-DS (a51b8f502a4b4fc7a949eb97a5bbb3f9e9dcdce3).
using Content.Server._NF.Salvage;
using Robust.Shared.Timing;

namespace Content.Server.Worldgen.Systems;

public sealed partial class LocalityLoaderSystem
{
    [Dependency] private IGameTiming _timing = default!;
    private readonly Dictionary<EntityUid, List<EntityUid>> _restrictionsByGrid = new();
    private GameTick _restrictionsTick = GameTick.Zero;

    private void DespawnRestrictions(EntityUid grid)
    {
        if (_restrictionsTick != _timing.CurTick)
        {
            _restrictionsTick = _timing.CurTick;
            _restrictionsByGrid.Clear();
            var query = AllEntityQuery<NFSalvageMobRestrictionsComponent>();
            while (query.MoveNext(out var uid, out var restrictions))
            {
                if (!restrictions.DespawnIfOffLinkedGrid)
                    continue;
                if (!_restrictionsByGrid.TryGetValue(restrictions.LinkedGridEntity, out var list))
                    _restrictionsByGrid[restrictions.LinkedGridEntity] = list = new List<EntityUid>();
                list.Add(uid);
            }
        }
        if (!_restrictionsByGrid.Remove(grid, out var linked))
            return;
        foreach (var uid in linked)
        {
            if (TryComp<NFSalvageMobRestrictionsComponent>(uid, out var restrictions)
                && restrictions.DespawnIfOffLinkedGrid && restrictions.LinkedGridEntity == grid)
                QueueDel(uid);
        }
    }
}
