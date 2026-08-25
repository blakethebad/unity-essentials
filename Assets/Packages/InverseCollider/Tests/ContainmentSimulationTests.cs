using NUnit.Framework;
using UnityEngine;

namespace UnityEssentials.Colliders.Tests
{
    /// <summary>
    /// End-to-end containment against real PhysX steps: a body launched at an inverse wall from the
    /// inside must still be inside 150 steps later. Stepping the default physics scene also steps
    /// whatever the open editor scene owns, so the whole rig sits at (0, 1000, 0), out of reach of
    /// anything authored near the origin.
    /// </summary>
    [TestFixture]
    public class ContainmentSimulationTests : ColliderTestFixture
    {
        private const float StepSeconds = 0.02f;
        private const int SimulationSteps = 150;
        private const float BodyRadius = 0.25f;
        private const float LaunchSpeed = 5f;
        private const float ContainedDistance = 2.5f;
        private const float EscapedDistance = 5f;

        private static readonly Vector3 RigOrigin = new Vector3(0f, 1000f, 0f);

        private SimulationMode _previousSimulationMode;

        [SetUp]
        public void SetUp()
        {
            _previousSimulationMode = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
        }

        // NUnit runs this even when the test failed or threw, and a derived teardown runs before the
        // base fixture's, so the editor gets its simulation mode back before anything is destroyed.
        [TearDown]
        public void TearDown()
        {
            Physics.simulationMode = _previousSimulationMode;
        }

        // ---- Containment ---------------------------------------------------------

        [Test]
        public void Simulate_DynamicBodyInsideBoxWithOutwardVelocity_StaysContained()
        {
            CreateBoxContainer(RigOrigin, 4f);
            var body = CreateBody(RigOrigin, new Vector3(LaunchSpeed, 0f, 0f));

            Simulate(SimulationSteps);

            AssertContained(body);
        }

        [Test]
        public void Simulate_DynamicBodyWithoutInverseCollider_EscapesVolume()
        {
            // The control: same body, same velocity, no container. Without it the run is 15 m of
            // straight-line travel, which is what makes the other three tests mean anything.
            var body = CreateBody(RigOrigin, new Vector3(LaunchSpeed, 0f, 0f));

            Simulate(SimulationSteps);

            AssertEscaped(body);
        }

        [Test]
        public void Simulate_BodyEnteringFromOutside_PassesWallAndIsThenContained()
        {
            CreateBoxContainer(RigOrigin, 4f);
            var body = CreateBody(RigOrigin + new Vector3(10f, 0f, 0f), new Vector3(-LaunchSpeed, 0f, 0f));

            Simulate(SimulationSteps);

            // Inbound is free — the walls are single-sided — so the body crosses in and is then held.
            AssertContained(body);
        }

        [Test]
        public void Simulate_DynamicBodyInsideSphere_StaysContained()
        {
            CreateSphereContainer(RigOrigin, 2f);
            var body = CreateBody(RigOrigin, new Vector3(LaunchSpeed, 0f, 0f));

            Simulate(SimulationSteps);

            AssertContained(body);
        }

        // ---- Helpers -------------------------------------------------------------

        // One sync pushes every authored transform into PhysX; after that the simulation owns the
        // poses and syncing again would fight it.
        private static void Simulate(int steps)
        {
            Physics.SyncTransforms();

            for (var i = 0; i < steps; i++)
            {
                Physics.Simulate(StepSeconds);
            }
        }

        private InverseCollider CreateBoxContainer(Vector3 position, float side)
        {
            var container = CreateCollider("BoxContainer");
            container.transform.position = position;
            SetField(container, "size", new Vector3(side, side, side));
            container.Rebuild();
            Assert.IsTrue(container.IsBuilt, "the container failed to build");
            return container;
        }

        private InverseCollider CreateSphereContainer(Vector3 position, float radius)
        {
            var container = CreateCollider("SphereContainer");
            container.transform.position = position;
            SetField(container, "shape", InverseColliderShape.Sphere);
            SetField(container, "radius", radius);
            container.Rebuild();
            Assert.IsTrue(container.IsBuilt, "the container failed to build");
            return container;
        }

        private Rigidbody CreateBody(Vector3 position, Vector3 velocity)
        {
            var host = Track(new GameObject("SimBody"));
            host.transform.position = position;
            host.AddComponent<SphereCollider>().radius = BodyRadius;

            var body = host.AddComponent<Rigidbody>();
            body.useGravity = false;
            // Continuous detection because the walls are zero-thickness single-sided triangles: the
            // library's first physics fixture must not flake on a tunnelling body.
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.linearVelocity = velocity;
            return body;
        }

        // 150 steps is three seconds and 15 m of travel against a 2 m half extent: a body still within
        // 2.5 m of the rig can only have been stopped by a wall, and one past 5 m can only have left.
        private static void AssertContained(Rigidbody body)
        {
            Assert.Less(
                Mathf.Abs(body.position.x - RigOrigin.x),
                ContainedDistance,
                $"the body left the volume (position {body.position})");
        }

        private static void AssertEscaped(Rigidbody body)
        {
            Assert.Greater(
                Mathf.Abs(body.position.x - RigOrigin.x),
                EscapedDistance,
                $"the body was held by something (position {body.position})");
        }
    }
}
