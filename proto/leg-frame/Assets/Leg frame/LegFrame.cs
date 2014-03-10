﻿using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/*  ===================================================================
 *                             Leg Frame
 *  ===================================================================
 *   The reference frame for a row of legs.
 *   It contains movement properties for this row, as well as feedback
 *   functionalities for torque handling.
 *   Each joint in the leg has a PD controller providing torque.
 *   The hip-joints do not however.
 *   */

public class LegFrame : MonoBehaviour 
{
    public const int c_legCount = 2; // always two legs per frame
    public enum LEG { LEFT = 0, RIGHT = 1 }
    public enum NEIGHBOUR_JOINTS { HIP_LEFT=0, HIP_RIGHT=1, SPINE=2, COUNT=3 }
    public enum PLANE { CORONAL = 0, SAGGITAL = 1 }
    public enum ORIENTATION { YAW = 0, PITCH = 1, ROLL=2 }

    // The gait for each leg
    public StepCycle[] m_tuneStepCycles = new StepCycle[c_legCount];

    // The id of the leg frame in the global torque list
    public int m_id;

    // The joints which are connected directly to the leg frame
    // Ie. the "hip joints" and closest spine segment. 
    // The hip joints are not driven by PD-controllers
    // and will affect the leg frame torque as well as get corrected
    // torques while in stance.
    public int[] m_neighbourJointIds = new int[(int)NEIGHBOUR_JOINTS.COUNT];

    // NOTE!
    // I've lessened the amount of parameters
    // by letting each leg in a leg frame share
    // per-leg parameters. Effectively mirroring
    // behaviour over the saggital plane.
    // There are still two step cycles though, to
    // allow for phase shifting.

    // PLF, the coronal(x) and saggital(y) step distance
    public Vector2 m_tuneStepLength = new Vector2(3.0f, 5.0f);


    // omegaLF, the desired heading orientation trajectory
    // yaw, pitch, roll
    public PcswiseLinear[] m_tuneOrientationLFTraj = new PcswiseLinear[3];

    // PD-controller and driver for calculating desired torque based
    // on the desired orientation
    public PIDn m_desiredLFTorquePD;

    // Foot controllers
    public Vector3[] m_footLiftPlacement = new Vector3[c_legCount]; // From where the foot was lifted
    public bool[] m_footLiftPlacementPerformed = new bool[c_legCount]; // If foot just took off (and the "old" pos should be updated)
    public Vector3[] m_footStrikePlacement = new Vector3[c_legCount]; // The place on the ground where the foot should strike next
    public Vector3[] m_footTarget = new Vector3[c_legCount]; // The current position in the foot's swing trajectory
    public float m_tuneFootPlacementVelocityScale = 1.0f;
    // hsw, step height trajectory
    public PcswiseLinear m_tuneStepHeightTraj;
    // tsw, step interpolation trajectory (horizontal easing between P1 and P2)
    public PcswiseLinear m_tuneFootTransitionEase;


    void Awake()
    {
        for (int i = 0; i < c_legCount; i++ )
        {
            m_footLiftPlacementPerformed[i]=false;
        }
        for (int i = 0; i < (int)NEIGHBOUR_JOINTS.COUNT; i++ )
        {
            m_neighbourJointIds[i] = -1;
        }
        // The orientation heading trajectory starts out
        // without any compensations (flat).
        foreach (PcswiseLinear traj in m_tuneOrientationLFTraj)
        {
            //traj.m_initAsFunc = PcswiseLinear.INITTYPE.FLAT;
            traj.reset();
        }
        
    }

	// Use this for initialization
	void Start () 
    {
	
	}
	
	// Update is called once per frame
	void Update () 
    {
	
	}

    // Calculate the next position where the foot should be placed for legs in swing
    public void updateFeet(float p_phi, Vector3 p_velocity, Vector3 p_desiredVelocity)
    {
        for (int i = 0; i < c_legCount; i++)
        {
            // The position is updated as long as the leg
            // is in stance. This means that the last calculated
            // position when the foot leaves the ground is used.
            if (m_tuneStepCycles[i].isInStance(p_phi))
            {
                updateFootStrikePosition(i, p_phi, p_velocity, p_desiredVelocity);
            }
            else // If the foot is in swing instead, start updating the current foot swing target
            {    // along the appropriate trajectory.
                updateFootSwingPosition(i, p_phi);
            }
        }
    }

