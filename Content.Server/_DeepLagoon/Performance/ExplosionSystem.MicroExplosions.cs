// Adapted from Lua-Frontier/Monolith-DS (a51b8f502a4b4fc7a949eb97a5bbb3f9e9dcdce3).
using Content.Shared._DeepLagoon.Performance;
using Content.Shared.Explosion;
using Content.Shared.Tiles;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Player;

namespace Content.Server.Explosion.EntitySystems;

public sealed partial class ExplosionSystem
{
    private static readonly Vector2i[] MicroOffsets =
    {
        new(0, 0), new(1, 0), new(-1, 0), new(0, 1), new(0, -1),
    };
    private const int MicroMaxPerTick = 40;
    private const int MicroMaxBacklog = 4096;
    private readonly Queue<MicroExplosion> _microQueue = new();
    private readonly HashSet<EntityUid> _microProcessed = new();
    private readonly List<(Vector2i GridIndices, Tile Tile)> _microTiles = new();
    private bool MicroMergeEnabled => _cfg.GetCVar(MonolithPerformanceCVars.ExplosionMerge);

    private readonly record struct MicroExplosion(EntityCoordinates Epicenter, ExplosionPrototype Proto,
        float TotalIntensity, float MaxTileIntensity, float TileBreakScale, int MaxTileBreak,
        bool CanCreateVacuum, EntityUid? Cause);

    private bool TryQueueMicroExplosion(EntityCoordinates epicenter, string typeId, float totalIntensity,
        float slope, float maxTileIntensity, float tileBreakScale, int maxTileBreak, bool canCreateVacuum, EntityUid? cause)
    {
        if (!_cfg.GetCVar(MonolithPerformanceCVars.MicroExplosions) || _microQueue.Count >= MicroMaxBacklog
            || !float.IsFinite(totalIntensity) || !float.IsFinite(maxTileIntensity) || !float.IsFinite(slope)
            || totalIntensity <= 0 || slope <= 0 || maxTileIntensity <= 0
            || totalIntensity > _cfg.GetCVar(MonolithPerformanceCVars.MicroExplosionMaxTotal)
            || maxTileIntensity > _cfg.GetCVar(MonolithPerformanceCVars.MicroExplosionMaxTile)
            || !HasComp<MapGridComponent>(epicenter.EntityId)
            || !_prototypeManager.TryIndex<ExplosionPrototype>(typeId, out var type))
            return false;
        _microQueue.Enqueue(new MicroExplosion(epicenter, type, totalIntensity, maxTileIntensity,
            tileBreakScale, maxTileBreak, canCreateVacuum, cause));
        return true;
    }

    private void ProcessMicroExplosions()
    {
        // Share DeepLagoon's existing time budget and item-throw budget with normal explosions.
        var maxTime = Math.Min(3d, MaxProcessingTime * 0.5d);
        var count = Math.Min(_microQueue.Count, MicroMaxPerTick);
        for (var i = 0; i < count && Stopwatch.Elapsed.TotalMilliseconds < maxTime; i++)
            ApplyMicroExplosion(_microQueue.Dequeue());
    }

    private void ApplyMicroExplosion(MicroExplosion micro)
    {
        var gridUid = micro.Epicenter.EntityId;
        if (TerminatingOrDeleted(gridUid) || !TryComp<MapGridComponent>(gridUid, out var grid)
            || !TryComp<BroadphaseComponent>(gridUid, out var lookup))
            return;
        if (TryComp<ProtectedGridComponent>(gridUid, out var protectedGrid) && protectedGrid.PreventExplosions)
            return;
        var mapPos = _transformSystem.ToMapCoordinates(micro.Epicenter);
        if (!_map.MapExists(mapPos.MapId))
            return;
        var center = _map.LocalToTile(gridUid, grid, micro.Epicenter);
        var centerExists = _map.TryGetTileRef(gridUid, grid, center, out var centerRef) && !centerRef.Tile.IsEmpty;
        var centerIntensity = centerExists ? MathF.Min(micro.MaxTileIntensity, micro.TotalIntensity) : 0f;
        var ringIntensity = MathF.Min(micro.MaxTileIntensity, (micro.TotalIntensity - centerIntensity) / 4f);
        var modifier = _damageableSystem.UniversalExplosionDamageModifier * _damageableSystem.UniversalAllDamageModifier;
        var remainingItemThrows = ItemThrowsPerTick;
        _microProcessed.Clear();
        _microTiles.Clear();
        for (var i = 0; i < MicroOffsets.Length; i++)
        {
            if (TerminatingOrDeleted(gridUid))
                break;
            var intensity = i == 0 ? centerIntensity : ringIntensity;
            if (intensity <= 0f)
                continue;
            var tile = center + MicroOffsets[i];
            if (!_map.TryGetTileRef(gridUid, grid, tile, out var tileRef) || tileRef.Tile.IsEmpty)
                continue;
            var damage = micro.Proto.DamagePerIntensity * (intensity * modifier);
            var throwForce = micro.Proto.ThrowEntitiesOnExplosion ? 10 * MathF.Sqrt(intensity) : 0f;
            var canDamageFloor = ExplodeTile(lookup, (gridUid, grid), tile, throwForce, damage, mapPos,
                _microProcessed, micro.Proto.ID, micro.Proto.FireStacks, micro.Proto.Temperature,
                intensity, micro.Cause, ref remainingItemThrows);
            if (canDamageFloor)
                DamageFloorTile(tileRef, intensity * micro.TileBreakScale, micro.MaxTileBreak, micro.CanCreateVacuum, _microTiles, micro.Proto);
        }
        if (TerminatingOrDeleted(gridUid))
            return;
        if (_microTiles.Count > 0)
            _map.SetTiles(gridUid, grid, _microTiles);
        // Monolith uses its own RMC visual system; do not introduce that dependency here.
        _audio.PlayStatic(micro.Proto.SmallSound, Filter.Pvs(mapPos), micro.Epicenter, true, micro.Proto.SmallSound.Params);
        CameraShake(4f, mapPos, micro.TotalIntensity);
    }

    private float CombineDistance(QueuedExplosion queued, EntityCoordinates epicenter, float totalIntensity, float distance)
    {
        if (!MicroMergeEnabled || distance <= 0f || queued.Epicenter.EntityId != epicenter.EntityId
            || queued.TotalIntensity + totalIntensity > _cfg.GetCVar(MonolithPerformanceCVars.ExplosionMergeMaxTotal)
            || !HasComp<MapGridComponent>(epicenter.EntityId))
            return distance;
        return MathF.Max(distance, _cfg.GetCVar(MonolithPerformanceCVars.ExplosionMergeDistance));
    }
}
