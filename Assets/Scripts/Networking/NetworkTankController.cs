using NetworkTankArena.Combat;
using NetworkTankArena.Tanks;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace NetworkTankArena.Networking
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject), typeof(AnticipatedNetworkTransform), typeof(TankSimulation))]
    public sealed class NetworkTankController : NetworkBehaviour
    {
        [SerializeField, Range(1f, 60f)] private float inputSendRate = 20f;
        [SerializeField, Min(0.1f)] private float inputTimeoutSeconds = 0.5f;
        [SerializeField, Min(0f)] private float remoteMovementSmoothingSeconds = 0.1f;

        private readonly NetworkVariable<float> turretYaw = new NetworkVariable<float>(0f,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly ServerTankIntent serverIntent = new ServerTankIntent();
        private TankSimulation tank;
        private AnticipatedNetworkTransform anticipatedNetworkTransform;
        private LocalNetworkSession localSession;
        private TankControlIntent localIntent;
        private ulong sequence;
        private double nextSendTime;

        public bool CanReadLocalInput => IsSpawned && IsClient && IsOwner && isActiveAndEnabled;

        private void Awake()
        {
            tank = GetComponent<TankSimulation>();
            anticipatedNetworkTransform = GetComponent<AnticipatedNetworkTransform>();
            // The existing local weapon cannot spawn replicated projectiles. Keep this lab movement-only.
            if (TryGetComponent(out TankWeapon weapon))
                weapon.enabled = false;
        }

        public override void OnNetworkSpawn()
        {
            ResetIntent();
            if (IsServer)
            {
                // NGO instantiates the active prefab before applying the approval pose. Flush that
                // pose to the CharacterController before the first Move can restore its cached origin.
                Physics.SyncTransforms();
                turretYaw.Value = transform.eulerAngles.y;
            }
            tank.ApplyReplicatedTurretYaw(turretYaw.Value);
            turretYaw.OnValueChanged += OnTurretYawChanged;
            BindLocalOwner();
        }

        public override void OnGainedOwnership() => BindLocalOwner();

        public override void OnLostOwnership()
        {
            ReleaseLocalOwner();
            ResetIntent();
        }

        protected override void OnOwnershipChanged(ulong previous, ulong current)
        {
            // Includes the server's remote-player copy, which never gains local ownership.
            ResetIntent();
        }

        public override void OnNetworkDespawn()
        {
            turretYaw.OnValueChanged -= OnTurretYawChanged;
            ReleaseLocalOwner();
            ResetIntent();
        }

        private void OnDisable()
        {
            ReleaseLocalOwner();
            localIntent = default;
            serverIntent.Reset();
            // Keep the sender sequence across component re-enables on the same spawned object.
        }

        private void OnEnable()
        {
            if (IsSpawned)
                BindLocalOwner();
        }

        public void SubmitLocalIntent(TankControlIntent intent)
        {
            if (CanReadLocalInput)
                localIntent = intent.Sanitize();
        }

        private void Update()
        {
            if (!CanReadLocalInput || Time.unscaledTimeAsDouble < nextSendTime)
                return;

            float rate = TankMath.IsFinite(inputSendRate) ? Mathf.Clamp(inputSendRate, 1f, 60f) : 20f;
            nextSendTime = Time.unscaledTimeAsDouble + 1d / rate;
            // Host follows the same RPC/validation path as a remote owner. No catch-up bursts.
            SubmitIntentRpc(localIntent.Throttle, localIntent.Turn, localIntent.AimWorldPoint,
                localIntent.HasAimPoint, ++sequence);
        }

        [Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable, InvokePermission = RpcInvokePermission.Owner)]
        private void SubmitIntentRpc(float throttle, float turn, Vector3 aimPoint, bool hasAimPoint,
            ulong inputSequence, RpcParams rpcParams = default)
        {
            if (!IsSpawned || !IsServer || !isActiveAndEnabled)
                return;

            serverIntent.TryReceive(rpcParams.Receive.SenderClientId, OwnerClientId, inputSequence,
                new TankControlIntent(throttle, turn, aimPoint, hasAimPoint), Time.unscaledTimeAsDouble);
        }

        private void FixedUpdate()
        {
            if (!IsSpawned)
                return;

            if (!IsServer)
            {
                if (CanReadLocalInput)
                {
                    // The owner moves provisionally; incoming server poses can still replace this movement.
                    Physics.SyncTransforms();
                    tank.SimulateMovement(localIntent, Time.fixedDeltaTime);
                    anticipatedNetworkTransform.AnticipateMove(transform.position);
                    anticipatedNetworkTransform.AnticipateRotate(transform.rotation);
                }
                return;
            }

            float timeout = TankMath.IsFinite(inputTimeoutSeconds) ? Mathf.Max(0.1f, inputTimeoutSeconds) : 0.5f;
            tank.Simulate(serverIntent.Read(Time.unscaledTimeAsDouble, timeout), Time.fixedDeltaTime);
            turretYaw.Value = tank.TurretYaw;
        }

        public override void OnReanticipate(double lastRoundTripTime)
        {
            if (IsServer || IsOwner || !anticipatedNetworkTransform.ShouldReanticipate)
                return;

            float smoothingDurationSeconds = TankMath.IsFinite(remoteMovementSmoothingSeconds)
                ? Mathf.Max(0f, remoteMovementSmoothingSeconds) : 0.1f;
            // Remote tanks smooth received poses without simulating their movement locally.
            anticipatedNetworkTransform.Smooth(anticipatedNetworkTransform.PreviousAnticipatedState,
                anticipatedNetworkTransform.AuthoritativeState, smoothingDurationSeconds);
        }

        private void OnTurretYawChanged(float previous, float current)
        {
            if (!IsServer)
                tank.ApplyReplicatedTurretYaw(current);
        }

        private void BindLocalOwner()
        {
            if (!CanReadLocalInput || localSession != null)
                return;

            localSession = FindFirstObjectByType<LocalNetworkSession>();
            if (localSession == null)
                Debug.LogError("The local player needs a LocalNetworkSession in the gameplay scene.", this);
            else
                localSession.BindLocalPlayer(this);
        }

        private void ReleaseLocalOwner()
        {
            if (localSession != null)
                localSession.ReleaseLocalPlayer(this);
            localSession = null;
        }

        private void ResetIntent()
        {
            localIntent = default;
            serverIntent.Reset();
            sequence = 0;
            nextSendTime = 0d;
        }
    }
}
