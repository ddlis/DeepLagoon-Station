// Adapted from Lua-Frontier/Monolith-DS (a51b8f502a4b4fc7a949eb97a5bbb3f9e9dcdce3).
using Content.Server.Shuttles.Components;
using Content.Server.Worldgen.Components;
using Content.Shared._DeepLagoon.Performance;
using Content.Shared.GameTicking;
using Content.Shared.Ghost;
using Robust.Shared.Configuration;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Player;

namespace Content.Server.Worldgen.Systems;

public sealed partial class WorldControllerSystem
{
    [Dependency] private IConfigurationManager _perfCfg = default!;
    private readonly Dictionary<EntityUid, TimeSpan> _unneededSince = new();
    private readonly HashSet<EntityUid> _activeHelmGrids = new();
    private readonly Stack<EntityUid> _pendingHelmGrids = new();
    private TimeSpan _nextChunkCountLog;
    private int _skippedHelms;
    private bool _idleHelmsSkip;
    private float _unloadGrace;

    private void InitializeMonolithWorldgen()
    {
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ =>
        {
            _unneededSince.Clear();
            _activeHelmGrids.Clear();
            _pendingHelmGrids.Clear();
            _nextChunkCountLog = TimeSpan.Zero;
        });
    }

    private bool ShouldUnload(EntityUid chunk, bool needed)
    {
        if (needed)
        {
            _unneededSince.Remove(chunk);
            return false;
        }

        var grace = _unloadGrace;
        if (!float.IsFinite(grace) || grace <= 0f)
            return true;

        var now = _gameTiming.CurTime;
        if (!_unneededSince.TryGetValue(chunk, out var since))
            _unneededSince[chunk] = since = now;

        if (now - since < TimeSpan.FromSeconds(grace))
            return false;

        _unneededSince.Remove(chunk);
        return true;
    }

    private void CollectActiveHelmGrids()
    {
        _activeHelmGrids.Clear();
        _pendingHelmGrids.Clear();
        _skippedHelms = 0;
        _idleHelmsSkip = _perfCfg.GetCVar(MonolithPerformanceCVars.IdleHelmsSkipWorldgen);
        _unloadGrace = _perfCfg.GetCVar(MonolithPerformanceCVars.ChunkUnloadGrace);
        if (!_idleHelmsSkip)
            return;

        var actors = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (actors.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid is { } grid && !HasComp<GhostComponent>(uid) && _activeHelmGrids.Add(grid))
                _pendingHelmGrids.Push(grid);
        }

        var consoles = EntityQueryEnumerator<ShuttleConsoleComponent, TransformComponent>();
        while (consoles.MoveNext(out _, out var console, out var xform))
        {
            if (console.SubscribedPilots.Count > 0 && xform.GridUid is { } grid && _activeHelmGrids.Add(grid))
                _pendingHelmGrids.Push(grid);
        }

        while (_pendingHelmGrids.TryPop(out var current))
        {
            if (!TryComp<JointComponent>(current, out var joints))
                continue;

            foreach (var joint in joints.GetJoints.Values)
            {
                var other = joint.BodyAUid == current ? joint.BodyBUid : joint.BodyAUid;
                if (!TryComp<TransformComponent>(other, out var otherXform))
                    continue;
                var otherGrid = HasComp<MapGridComponent>(other) ? other : otherXform.GridUid;
                if (otherGrid is { } connectedGrid && _activeHelmGrids.Add(connectedGrid))
                    _pendingHelmGrids.Push(connectedGrid);
            }
        }
    }

    private bool IsIdleHelm(EntityUid uid, TransformComponent xform)
    {
        if (!_idleHelmsSkip || !HasComp<ShuttleConsoleComponent>(uid))
            return false;
        if (xform.GridUid is not { } grid || _activeHelmGrids.Contains(grid))
            return false;
        // Unoccupied moving shuttles still need their surrounding space (e.g. autopilot).
        if (TryComp<PhysicsComponent>(grid, out var body) && body.LinearVelocity.LengthSquared() > 0.01f)
            return false;
        _skippedHelms++;
        return true;
    }

    private void LogChunkCount()
    {
        var now = _gameTiming.CurTime;
        if (now < _nextChunkCountLog)
            return;
        _nextChunkCountLog = now + TimeSpan.FromSeconds(10);
        _sawmill.Debug($"{EntityManager.Count<LoadedChunkComponent>()} loaded chunks, {_skippedHelms} idle helms skipped.");
    }
}