    // Calculate a new position where to place a foot.
    // This is where the foot will try to swing to in its
    // trajectory.
    private void updateFootStrikePosition(int p_idx, float p_phi, Vector3 p_velocity, Vector3 p_desiredVelocity)
    {
        // Set the lift position to the old strike position (used for trajectory planning)
        // the first time each cycle that we enter this function
        if (!m_footLiftPlacementPerformed[p_idx])
        {
            m_footLiftPlacement[p_idx]=m_footStrikePlacement[p_idx];
            m_footLiftPlacementPerformed[p_idx]=true;
        }
        // Calculate the new position
        float mirror=(float)(p_idx*2-1); // flips the coronal axis for the left leg
        Vector3 regularFootPos = transform.TransformPoint(new Vector3(mirror * m_tuneStepLength.x, 0.0f, m_tuneStepLength.y));
        Vector3 finalPos = calculateVelocityScaledFootPos(regularFootPos, p_velocity, p_desiredVelocity);
        m_footStrikePlacement[p_idx] = projectFootPosToGround(finalPos);
    }

    private void updateFootSwingPosition(int p_idx, float p_phi)
    {
        Vector3 oldPos = m_footTarget[p_idx]; // only for debug...
        //
        m_footLiftPlacementPerformed[p_idx]=false; // reset
        // Get the fractional swing phase
        float m_swingPhi = m_tuneStepCycles[p_idx].getSwingPhase(p_phi);
        // The height offset, ie. the "lift" that the foot makes between stepping points.
        Vector3 heightOffset = new Vector3(0.0f, m_tuneStepHeightTraj.getValAt(m_swingPhi), 0.0f);
        // scale the phi based on the easing function, for ground plane movement
        m_swingPhi = getFootTransitionPhase(m_swingPhi);
        // Calculate the position
        // Foot movement along the ground
        Vector3 groundPlacement=Vector3.Lerp(m_footLiftPlacement[p_idx],m_footStrikePlacement[p_idx],m_swingPhi);
        m_footTarget[p_idx] = groundPlacement+heightOffset;
        //
        Color dbg=Color.green;
        if (p_idx==0) 
            dbg = Color.red;
        Debug.DrawLine(oldPos, m_footTarget[p_idx], dbg,2.0f);
    }

    // Project a foot position to the ground beneath it
    private Vector3 projectFootPosToGround(Vector3 p_footPosLF)
    {
        return new Vector3(p_footPosLF.x,0.0f,p_footPosLF.z); // for now, super simple lock to 0
    }

    // Scale a foot strike position prediction to the velocity difference
    private Vector3 calculateVelocityScaledFootPos(Vector3 p_footPosLF,
                                                   Vector3 p_velocity,
                                                   Vector3 p_desiredVelocity)
    {
        return p_footPosLF + (p_velocity - p_desiredVelocity) * m_tuneFootPlacementVelocityScale;
    }

    // Get the phase value in the foot transition based on
    // swing phase. Note the phi variable here is the fraction
    // of the swing phase!
    private float getFootTransitionPhase(float p_swingPhi)
    {
        return m_tuneFootTransitionEase.getValAt(p_swingPhi);
    }

    // Retrieves the current orientation quaternion from the
    // trajectory function at time phi.
    private Quaternion getCurrentDesiredOrientation(float p_phi)
    {
        float yaw = m_tuneOrientationLFTraj[(int)ORIENTATION.YAW].getValAt(p_phi);
        float pitch = m_tuneOrientationLFTraj[(int)ORIENTATION.PITCH].getValAt(p_phi);
        float roll = m_tuneOrientationLFTraj[(int)ORIENTATION.ROLL].getValAt(p_phi);
        return Quaternion.Euler(new Vector3(pitch, yaw, roll));
    }

    // Drives the PD-controller and retrieves the 3-axis torque
    // vector that will be used as the desired torque for which the
    // stance legs tries to accomplish.
    private Vector3 getPDTorque(Quaternion p_desiredOrientation)
    {
        Vector3 torque = m_desiredLFTorquePD.drive(transform.rotation,p_desiredOrientation,Time.deltaTime);
        return torque;
    }

    // Function to get the stance and swing legs 
    // sorted into two separate lists
    private void separateLegsPerPhase(float p_phi, 
                                      ref List<int> p_stanceLegs, 
                                      ref List<int> p_swingLegs)
    {
        for (int i = 0; i < c_legCount; i++)
        {
            // Only need to add the indices
            if (m_tuneStepCycles[i].isInStance(p_phi))
            {
                p_stanceLegs.Add(i);
            }
            else
            {
                p_swingLegs.Add(i);
            }
        }
    }

