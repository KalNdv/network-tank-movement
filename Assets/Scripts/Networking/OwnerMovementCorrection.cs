using UnityEngine;
using NetworkTankArena.Tanks;

namespace NetworkTankArena.Networking
{
    internal sealed class OwnerMovementCorrection
    {
        // Pending server correction, applied over the remaining time.
        private Vector3 remainingPositionCorrection;
        private Quaternion remainingRotationCorrection = Quaternion.identity;
        private float remainingCorrectionSeconds;

        internal bool Begin(Pose previousPose, Pose authoritativePose, float smoothingDurationSeconds,
            float maximumPositionCorrectionSpeed = float.MaxValue, float maximumRotationCorrectionSpeed = float.MaxValue)
        {
            // Replace any unfinished correction with the newest server update.
            Reset();
            float positionError = Vector3.Distance(previousPose.position, authoritativePose.position);
            float rotationError = Quaternion.Angle(previousPose.rotation, authoritativePose.rotation);
            // Invalid settings and large errors keep the server's immediate correction.
            if (!TankMath.IsFinite(smoothingDurationSeconds) || smoothingDurationSeconds <= 0f ||
                !TankMath.IsFinite(positionError) || positionError > 3f ||
                !TankMath.IsFinite(rotationError) || rotationError > 45f)
                return false;

            // Store the movement and rotation needed to reach the server pose.
            remainingPositionCorrection = authoritativePose.position - previousPose.position;
            remainingRotationCorrection = authoritativePose.rotation * Quaternion.Inverse(previousPose.rotation);
            remainingCorrectionSeconds = smoothingDurationSeconds;
            // Extend catch-up time so corrections cannot overpower full-speed movement or turning.
            if (TankMath.IsFinite(maximumPositionCorrectionSpeed) && maximumPositionCorrectionSpeed > 0f)
                remainingCorrectionSeconds = Mathf.Max(remainingCorrectionSeconds, positionError / maximumPositionCorrectionSpeed);
            if (TankMath.IsFinite(maximumRotationCorrectionSpeed) && maximumRotationCorrectionSpeed > 0f)
                remainingCorrectionSeconds = Mathf.Max(remainingCorrectionSeconds, rotationError / maximumRotationCorrectionSpeed);
            return true;
        }

        internal void Apply(Transform tankTransform, float deltaTime)
        {
            if (remainingCorrectionSeconds <= 0f || !TankMath.IsFinite(deltaTime) || deltaTime <= 0f)
                return;

            // Apply this step's share, then keep only the correction that remains.
            float correctionFraction = Mathf.Clamp01(deltaTime / remainingCorrectionSeconds);
            Vector3 positionAdjustment = remainingPositionCorrection * correctionFraction;
            Quaternion rotationAdjustment = Quaternion.Slerp(Quaternion.identity, remainingRotationCorrection, correctionFraction);
            tankTransform.SetPositionAndRotation(tankTransform.position + positionAdjustment,
                rotationAdjustment * tankTransform.rotation);
            remainingPositionCorrection -= positionAdjustment;
            remainingRotationCorrection *= Quaternion.Inverse(rotationAdjustment);
            remainingCorrectionSeconds = Mathf.Max(0f, remainingCorrectionSeconds - deltaTime);
        }

        internal void Reset()
        {
            // Identity means no rotation; zero remaining time stops correction updates.
            remainingPositionCorrection = Vector3.zero;
            remainingRotationCorrection = Quaternion.identity;
            remainingCorrectionSeconds = 0f;
        }
    }
}
