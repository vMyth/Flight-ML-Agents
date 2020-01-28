using MLAgents;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Aircraft
{
    public class AircraftAgent : Agent
    {
        [Header("Movement Parameters")]
        public float thrust = 100000f;
        public float pitchSpeed = 100f;
        public float yawSpeed = 100f;
        public float rollSpeed = 100f;
        public float boostMultiplier = 2f;

        public int NextCheckpointIndex { get; set; }

        //Components to keep track of
        private AircraftArea area;
        new private Rigidbody rigidbody;
        private TrailRenderer trail;

        //Controls
        private float pitchChange = 0f;
        private float smoothPitchChange = 0f;
        private float maxPitchAngle = 45f;
        private float yawChange = 0f;
        private float smoothYawChange = 0f;
        private float rollChange = 0f;
        private float smoothRollChange = 0f;
        private float maxRollAngle = 45f;
        private bool boost;

        public override void InitializeAgent()
        {
            base.InitializeAgent();
            area = GetComponentInParent<AircraftArea>();
            rigidbody = GetComponent<Rigidbody>();
            trail = GetComponent<TrailRenderer>();
        }

        public override void AgentAction(float[] vectorAction)
        {
            //Read values for pitch and yaw
            pitchChange = vectorAction[0]; //up or none
            if (pitchChange == 2) pitchChange = -1f; //down
            yawChange = vectorAction[1]; //turn right or none
            if (pitchChange == 2) pitchChange = -1f; //turn left

            //Read value for boost and enable/disbale renderer
            boost = vectorAction[2] == 1;
            if (boost && !trail.emitting) trail.Clear();
            trail.emitting = boost;

            ProcessMovement();
        }

        private void ProcessMovement()
        {
            //Calculate boost
            float boostModifier = boost ? boostMultiplier : 1f;

            //Apply forward thrust
            rigidbody.AddForce(transform.forward * thrust * boostModifier, ForceMode.Force);

            //Get the current rotation
            Vector3 curRot = transform.rotation.eulerAngles;

            //Calculate the roll angle (between -180 and 180)
            float rollAngle = curRot.z > 180f ? curRot.z - 360f : curRot.z;
            if (yawChange == 0f)
            {
                //Not turning; smoothly roll toward center
                rollChange = -rollAngle / maxRollAngle;
            }
            else
            {
                //Turning; roll in opposite direction of turn
                rollChange = -yawChange;
            }

            //Calculate smooth deltas
            smoothPitchChange = Mathf.MoveTowards(smoothPitchChange, pitchChange, 2f * Time.fixedDeltaTime);
            smoothYawChange = Mathf.MoveTowards(smoothYawChange, yawChange, 2f * Time.fixedDeltaTime);
            smoothRollChange = Mathf.MoveTowards(smoothRollChange, rollChange, 2f * Time.fixedDeltaTime);

            //Calculate new pitch, yaw and roll. Clamp pitch and roll.
            float pitch = ClampAngle(curRot.x + smoothPitchChange * Time.fixedDeltaTime * pitchSpeed, -maxPitchAngle, maxPitchAngle);
            float yaw = curRot.y + smoothYawChange * Time.fixedDeltaTime * yawSpeed;
            float roll = ClampAngle(curRot.z + smoothRollChange + Time.fixedDeltaTime * rollSpeed, -maxRollAngle, maxRollAngle);

            //Set the nre rotation
            transform.rotation = Quaternion.Euler(pitch, yaw, roll);
        }

        private static float ClampAngle(float angle, float from, float to)
        {
            if (angle < 0f) angle = 360f + angle;
            if (angle > 180f) return Mathf.Max(angle, 360f + from);
            return Mathf.Min(angle, to);
        }
    }
}