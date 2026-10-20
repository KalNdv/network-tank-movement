using System.Collections.Generic;
using NetworkTankArena.Combat;
using NetworkTankArena.Tanks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace NetworkTankArena.Tests
{
    /// <summary>Unity physics-query integration tests with explicit gameplay steps, not Play Mode timing tests.</summary>
    public sealed class ProjectileCombatTests
    {
        private static readonly Vector3 TestOrigin = new Vector3(2000f, 100f, 2000f);
        private readonly List<GameObject> objects = new List<GameObject>();
        private float previousTimeScale;
        private string objectPrefix;

        [SetUp]
        public void SetUp()
        {
            previousTimeScale = Time.timeScale;
            Time.timeScale = 1f;
            objectPrefix = "Combat test " + System.Guid.NewGuid().ToString("N") + " ";
        }

        [TearDown]
        public void TearDown()
        {
            // Include any weapon-created clone, even if a no-spawn assertion failed.
            foreach (Projectile projectile in Object.FindObjectsByType<Projectile>(FindObjectsSortMode.None))
            {
                if (projectile.name.StartsWith(objectPrefix))
                    Object.DestroyImmediate(projectile.gameObject);
            }
            for (int i = objects.Count - 1; i >= 0; i--)
            {
                if (objects[i] != null)
                    Object.DestroyImmediate(objects[i]);
            }
            objects.Clear();
            Time.timeScale = previousTimeScale;
        }

        [Test]
        public void OwnerRootAndDescendantsDoNotHideAnObstructionBehindThem()
        {
            GameObject owner = CreateBox("owner", TestOrigin, Vector3.one * 2f);
            CreateBox("owner child", TestOrigin + Vector3.forward * 2f, Vector3.one, owner.transform);
            CreateBox("wall", TestOrigin + Vector3.forward * 5f, new Vector3(2f, 2f, 0.1f));
            Projectile projectile = CreateProjectile(owner);

            projectile.Simulate(0.5f);

            Assert.That(projectile.IsTerminal, Is.True);
            Assert.That(projectile.Position.z - TestOrigin.z, Is.EqualTo(4.85f).Within(0.01f));
        }

        [Test]
        public void HighSpeedSweepHitsThinWallAndCoverBlocksDamageBehindIt()
        {
            GameObject owner = CreateObject("owner", TestOrigin);
            CreateBox("thin wall", TestOrigin + Vector3.forward * 7f, new Vector3(2f, 2f, 0.02f));
            DamageTestTarget target = CreateBox("covered target", TestOrigin + Vector3.forward * 9f,
                Vector3.one).AddComponent<DamageTestTarget>();
            Projectile projectile = CreateProjectile(owner, 1000f);

            projectile.Simulate(0.1f);

            Assert.That(projectile.IsTerminal, Is.True);
            Assert.That(projectile.Position.z - TestOrigin.z, Is.EqualTo(6.89f).Within(0.01f));
            Assert.That(target.HitCount, Is.Zero);
        }

        [Test]
        public void HitDeliversDamageOnceToAnAncestorAndAttributesTheFiringTank()
        {
            GameObject owner = CreateObject("owner", TestOrigin);
            GameObject targetRoot = CreateObject("target root", TestOrigin + Vector3.forward * 5f);
            DamageTestTarget target = targetRoot.AddComponent<DamageTestTarget>();
            CreateBox("target collider", targetRoot.transform.position, Vector3.one, targetRoot.transform);
            Projectile projectile = CreateProjectile(owner);

            projectile.Simulate(0.5f);
            Vector3 impactPosition = projectile.Position;
            projectile.Simulate(10f);

            Assert.That(projectile.IsTerminal, Is.True);
            Assert.That(projectile.Position, Is.EqualTo(impactPosition));
            Assert.That(target.HitCount, Is.EqualTo(1));
            Assert.That(target.AccumulatedDamage, Is.EqualTo(25));
            Assert.That(target.LastSource, Is.SameAs(owner));
        }

        [Test]
        public void StartingInsideATargetResolvesOneImmediateImpact()
        {
            GameObject owner = CreateObject("owner", TestOrigin);
            DamageTestTarget target = CreateBox("overlapping target", TestOrigin,
                Vector3.one).AddComponent<DamageTestTarget>();
            Projectile projectile = CreateProjectile(owner);

            projectile.Simulate(0.1f);
            projectile.Simulate(0.1f);

            Assert.That(projectile.IsTerminal, Is.True);
            Assert.That(projectile.Position, Is.EqualTo(TestOrigin));
            Assert.That(target.HitCount, Is.EqualTo(1));
        }

        [TestCase(0.65f)]
        [TestCase(1.3f)]
        public void LaunchPathOrMuzzleObstructionConsumesCooldownWithoutSpawningBeyondIt(float targetDistance)
        {
            TankWeapon weapon = CreateWeapon(out TankSimulation tank);
            DamageTestTarget target = CreateBox("launch obstruction",
                tank.TurretOrigin + Vector3.forward * targetDistance,
                new Vector3(1f, 1f, 0.2f)).AddComponent<DamageTestTarget>();
            int countBefore = Object.FindObjectsByType<Projectile>(FindObjectsSortMode.None).Length;

            Assert.That(weapon.TryFire(), Is.True);
            Assert.That(weapon.TryFire(), Is.False);

            Assert.That(Object.FindObjectsByType<Projectile>(FindObjectsSortMode.None).Length, Is.EqualTo(countBefore));
            Assert.That(target.HitCount, Is.EqualTo(1));
            Assert.That(target.AccumulatedDamage, Is.EqualTo(25));
            Assert.That(target.LastSource, Is.SameAs(tank.gameObject));
        }

        [Test]
        public void LifetimeClampsLastStepAndTerminalProjectileCannotHitAFurtherTarget()
        {
            GameObject owner = CreateObject("owner", TestOrigin);
            DamageTestTarget target = CreateBox("beyond lifetime", TestOrigin + Vector3.forward * 11f,
                new Vector3(1f, 1f, 0.2f)).AddComponent<DamageTestTarget>();
            Projectile projectile = CreateProjectile(owner, 20f, 0.5f);

            projectile.Simulate(0.2f);
            projectile.Simulate(10f);
            Vector3 expiredPosition = projectile.Position;
            projectile.Simulate(10f);

            Assert.That(projectile.IsTerminal, Is.True);
            Assert.That(projectile.Age, Is.EqualTo(0.5f));
            Assert.That(expiredPosition.z - TestOrigin.z, Is.EqualTo(10f).Within(0.01f));
            Assert.That(projectile.Position, Is.EqualTo(expiredPosition));
            Assert.That(target.HitCount, Is.Zero);
        }

        [Test]
        public void AcceptedShotSnapshotsCanonicalPoseAndLocalAdapterSkipsCreationFrame()
        {
            TankWeapon weapon = CreateWeapon(out TankSimulation tank);
            tank.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            Vector3 expectedOrigin = tank.TurretOrigin + Vector3.forward * 1.3f;

            Assert.That(weapon.TryFire(), Is.True);
            Assert.That(weapon.TryFire(), Is.False);
            Projectile shot = System.Array.Find(Object.FindObjectsByType<Projectile>(FindObjectsSortMode.None),
                candidate => candidate.name == objectPrefix + "projectile template(Clone)");
            Assert.That(shot, Is.Not.Null);
            Assert.That(Vector3.Distance(shot.Position, expectedOrigin), Is.LessThan(0.001f));
            Assert.That(shot.Direction, Is.EqualTo(Vector3.forward));

            int creationFrame = Time.frameCount;
            shot.SimulateLocalFrame(creationFrame, 0.1f);
            Assert.That(shot.Position, Is.EqualTo(expectedOrigin));
            Assert.That(shot.Age, Is.Zero);

            tank.transform.position += Vector3.right * 10f;
            tank.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            shot.SimulateLocalFrame(creationFrame + 1, 0.1f);
            Assert.That(Vector3.Distance(shot.Position, expectedOrigin + Vector3.forward * 2f),
                Is.LessThan(0.001f));
            Assert.That(shot.Direction, Is.EqualTo(Vector3.forward));
        }

        [Test]
        public void PausedWeaponRejectsRequestWithoutSpawning()
        {
            TankWeapon weapon = CreateWeapon(out _);
            int countBefore = Object.FindObjectsByType<Projectile>(FindObjectsSortMode.None).Length;
            Time.timeScale = 0f;

            Assert.That(weapon.TryFire(), Is.False);
            Assert.That(Object.FindObjectsByType<Projectile>(FindObjectsSortMode.None).Length, Is.EqualTo(countBefore));
        }

        [Test]
        public void ProjectileRejectsInvalidPoseAndPhysicsShape()
        {
            GameObject owner = CreateObject("owner", TestOrigin);
            Projectile projectile = CreateObject("invalid projectile", TestOrigin).AddComponent<Projectile>();
            Assert.That(projectile.Initialize(default, owner, 20f, 0.1f, 3f, 25, 1), Is.False);
            Assert.That(FiringPose.TryCalculate(TestOrigin, 0f, 0f, out var pose), Is.True);
            projectile.gameObject.AddComponent<SphereCollider>();
            Assert.That(projectile.Initialize(pose, owner, 20f, 0.1f, 3f, 25, 1), Is.False);
        }

        private TankWeapon CreateWeapon(out TankSimulation tank)
        {
            GameObject root = CreateObject("tank", TestOrigin);
            root.AddComponent<CharacterController>();
            tank = root.AddComponent<TankSimulation>();
            TankWeapon weapon = root.AddComponent<TankWeapon>();
            Projectile template = CreateObject("projectile template", TestOrigin).AddComponent<Projectile>();
            var serialized = new SerializedObject(weapon);
            serialized.FindProperty("tank").objectReferenceValue = tank;
            serialized.FindProperty("projectilePrefab").objectReferenceValue = template;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return weapon;
        }

        private Projectile CreateProjectile(GameObject owner, float speed = 20f, float lifetime = 3f)
        {
            Assert.That(FiringPose.TryCalculate(TestOrigin, 0f, 0f, out var pose), Is.True);
            Projectile projectile = CreateObject("projectile", TestOrigin).AddComponent<Projectile>();
            Assert.That(projectile.Initialize(pose, owner, speed, 0.1f, lifetime, 25, 1), Is.True);
            return projectile;
        }

        private GameObject CreateBox(string name, Vector3 position, Vector3 size, Transform parent = null)
        {
            GameObject result = CreateObject(name, position);
            if (parent != null)
                result.transform.SetParent(parent, true);
            // Set the pose before adding the collider; no routine transform synchronization is needed.
            result.AddComponent<BoxCollider>().size = size;
            return result;
        }

        private GameObject CreateObject(string name, Vector3 position)
        {
            var result = new GameObject(objectPrefix + name);
            result.transform.position = position;
            objects.Add(result);
            return result;
        }
    }
}