    // This function applies the current torques to the leg frame
    // and corrects the stance leg torques based on a desirec torque for
    // the leg frame itself.
    public Vector3[] applyNetLegFrameTorque(Vector3[] p_currentTorques, float p_phi)
    {
        // Preparations, get ahold of all legs in stance,
        // all legs in swing. And get ahold of their and the 
        // closest spine's torques.
        List<int> stanceLegs=new List<int>();
        List<int> swingLegs=new List<int>();
        Vector3 tstance=Vector3.zero, tswing=Vector3.zero, tspine=Vector3.zero;
        // Find the swing-, and stance legs
        separateLegsPerPhase(p_phi,ref stanceLegs,ref swingLegs);
        // Sum the torques, go through all ids, retrieve their joint id in
        // the global torque vector, and retrieve the current torque:
        for (int i=0;i<stanceLegs.Count;i++)
            tstance+=p_currentTorques[m_neighbourJointIds[stanceLegs[i]]];
        //
        for (int i=0;i<swingLegs.Count;i++)
            tswing+=p_currentTorques[m_neighbourJointIds[swingLegs[i]]];
        //
        int spineIdx=m_neighbourJointIds[(int)NEIGHBOUR_JOINTS.SPINE];
        if (spineIdx!=-1)
            tspine=p_currentTorques[spineIdx];

        // 1. Calculate current torque for leg frame:
        // tLF = tstance + tswing + tspine.
        // Here the desired torque is feedbacked through the
        // stance legs (see 3) as their current torque
        // is the product of previous desired torque combined
        // with current real-world scenarios.
        Vector3 tLF=tstance+tswing+tspine;
        p_currentTorques[m_id]=tLF;

        // 2. Calculate a desired torque, tdLF, using the previous current
        // torque, tLF, and a PD-controller driving towards the 
        // desired orientation, omegaLF.
        Quaternion omegaLF=getCurrentDesiredOrientation(p_phi);
        Vector3 tdLF = getPDTorque(omegaLF);
        // test code
        //rigidbody.AddTorque(tdLF);

        // 3. Now loop through all legs in stance (N) and
        // modify their torques in the vector according
        // to tstancei = (tdLF −tswing −tspine)/N
        // This is to try to make the stance legs compensate
        // for current errors in order to make the leg frame
        // reach its desired torque.
        int N = stanceLegs.Count;
        for (int i = 0; i < N; i++)
        {
            int sidx=stanceLegs[i];;
            int idx=m_neighbourJointIds[sidx];
            p_currentTorques[idx] = (tdLF - tswing - tspine)/(float)N;
            //if (p_currentTorques[idx].magnitude > 100.0f)
            //{
            //    p_currentTorques[idx].Normalize();
            //    Debug.Log("Normalized!");
            //}
            if (float.IsNaN(p_currentTorques[idx].x))
            {
                Debug.Log("NAN");
                Debug.Log("omegaLF "+omegaLF.ToString());
                Debug.Log("current " + transform.rotation.ToString());
                Quaternion error = omegaLF * Quaternion.Inverse(omegaLF);
                // Separate angle and axis, so we can feed the axis-wise
                // errors to the PIDs.
                float a;
                Vector3 dir;
                error.ToAngleAxis(out a, out dir);
                Debug.Log("omegaLF^-1 " + Quaternion.Inverse(omegaLF).ToString());
                Debug.Log("deltaT " + Time.deltaTime);
                Debug.Log("error " + error.ToString());
                Debug.Log("a " + error.ToString());
                Debug.Log("dir " + dir.ToString());
                Debug.Log("tdLF " + tdLF.ToString());
                Debug.Log("tSwing " + tswing.ToString());
                Debug.Log("tSpine " + tspine.ToString());
                Debug.Log(idx + " N: " + N);
                Debug.Log(p_currentTorques[idx]);
                Time.timeScale = 0.0f;
            }
            
        }

        // Return the vector, now containing the new LF torque
        // as well as any corrected stance-leg torques.
        return p_currentTorques;
    }

    public void OnDrawGizmos()
    {
        for (int i = 0; i < m_footStrikePlacement.Length; i++)
        {
            if (i==0) 
                Gizmos.color = Color.red;
            else
                Gizmos.color = Color.green;
            Gizmos.DrawSphere(m_footStrikePlacement[i],0.5f);
            Gizmos.DrawCube(m_footLiftPlacement[i], Vector3.one*0.5f);
            Gizmos.color *= 1.2f;
            Gizmos.DrawSphere(m_footTarget[i], 0.25f);
        }
        drawLegEstimation();
    }

    private void drawLegEstimation()
    {
        for (int i = 0; i < c_legCount; i++)
        {
            float d = 1.0f;
            if (i == 0)
            {
                Gizmos.color = Color.red;
                d = -1.0f;
            }
            else
                Gizmos.color = Color.green;
            Gizmos.DrawLine(transform.position+transform.right*d, m_footTarget[i]);
        }
    }


}
