using System.Numerics;
using Content.Server.NPC.HTN;
using Content.Shared.Ghost;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Server.Player;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Player;

namespace Content.Server.NPC.Systems;

public sealed partial class NPCSystem
{
    private const int SleepPassTicks = 45;

    private readonly List<EntityUid> _sleepQueue = new();

    private readonly Dictionary<MapId, List<Vector2>>
        _sleepPlayers = new();

    private int _sleepIndex;
    private int _sleepBatch;

    private void ClearSleepPass()
    {
        _sleepPlayers.Clear();
        _sleepQueue.Clear();

        _sleepIndex = 0;
        _sleepBatch = 0;
        _playerDistanceCheckTimer = 0f;
    }

    private void RefreshSleepPlayers()
    {
        foreach (var list in _sleepPlayers.Values)
            list.Clear();

        foreach (var session in _playerManager.Sessions)
        {
            if (session.AttachedEntity is not { Valid: true } playerEnt ||
                TerminatingOrDeleted(playerEnt) ||
                HasComp<GhostComponent>(playerEnt) ||
                !TryComp<TransformComponent>(playerEnt, out var playerTransform))
                continue;

            if (TryComp<MobStateComponent>(playerEnt, out var state) &&
                state.CurrentState != MobState.Alive)
                continue;

            var coords = _transform.GetMapCoordinates(
                playerEnt, playerTransform);

            if (coords.MapId == MapId.Nullspace)
                continue;

            if (!_sleepPlayers.TryGetValue(coords.MapId, out var list))
            {
                list = new List<Vector2>();
                _sleepPlayers.Add(coords.MapId, list);
            }

            list.Add(coords.Position);
        }
    }

    private void StartSleepPass()
    {
        _sleepPlayers.Clear();
        _sleepQueue.Clear();

        var query = EntityQueryEnumerator<HTNComponent>();

        while (query.MoveNext(out var uid, out _))
            _sleepQueue.Add(uid);

        _sleepIndex = 0;

        _sleepBatch = Math.Max(
            1,
            (_sleepQueue.Count + SleepPassTicks - 1)
            / SleepPassTicks);
    }

    private void StepSleepPass()
    {
        if (_sleepIndex >= _sleepQueue.Count)
            return;

        RefreshSleepPlayers();

        var end = Math.Min(
            _sleepQueue.Count,
            _sleepIndex + _sleepBatch);

        for (; _sleepIndex < end; _sleepIndex++)
            CheckSleep(_sleepQueue[_sleepIndex]);
    }

    private void CheckSleep(EntityUid npcUid)
    {
        if (TerminatingOrDeleted(npcUid) ||
            !TryComp<HTNComponent>(npcUid, out var htn) ||
            !TryComp<TransformComponent>(npcUid, out var npcTransform) ||
            HasComp<ActorComponent>(npcUid) ||
            TryComp<MindContainerComponent>(npcUid, out var mindContainer)
                && mindContainer.HasMind ||
            _mobState.IsIncapacitated(npcUid))
            return;

        var hasNearbyPlayer = false;

        var minDistance =
            htn.SleepPlayerCheckRangeOverride
            ?? _playerPauseDistance;

        if (htn.SleepMaxGridSpeed is { } maxSpeed &&
            TryComp<PhysicsComponent>(
                npcTransform.GridUid, out var gridBody) &&
            gridBody.LinearVelocity.Length() > maxSpeed)
        {
            hasNearbyPlayer = true;
        }

        if (!hasNearbyPlayer && minDistance >= 0f)
        {
            var npcCoords = _transform.GetMapCoordinates(
                npcUid, npcTransform);

            if (npcCoords.MapId == MapId.Nullspace)
                return;

            if (_sleepPlayers.TryGetValue(
                    npcCoords.MapId, out var players))
            {
                var limit = minDistance * minDistance;

                foreach (var position in players)
                {
                    if ((position - npcCoords.Position)
                        .LengthSquared() > limit)
                        continue;

                    hasNearbyPlayer = true;
                    break;
                }
            }
        }

        var isAwake = IsAwake(npcUid, htn);

        if (!hasNearbyPlayer && isAwake)
            SleepNPC(npcUid, htn);
        else if (hasNearbyPlayer && !isAwake)
            WakeNPC(npcUid, htn);
    }
}
