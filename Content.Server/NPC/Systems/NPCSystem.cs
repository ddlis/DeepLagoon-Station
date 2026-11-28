using Content.Shared.GameTicking;
using System.Diagnostics.CodeAnalysis;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Shared.CCVar;
using Content.Shared.Ghost;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC;
using Content.Shared.NPC.Systems;
using Prometheus;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Physics.Components; // Mono
using Robust.Shared.Player;
using Robust.Shared.Map;

namespace Content.Server.NPC.Systems
{
    /// <summary>
    ///     Handles NPCs running every tick.
    /// </summary>
    public sealed partial class NPCSystem : EntitySystem
    {
        private static readonly Gauge ActiveGauge = Metrics.CreateGauge(
            "npc_active_count",
            "Amount of NPCs that are actively processing");

        [Dependency] private IConfigurationManager _configurationManager = default!;
        [Dependency] private HTNSystem _htn = default!;
        [Dependency] private MobStateSystem _mobState = default!;
        [Dependency] private NPCSteeringSystem _steering = default!;
        [Dependency] private SharedTransformSystem _transform = default!;
        [Dependency] private IPlayerManager _playerManager = default!;

        /// <summary>
        /// Whether any NPCs are allowed to run at all.
        /// </summary>
        public bool Enabled { get; set; } = true;

        private int _maxUpdates;

        private int _count;

        private bool _pauseWhenNoPlayersInRange;
        private float _playerPauseDistance;
        private float _playerDistanceCheckTimer;
        private const float PlayerDistanceCheckInterval = 2.0f; // Check every 2 seconds

        /// <inheritdoc />
        public override void Initialize()
        {
            base.Initialize();

            Subs.CVar(_configurationManager, CCVars.NPCEnabled, value => Enabled = value, true);
            Subs.CVar(_configurationManager, CCVars.NPCMaxUpdates, obj => _maxUpdates = obj, true);
            Subs.CVar(_configurationManager, CCVars.NPCPauseWhenNoPlayersInRange, value => _pauseWhenNoPlayersInRange = value, true);
            Subs.CVar(_configurationManager, CCVars.NPCPlayerPauseDistance, value => _playerPauseDistance = value, true);
            SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => ClearSleepPass());
        }

        public void OnPlayerNPCAttach(EntityUid uid, HTNComponent component, PlayerAttachedEvent args)
        {
            SleepNPC(uid, component);
        }

        public void OnPlayerNPCDetach(EntityUid uid, HTNComponent component, PlayerDetachedEvent args)
        {
            if (_mobState.IsIncapacitated(uid) || TerminatingOrDeleted(uid))
                return;

            // This NPC has an attached mind, so it should not wake up.
            if (TryComp<MindContainerComponent>(uid, out var mindContainer) && mindContainer.HasMind)
                return;

            WakeNPC(uid, component);
        }

        public void OnNPCMapInit(EntityUid uid, HTNComponent component, MapInitEvent args)
        {
            component.Blackboard.SetValue(NPCBlackboard.Owner, uid);
            WakeNPC(uid, component);
        }

        public void OnNPCShutdown(EntityUid uid, HTNComponent component, ComponentShutdown args)
        {
            SleepNPC(uid, component);
        }

        /// <summary>
        /// Is the NPC awake and updating?
        /// </summary>
        public bool IsAwake(EntityUid uid, HTNComponent component, ActiveNPCComponent? active = null)
        {
            return Resolve(uid, ref active, false);
        }

        public bool TryGetNpc(EntityUid uid, [NotNullWhen(true)] out NPCComponent? component)
        {
            // If you add your own NPC components then add them here.

            if (TryComp<HTNComponent>(uid, out var htn))
            {
                component = htn;
                return true;
            }

            component = null;
            return false;
        }

        /// <summary>
        /// Allows the NPC to actively be updated.
        /// </summary>
        public void WakeNPC(EntityUid uid, HTNComponent? component = null)
        {
            if (!Resolve(uid, ref component, false))
            {
                return;
            }

            Log.Debug($"Waking {ToPrettyString(uid)}");
            EnsureComp<ActiveNPCComponent>(uid);
        }

        public void SleepNPC(EntityUid uid, HTNComponent? component = null)
        {
            if (!Resolve(uid, ref component, false))
            {
                return;
            }

            // Sleeping NPCs must not keep consuming the planning queue.
            _htn.CancelPlanning(component);

            // Don't bother with an event
            if (TryComp<HTNComponent>(uid, out var htn))
            {
                if (htn.Plan != null)
                {
                    var currentOperator = htn.Plan.CurrentOperator;
                    _htn.ShutdownTask(currentOperator, htn.Blackboard, HTNOperatorStatus.Failed);
                    _htn.ShutdownPlan(htn);
                    htn.Plan = null;
                }
            }

            Log.Debug($"Sleeping {ToPrettyString(uid)}");
            RemComp<ActiveNPCComponent>(uid);
        }

        /// <inheritdoc />
        public override void Update(float frameTime)
        {
            base.Update(frameTime);

            if (!Enabled)
                return;

            // Check player distances periodically to pause/unpause NPCs.
            if (_pauseWhenNoPlayersInRange)
            {
                _playerDistanceCheckTimer += frameTime;
                if (_playerDistanceCheckTimer >= PlayerDistanceCheckInterval && _sleepIndex >= _sleepQueue.Count)
                {
                    _playerDistanceCheckTimer = 0f;
                    StartSleepPass();
                }
                StepSleepPass();
            }
            else if (_sleepQueue.Count != 0)
                ClearSleepPass();

            // Add your system here.
            _htn.UpdateNPC(ref _count, _maxUpdates, frameTime);

            ActiveGauge.Set(Count<ActiveNPCComponent>());
        }

        public void OnMobStateChange(EntityUid uid, HTNComponent component, MobStateChangedEvent args)
        {
            if (HasComp<ActorComponent>(uid))
                return;

            switch (args.NewMobState)
            {
                case MobState.Alive:
                    WakeNPC(uid, component);
                    break;
                case MobState.Critical:
                case MobState.Dead:
                    SleepNPC(uid, component);
                    break;
            }
        }
    }
}
