﻿using UnityEngine;
using System.Collections;

/*  ===================================================================
 *                             Controller
 *  ===================================================================
 *   The overall locomotion controller.
 *   Contains the top-level controller logic, which sums all
 *   the torques and feeds them to the physics engine.
 *   */

public class Controller : MonoBehaviour 
{
    public LegFrame[] m_legFrames=new LegFrame[1];
    public GaitPlayer m_player;
    private Vector3[] m_jointTorques;
    public Rigidbody[] m_joints;
    public PIDdriverTorque3[] m_jointPD;
    public Vector3 m_currentVelocity;
    public Vector3 m_goalVelocity;
    public Vector3 m_desiredVelocity;

    void Start()
    {
        m_jointTorques = new Vector3[m_joints.Length];
        // hard code for now
        m_legFrames[0].m_neighbourJointIds[(int)LegFrame.LEG.LEFT] = 0;
        m_legFrames[0].m_neighbourJointIds[(int)LegFrame.LEG.RIGHT] = 1;
        m_legFrames[0].m_id = 2;
    }

	
	// Update is called once per frame
	void Update () 
    {
        // Advance the player
        m_player.updatePhase(Time.deltaTime);

        // Update desired velocity
        updateDesiredVelocity(Time.deltaTime);

        // update feet positions
        updateFootStrikePositions();

        // Recalculate all torques for this frame
        updateTorques();

        // Debug color of legs when in stance
        debugColorLegs();
	}

    void FixedUpdate()
    {
        for (int i = 0; i < m_jointTorques.Length; i++)
        {
            Vector3 torque = m_jointTorques[i];
            m_joints[i].AddTorque(torque);
            Debug.DrawLine(m_joints[i].transform.position,m_joints[i].transform.position+torque,new Color(i%2,(i%3)*0.5f,(i+2)%4/3.0f) );
        }
    }

    void OnGUI()
    {
        // Draw step cycles
        for (int i = 0; i < m_legFrames.Length; i++)
        {
            drawStepCycles(m_player.m_gaitPhase, 10.0f+(float)i*10.0f, m_legFrames[i],i);
        }
    }

    void updateFootStrikePositions()
    {
        for (int i = 0; i < m_legFrames.Length; i++)
        {
            m_legFrames[i].updateFootPosForSwingLegs(m_player.m_gaitPhase, m_currentVelocity, m_desiredVelocity);
        }
    }

    void updateTorques()
    {
        float phi = m_player.m_gaitPhase;
        // Get the two variants of torque
        Vector3[] tPD = computePDTorques(phi);
        Vector3[] tVF = computeVFTorques(phi);
        // Sum them
        for (int i = 0; i < m_jointTorques.Length; i++)
        {
            m_jointTorques[i] = tPD[i] + tVF[i];
        }

        // Apply them to the leg frames, also
        // feed back corrections for hip joints
        for (int i = 0; i < m_legFrames.Length; i++)
        {
            m_jointTorques = m_legFrames[i].applyNetLegFrameTorque(m_jointTorques, phi);
        }
    }

    // Compute the torque of all PD-controllers in the joints
    Vector3[] computePDTorques(float p_phi)
    {
        // TEMPORARY TEST CODE!!!!!!!!!!!!
         // right now, just fetch the old torque
        // but only for stance legs, we see this as
        // their simulation of not being controlled
        //by setting every other joint to zero, we simulate
        // their control, ie. the PD.
         Vector3[] newTorques = new Vector3[m_jointTorques.Length];
         for (int i = 0; i < m_legFrames.Length; i++)
         {
             LegFrame lf = m_legFrames[i];
             newTorques[lf.m_id] = m_jointTorques[lf.m_id];
             for (int n = 0; n < lf.m_tuneStepCycles.Length; n++)
             {
                 StepCycle cycle = lf.m_tuneStepCycles[n];
                 int jointID = lf.m_neighbourJointIds[n];
                 //if (cycle.isInStance(m_player.m_gaitPhase))
                 {
                     newTorques[jointID] = m_jointTorques[jointID];
                 }
             }
         }
        return newTorques;
    }


    // Compute the torque of all applied virtual forces
    Vector3[] computeVFTorques(float p_phi)
    {
        return new Vector3[m_jointTorques.Length];
    }

