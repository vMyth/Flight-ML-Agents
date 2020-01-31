﻿using MLAgents;
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

        [Header("Explosion Stuff")]
        [Tooltip("The aircraft mesh that will disappear on explosion")]
        public GameObject meshObject;

        [Tooltip("The game object of the explosion particle effect")]
        public GameObject explosionEffect;

        [Header("Training")]
        [Tooltip("Number of steps to time out after in training")]
        public int stepTimeout = 300;

        public int NextCheckpointIndex { get; set; }

        //Components to keep track of
        private AircraftArea area;
        new private Rigidbody rigidbody;
        private TrailRenderer trail;
        private RayPerception3D rayPerception;

        //When the next step timeout will be during training
        public float nextStepTimeout;

        //Whether the aircraft is frozen (intentionally not flying)
        private bool frozen = false;

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
            rayPerception = GetComponent<RayPerception3D>();

            //Override the max step set in the inspector
            //Max 5000 steps if training, infinite steps if racing
            agentParameters.maxStep = area.trainingMode ? 5000 : 0;
        }

        public override void AgentAction(float[] vectorAction)
        {
            // Read values for pitch and yaw
            pitchChange = vectorAction[0]; // up or none
            if (pitchChange == 2) pitchChange = -1f; // down
            yawChange = vectorAction[1]; // turn right or none
            if (yawChange == 2) yawChange = -1f; // turn left

            // Read value for boost and enable/disable trail renderer
            boost = vectorAction[2] == 1;
            if (boost && !trail.emitting) trail.Clear();
            trail.emitting = boost;

            if (frozen) return;

            ProcessMovement();

            if (area.trainingMode)
            {
                //Small negative reward every step
                AddReward(-1f / agentParameters.maxStep);

                //Make sure we haven't run out of time if training 
                if (GetStepCount() > nextStepTimeout)
                {
                    AddReward(-.5f);
                    Done();
                }

                Vector3 localCheckpointDir = VectorToNextCheckpoint();
                if (localCheckpointDir.magnitude < area.AircraftAcademy.FloatProperties.GetPropertyWithDefault("checkpoint_radius", 0f))
                {
                    GotCheckpoint();
                }
            }
        }

        public override void CollectObservations()
        {
            //Observe aircraft velocity (1 vector3 = 3 values)
            AddVectorObs(transform.InverseTransformDirection(rigidbody.velocity));

            //Where is the next checkoint? (1 vector3 = 3 values)
            AddVectorObs(VectorToNextCheckpoint());

            //Orientation of the next checkpoint (1 vector3 = 3 values)
            Vector3 nextCheckpointForward = area.Checkpoints[NextCheckpointIndex].transform.forward;
            AddVectorObs(transform.InverseTransformDirection(nextCheckpointForward));

            //Observe ray perception results
            string[] detectableObjects = { "untagged", "checkpoint" };

            //Look ahead and upward
            //(2tags + 1 hit/not + 1 dist to obj) * 3 ray angles = 12 values
            AddVectorObs(rayPerception.Perceive(
                rayDistance: 250f,
                rayAngles: new float[] { 60f, 90f, 120f },
                detectableObjects: detectableObjects,
                startOffset: 0f,
                endOffset: 75f
                ));

            //Look center and at several angles along the horizon
            //(2tags + 1 hit/not + 1 dist to obj) * 7 ray angles = 28 values
            AddVectorObs(rayPerception.Perceive(
                rayDistance: 250f,
                rayAngles: new float[] { 60f, 70f, 80f, 90f, 100f, 110f, 120f },
                detectableObjects: detectableObjects,
                startOffset: 0f,
                endOffset: 0f
                ));

            //Look ahead and downward
            //(2tags + 1 hit/not + 1 dist to obj) * 3 ray angles = 12 values
            AddVectorObs(rayPerception.Perceive(
                rayDistance: 250f,
                rayAngles: new float[] { 60f, 90f, 120f },
                detectableObjects: detectableObjects,
                startOffset: 0f,
                endOffset: -75f
                ));

            //Total Observations = 3 + 3 + 3 + 12 + 28 + 12 = 61
        }

        public override void AgentReset()
        {
            //Reset the velocity, position, and orientation
            rigidbody.velocity = Vector3.zero;
            rigidbody.angularVelocity = Vector3.zero;
            trail.emitting = false;
            area.ResetAgenntPosition(agent: this, randomize: area.trainingMode);

            //Update the step timeout if training 
            if (area.trainingMode) nextStepTimeout = GetStepCount() + stepTimeout;
        }

        public void FreezeAgent()
        {
            Debug.Assert(area.trainingMode == false, "Freeze/Thaw not supported in training");
            frozen = true;
            rigidbody.Sleep();
            trail.emitting = false;
        }

        public void ThawAgent()
        {
            Debug.Assert(area.trainingMode == false, "Freeze/Thaw not supported in training");
            frozen = false;
            rigidbody.WakeUp();
        }

        private void GotCheckpoint()
        {
            //Next checkpoint was reached, update
            NextCheckpointIndex = (NextCheckpointIndex + 1) % area.Checkpoints.Count;

            if (area.trainingMode)
            {
                AddReward(.5f);
                nextStepTimeout = GetStepCount() + stepTimeout;
            }
        }

        private Vector3 VectorToNextCheckpoint()
        {
            Vector3 nextCheckpointDir = area.Checkpoints[NextCheckpointIndex].transform.position - transform.position;
            Vector3 localCheckpointDir = transform.InverseTransformDirection(nextCheckpointDir);
            return localCheckpointDir;
        }

        private void ProcessMovement()
        {
            // Calculate boost
            float boostModifier = boost ? boostMultiplier : 1f;

            // Apply forward thrust
            rigidbody.AddForce(transform.forward * thrust * boostModifier, ForceMode.Force);

            // Get the current rotation
            Vector3 curRot = transform.rotation.eulerAngles;

            // Calculate the roll angle (between -180 and 180)
            float rollAngle = curRot.z > 180f ? curRot.z - 360f : curRot.z;
            if (yawChange == 0f)
            {
                // Not turning; smoothly roll toward center
                rollChange = -rollAngle / maxRollAngle;
            }
            else
            {
                // Turning; roll in opposite direction of turn
                rollChange = -yawChange;
            }

            // Calculate smooth deltas
            smoothPitchChange = Mathf.MoveTowards(smoothPitchChange, pitchChange, 2f * Time.fixedDeltaTime);
            smoothYawChange = Mathf.MoveTowards(smoothYawChange, yawChange, 2f * Time.fixedDeltaTime);
            smoothRollChange = Mathf.MoveTowards(smoothRollChange, rollChange, 2f * Time.fixedDeltaTime);

            // Calculate new pitch, yaw, and roll. Clamp pitch and roll.
            float pitch = ClampAngle(curRot.x + smoothPitchChange * Time.fixedDeltaTime * pitchSpeed,
                                        -maxPitchAngle,
                                        maxPitchAngle);
            float yaw = curRot.y + smoothYawChange * Time.fixedDeltaTime * yawSpeed;
            float roll = ClampAngle(curRot.z + smoothRollChange * Time.fixedDeltaTime * rollSpeed,
                                    -maxRollAngle,
                                    maxRollAngle);

            // Set the new rotation
            transform.rotation = Quaternion.Euler(pitch, yaw, roll);
        }

        private static float ClampAngle(float angle, float from, float to)
        {
            if (angle < 0f) angle = 360f + angle;
            if (angle > 180f) return Mathf.Max(angle, 360f + from);
            return Mathf.Min(angle, to);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (other.transform.CompareTag("checkpoint") && other.gameObject == area.Checkpoints[NextCheckpointIndex])
            {
                GotCheckpoint();
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (!collision.transform.CompareTag("agent"))
            {
                //We hit something that wasn't another agent
                AddReward(-1f);
                Done();
                return;
            }
            else
            {
                StartCoroutine(ExplosionReset());
            }
        }

        private IEnumerator ExplosionReset()
        {
            FreezeAgent();

            //Disable aircraft meshobject, enable explosion
            meshObject.SetActive(false);
            explosionEffect.SetActive(true);
            yield return new WaitForSeconds(2f);

            //Disable explosion, re-enable aircraft mesh
            meshObject.SetActive(true);
            explosionEffect.SetActive(false);
            area.ResetAgenntPosition(agent: this);
            yield return new WaitForSeconds(1f);

            ThawAgent();
        }
    }
}