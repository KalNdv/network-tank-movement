using System.Reflection;
using NetworkTankArena.Networking;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace NetworkTankArena.Tests
{
    public sealed class OwnerMovementCorrectionTests
    {
        private GameObject tankRoot;
        private OwnerMovementCorrection movementCorrection;

        [SetUp]
        public void SetUp()
        {
            // Each test starts with a tank at x = 10 and no pending correction.
            tankRoot = new GameObject("Owner correction test");
            tankRoot.transform.position = new Vector3(10f, 0f, 0f);
            movementCorrection = new OwnerMovementCorrection();
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(tankRoot);

        [Test]
        public void SmallServerCorrectionIsSpreadAcrossItsDuration()
        {
            Assert.That(BeginCorrection(9f, 20f), Is.True);
            Assert.That(tankRoot.transform.position.x, Is.EqualTo(10f));

            movementCorrection.Apply(tankRoot.transform, 0.02f);

            Assert.That(tankRoot.transform.position.x, Is.EqualTo(9.8f).Within(0.0001f));
            Assert.That(tankRoot.transform.eulerAngles.y, Is.EqualTo(4f).Within(0.02f));

            movementCorrection.Apply(tankRoot.transform, 0.08f);

            Assert.That(tankRoot.transform.position.x, Is.EqualTo(9f).Within(0.0001f));
            Assert.That(tankRoot.transform.eulerAngles.y, Is.EqualTo(20f).Within(0.001f));
        }

        [Test]
        public void MissingFurtherSnapshotsDoesNotCancelCorrectionOrLocalMovement()
        {
            BeginCorrection(9f);
            for (int simulationStep = 0; simulationStep < 10; simulationStep++)
            {
                movementCorrection.Apply(tankRoot.transform, 0.02f);
                tankRoot.transform.position += Vector3.right * 0.12f;
            }

            Assert.That(tankRoot.transform.position.x, Is.EqualTo(10.2f).Within(0.0001f));
        }

        [Test]
        public void NewServerSnapshotReplacesTheUnfinishedCorrection()
        {
            BeginCorrection(9f);
            movementCorrection.Apply(tankRoot.transform, 0.02f);

            Assert.That(BeginCorrection(9.3f), Is.True);
            movementCorrection.Apply(tankRoot.transform, 0.1f);

            Assert.That(tankRoot.transform.position.x, Is.EqualTo(9.3f).Within(0.0001f));
        }

        [TestCase(6.9f, 0f, 0.1f)]
        [TestCase(10f, 46f, 0.1f)]
        [TestCase(9f, 0f, 0f)]
        [TestCase(9f, 0f, float.NaN)]
        public void LargeOrDisabledCorrectionUsesServerPoseImmediately(float serverPosition, float serverYaw, float duration)
        {
            BeginCorrection(9f);
            Assert.That(BeginCorrection(serverPosition, serverYaw, duration), Is.False);
            tankRoot.transform.SetPositionAndRotation(new Vector3(serverPosition, 0f, 0f), Quaternion.Euler(0f, serverYaw, 0f));

            movementCorrection.Apply(tankRoot.transform, 0.1f);

            Assert.That(tankRoot.transform.position.x, Is.EqualTo(serverPosition));
            Assert.That(Quaternion.Angle(tankRoot.transform.rotation, Quaternion.Euler(0f, serverYaw, 0f)), Is.LessThan(0.001f));
        }

        [Test]
        public void ResetDiscardsAnUnfinishedCorrection()
        {
            BeginCorrection(9f, 20f);
            movementCorrection.Reset();
            movementCorrection.Apply(tankRoot.transform, 0.1f);

            Assert.That(tankRoot.transform.position.x, Is.EqualTo(10f));
            Assert.That(tankRoot.transform.rotation, Is.EqualTo(Quaternion.identity));
        }

        [Test]
        public void DelayedSnapshotsCannotReverseFullSpeedForwardMovement()
        {
            const float frameSeconds = 1f / 60f;
            for (int frame = 0; frame < 60; frame++)
            {
                // Skip every third snapshot to exercise correction replacement across packet gaps.
                if (frame % 3 == 0 && frame % 9 != 0)
                    movementCorrection.Begin(new Pose(tankRoot.transform.position, Quaternion.identity),
                        new Pose(tankRoot.transform.position - Vector3.right * 1.8f, Quaternion.identity),
                        0.1f, 6f, 120f);
                float previousPosition = tankRoot.transform.position.x;
                movementCorrection.Apply(tankRoot.transform, frameSeconds);
                tankRoot.transform.position += Vector3.right * (6f * frameSeconds);

                Assert.That(tankRoot.transform.position.x, Is.GreaterThanOrEqualTo(previousPosition - 0.00001f));
            }
        }

        [Test]
        public void BoundedCorrectionStillReachesTheServerWhenInputStops()
        {
            movementCorrection.Begin(new Pose(tankRoot.transform.position, Quaternion.identity),
                new Pose(new Vector3(8.2f, 0f, 0f), Quaternion.Euler(0f, 36f, 0f)), 0.05f, 6f, 120f);

            movementCorrection.Apply(tankRoot.transform, 0.02f);
            Assert.That(tankRoot.transform.position.x, Is.EqualTo(9.88f).Within(0.0001f));
            Assert.That(tankRoot.transform.eulerAngles.y, Is.LessThanOrEqualTo(2.42f));

            // A limited correction must still finish when no further snapshots arrive.
            for (int frame = 0; frame < 30; frame++)
                movementCorrection.Apply(tankRoot.transform, 0.02f);

            Assert.That(tankRoot.transform.position.x, Is.EqualTo(8.2f).Within(0.0001f));
            Assert.That(tankRoot.transform.eulerAngles.y, Is.EqualTo(36f).Within(0.001f));
        }

        [Test]
        public void DisabledOwnerCannotQueueACorrectionForReenable()
        {
            // Set up a late owner update without starting a network session.
            NetworkTankController networkController = tankRoot.AddComponent<NetworkTankController>();
            typeof(NetworkTankController).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(networkController, null);
            networkController.enabled = false;
            typeof(NetworkBehaviour).GetProperty("IsOwner").SetValue(networkController, true);
            AnticipatedNetworkTransform anticipatedTransform = tankRoot.GetComponent<AnticipatedNetworkTransform>();
            typeof(AnticipatedNetworkTransform).GetProperty("ShouldReanticipate").SetValue(anticipatedTransform, true);
            typeof(AnticipatedNetworkTransform).GetField("m_PreviousAnticipatedTransform", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(anticipatedTransform, new AnticipatedNetworkTransform.TransformState
                {
                    Position = tankRoot.transform.position, Rotation = Quaternion.identity, Scale = Vector3.one
                });
            typeof(AnticipatedNetworkTransform).GetField("m_AuthoritativeTransform", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(anticipatedTransform, new AnticipatedNetworkTransform.TransformState
                {
                    Position = new Vector3(9f, 0f, 0f), Rotation = Quaternion.identity, Scale = Vector3.one
                });

            networkController.OnReanticipate(0.3d);
            networkController.enabled = true;
            var queuedCorrection = (OwnerMovementCorrection)typeof(NetworkTankController)
                .GetField("ownerMovementCorrection", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(networkController);
            queuedCorrection.Apply(tankRoot.transform, 0.1f);

            Assert.That(tankRoot.transform.position.x, Is.EqualTo(10f));
        }

        private bool BeginCorrection(float serverPosition, float serverYaw = 0f, float duration = 0.1f) =>
            movementCorrection.Begin(new Pose(tankRoot.transform.position, tankRoot.transform.rotation),
                new Pose(new Vector3(serverPosition, 0f, 0f), Quaternion.Euler(0f, serverYaw, 0f)), duration);
    }
}