    // Function for deciding the current desired velocity in order
    // to reach the goal velocity
    void updateDesiredVelocity(float p_dt)
    {
        float goalSqrMag=m_goalVelocity.sqrMagnitude;
        float currentSqrMag=m_goalVelocity.sqrMagnitude;
        float stepSz = 0.5f * p_dt;
        // Note the material doesn't mention taking dt into 
        // account for the step size, they might be running fixed timestep
        //
        // If the goal is faster
        if (goalSqrMag>currentSqrMag)
        {
            // Take steps no bigger than 0.5m/s
            if (goalSqrMag < currentSqrMag + stepSz)
                m_currentVelocity=m_goalVelocity;
            else
                m_currentVelocity += m_currentVelocity.normalized * stepSz;
        }
        else // if the goal is slower
        {
            // Take steps no smaller than 0.5
            if (goalSqrMag > currentSqrMag - stepSz)
                m_currentVelocity=m_goalVelocity;
            else
                m_currentVelocity -= m_currentVelocity.normalized * stepSz;
        }
    }

    void debugColorLegs()
    {
        for (int i = 0; i < m_legFrames.Length; i++)
        {
            LegFrame lf = m_legFrames[i];
            for (int n = 0; n < lf.m_tuneStepCycles.Length; n++)
            {
                StepCycle cycle = lf.m_tuneStepCycles[n];
                Rigidbody current = m_joints[lf.m_neighbourJointIds[n]];
                if (cycle.isInStance(m_player.m_gaitPhase))
                {
                    current.gameObject.renderer.material.color = Color.yellow;
                }
                else
                {
                    current.gameObject.renderer.material.color = Color.white;
                }
            }
        }
    }

    void drawStepCycles(float p_phi,float p_yOffset,LegFrame p_frame, int legFrameId)
    {
        for (int i = 0; i < LegFrame.c_legCount; i++)
        {
            StepCycle cycle = p_frame.m_tuneStepCycles[i];
            if (cycle!=null)
            {
                // DRAW!
                float timelineLen = 300.0f;
                float xpad = 10.0f;
                float offset = cycle.m_tuneStepTrigger;
                float len = cycle.m_tuneDutyFactor;
                float lineStart = xpad;
                float lineEnd = lineStart + timelineLen;
                float dutyEnd = lineStart + timelineLen * (offset + len);
                float w = 4.0f;                
                float y = p_yOffset+(float)i*w*2.0f;
                bool stance = cycle.isInStance(p_phi);
                // Draw back
                Color ucol = Color.white*0.5f+new Color((float)(legFrameId%2), (float)(i%2), 1-(float)(i%2),1.0f);
                Debug.Log(ucol.ToString());
                int h = (int)w / 2;
                Drawing.DrawLine(new Vector2(lineStart-1, y-h-1), new Vector2(lineEnd+1, y-h-1), Color.black, 1);
                Drawing.DrawLine(new Vector2(lineStart-1, y+h), new Vector2(lineEnd+1, y+h), Color.black, 1);
                Drawing.DrawLine(new Vector2(lineStart-1, y-h-1), new Vector2(lineStart-1, y+h+1), Color.black, 1);
                Drawing.DrawLine(new Vector2(lineEnd+1, y-h-1), new Vector2(lineEnd+1, y+h), Color.black, 1);
                Drawing.DrawLine(new Vector2(lineStart, y), new Vector2(lineEnd, y), new Color(1.0f,1.0f,1.0f,1.0f), w);
                // Color depending on stance
                Color currentCol = Color.black;
                float phase = cycle.getStancePhase(p_phi);
                if (stance)
                    currentCol = Color.Lerp(ucol, Color.black, phase*phase);

                // draw df
                Drawing.DrawLine(new Vector2(lineStart + timelineLen * offset, y), new Vector2(Mathf.Min(lineEnd, dutyEnd), y), currentCol, w);
                // draw rest if out of bounds
                if (offset + len > 1.0f)
                    Drawing.DrawLine(new Vector2(lineStart, y), new Vector2(lineStart + timelineLen * (offset + len - 1.0f), y), currentCol, w);

                // Draw current time marker
                Drawing.DrawLine(new Vector2(lineStart + timelineLen * p_phi-1, y), new Vector2(lineStart + timelineLen * p_phi + 3, y),
                    Color.red, w);
                Drawing.DrawLine(new Vector2(lineStart + timelineLen * p_phi, y), new Vector2(lineStart + timelineLen * p_phi + 2, y),
                    Color.green*2, w);
            }
        }
    }
}
