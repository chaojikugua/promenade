#include "ControllerMovementRecorderComponent.h"
#include "ControllerComponent.h"
#include "ControllerSystem.h"
#include <assert.h>
#include <ToString.h>


ControllerMovementRecorderComponent::ControllerMovementRecorderComponent()
{
	m_fdWeight = 100.0f; // deviation from reference motion
	m_fvWeight = 5.0f;   // deviation from desired speed
	m_fhWeight = 0.5f;	 // acceleration of head
	m_frWeight = 0.01f;	 // whole body rotation
	m_fpWeight = 0.0f;	 // movement distance
}


double ControllerMovementRecorderComponent::evaluate( bool p_dbgPrint )
{
	double fv = evaluateFV();
	double fr = evaluateFR();
	double fh = evaluateFH();
	double fp = evaluateFP();
	double fd = evaluateFD();
	double fobj = (double)m_fdWeight*fd + (double)m_fvWeight*fv + (double)m_frWeight*fr + (double)m_fhWeight*fh - (double)m_fpWeight*fp;
	if (p_dbgPrint)
	{
		DEBUGPRINT(((ToString(fobj) + " = fd" + ToString((double)m_fdWeight*fd) +" = fv" + ToString((double)m_fvWeight*fv) + " + fr" + ToString((double)m_frWeight*fr) + " + fh" + ToString((double)m_fhWeight*fh) + " - fp" + ToString((double)m_fpWeight*fp)+"\n").c_str()));
	}
	return fobj;
}

void ControllerMovementRecorderComponent::fv_calcStrideMeanVelocity(ControllerComponent* p_controller,
	ControllerSystem* p_system, bool p_forceStore /*= false*/)
{
	GaitPlayer* player = &p_controller->m_player;
	bool restarted = player->checkHasRestartedStride_AndResetFlag();
	ControllerSystem::VelocityStat& velocities = p_system->getControllerVelocityStat(p_controller);
	if (!restarted && !p_forceStore)
	{
		m_temp_currentStrideVelocities.push_back(velocities.m_currentVelocity);
		m_temp_currentStrideDesiredVelocities.push_back(velocities.m_desiredVelocity);
	}
	else
	{
		glm::vec3 totalVelocities(0.0f), totalDesiredVelocities(0.0f);
		for (int i = 0; i < m_temp_currentStrideVelocities.size(); i++)
		{
			totalVelocities += m_temp_currentStrideVelocities[i];
			// force straight movement behavior from tests, set desired coronal velocity to constant zero:
			totalDesiredVelocities += glm::vec3(0.0f, 0.0f, m_temp_currentStrideDesiredVelocities[i].z);
		}
		totalVelocities /= max(1.0f, (float)m_temp_currentStrideVelocities.size());
		totalDesiredVelocities /= max(1.0f, (float)m_temp_currentStrideDesiredVelocities.size());
		// add to lists
		m_fvVelocityDeviations.push_back((double)glm::length(totalVelocities - totalDesiredVelocities));
		//
		m_temp_currentStrideVelocities.clear();
		m_temp_currentStrideDesiredVelocities.clear();
	}
}

void ControllerMovementRecorderComponent::fr_calcRotationDeviations(ControllerComponent* p_controller, ControllerSystem* p_system)
{
	unsigned int legframes = p_controller->getLegFrameCount();
	GaitPlayer* player = &p_controller->m_player;
	if (m_frBodyRotationDeviations.size() < legframes) m_frBodyRotationDeviations.resize(legframes);
	for (int i = 0; i < legframes; i++)
	{
		ControllerComponent::LegFrame* lf = p_controller->getLegFrame(i);
		glm::quat currentDesiredOrientation = lf->getCurrentDesiredOrientation(player->getPhase());
		glm::quat currentOrientation = MathHelp::getMatrixRotation(p_system->getLegFrameTransform(lf));
		glm::quat diff = glm::inverse(currentOrientation) * currentDesiredOrientation;
		glm::vec3 axis; float angle;
		MathHelp::quatToAngleAxis(diff, angle, axis);
		m_frBodyRotationDeviations[i].push_back(TODEG*angle);
	}
}

void ControllerMovementRecorderComponent::fh_calcHeadAccelerations( ControllerComponent* p_controller, ControllerSystem* p_system )
{
	unsigned int headJointId = p_controller->getHeadJointId();
	glm::vec3 acceleration = p_system->getJointAcceleration(headJointId);
	m_fhHeadAcceleration.push_back((double)glm::length(acceleration));
}

void ControllerMovementRecorderComponent::fd_calcReferenceMotion( ControllerComponent* p_controller, ControllerSystem* p_system )
{
	unsigned int idx = p_controller->m_sysIdx;
	ControllerSystem::VelocityStat velstat = p_system->getControllerVelocityStat(p_controller);
	unsigned int legFrames = p_controller->getLegFrameCount();
	// totalscores
	double lenFt = 0.0, lenKnees = 0.0, lenHips = 0.0, lenBod = 0.0, lenHd = 0.0, lenDist=0.0;
	// step through each lf and add score
	for (unsigned int i = 0; i < legFrames; i++)
	{
		ControllerComponent::LegFrame* lf = p_controller->getLegFrame(i);
		glm::vec3 lfPos = p_system->getLegFramePosition(lf);
		double tlenBod = (double)lfPos.y - (double)lf->m_height; // assuming ground is 0!!
		tlenBod *= tlenBod; // sqr
		double tlenHd = 0.0; // head height
		tlenHd *= tlenHd; // sqr
		lenBod += tlenBod;
		lenHd += tlenBod;
	}
	/*double ghostDist = (double)(m_ghostController.position.z - m_ghostStart.z);
	double controllerDist = (double)(m_myController.transform.position.z - m_mycontrollerStart.z);
	double lenDist = ghostDist - controllerDist;
	if (controllerDist < 0.0) lenDist *= 2.0; // penalty for falling or walking backwards
	lenDist *= lenDist; // sqr

	double lenFt = 0.0;
	double lenHips = 0.0;
	double lenKnees = 0.0;
	Vector3 wantedWPos = new Vector3(m_myController.transform.position.x, m_origBodyHeight, m_ghostController.position.z - m_ghostStart.z + m_mycontrollerStart.z);
	wantedWPos = new Vector3(m_myController.transform.position.x, m_origBodyHeight, m_myController.transform.position.z);
	for (int i = 0; i < m_myLegFrame.m_feet.Length; i++)
	{
		Vector3 footRefToFoot = (m_myLegFrame.m_feet[i].transform.position - wantedWPos) - (m_referenceHandler.m_foot[i].position - m_ghostController.position);
		Vector3 hipRefToHip = (m_myController.m_joints[(i * 2) + 1].transform.position - wantedWPos) - (m_referenceHandler.m_IK[i].m_hipPos - m_ghostController.position);
		Vector3 kneeRefToKnee = (m_myController.m_joints[(i + 1) * 2].transform.position - wantedWPos) - (m_referenceHandler.m_knee[i].position - m_ghostController.position);
		Debug.DrawLine(m_myLegFrame.m_feet[i].transform.position,
			m_myLegFrame.m_feet[i].transform.position - footRefToFoot, Color.white);
		Debug.DrawLine(m_myController.m_joints[(i * 2) + 1].transform.position,
			m_myController.m_joints[(i * 2) + 1].transform.position - hipRefToHip, Color.yellow*2.0f);
		Debug.DrawLine(m_myController.m_joints[(i + 1) * 2].transform.position,
			m_myController.m_joints[(i + 1) * 2].transform.position - kneeRefToKnee, Color.yellow);

		lenFt += (double)Vector3.SqrMagnitude(footRefToFoot);
		lenHips += (double)Vector3.SqrMagnitude(hipRefToHip);
		lenKnees += (double)Vector3.SqrMagnitude(kneeRefToKnee);
	}*/
	m_fdBodyHeightSqrDiffs.push_back(/*lenFt * 0.4 + lenKnees + lenHips + */lenBod + 2.0f*lenHd + 0.1 * lenDist);
}

void ControllerMovementRecorderComponent::fp_calcMovementDistance(ControllerComponent* p_controller, ControllerSystem* p_system)
{
	m_fpMovementDist.push_back(p_system->getControllerVelocityStat(p_controller).m_currentVelocity);
	m_fvVelocityGoal = p_system->getControllerVelocityStat(p_controller).m_goalVelocity;
}

double ControllerMovementRecorderComponent::evaluateFV()
{
	double total = 0.0f;
	// mean
	for (auto n : m_fvVelocityDeviations)
	{
		total += n;
	}
	double avg = total /= max(1.0, ((double)(m_fvVelocityDeviations.size())));
	double totmeandiffsqr = 0.0f;
	// std
	for (auto n : m_fvVelocityDeviations)
	{
		double mdiff = n - avg;
		totmeandiffsqr += mdiff * mdiff;
	}
	double sdeviation = sqrt(totmeandiffsqr / max(1.0, ((double)m_fvVelocityDeviations.size())));
	return avg;
}

double ControllerMovementRecorderComponent::evaluateFR()
{
	double total = 0.0f;
	int sz = 0;
	// mean
	for (int x = 0; x < m_frBodyRotationDeviations.size();x++)
	for (int y = 0; y < m_frBodyRotationDeviations[x].size(); y++)
	{
		total += (double)m_frBodyRotationDeviations[x][y];
		sz++;
	}
	double avg = total /= max(1, (double)(sz));
	return avg;
}

double ControllerMovementRecorderComponent::evaluateFH()
{
	double total = 0.0f;
	int sz = 0;
	// mean
	for(auto n : m_fhHeadAcceleration)
	{
		total += n;
		sz++;
	}

	double avg = total /= max(1.0, (double)(sz));
	return avg;
}

double ControllerMovementRecorderComponent::evaluateFD()
{
	double total = 0.0f;
	// mean
	for (auto n : m_fdBodyHeightSqrDiffs)
	{
		total += n;
	}
	double avg = total /= max(1.0, (double)(m_fdBodyHeightSqrDiffs.size()));
	double totmeandiffsqr = 0.0f;
	// std
	for (auto n : m_fdBodyHeightSqrDiffs)
	{
		double mdiff = n - avg;
		totmeandiffsqr += mdiff * mdiff;
	}
	double sdeviation = sqrt(totmeandiffsqr / max(1.0, (double)m_fdBodyHeightSqrDiffs.size()));
	return avg;
}

double ControllerMovementRecorderComponent::evaluateFP()
{
	glm::vec3 total;
	// mean
	for (auto n : m_fpMovementDist)
	{
		total += n;
	}
	float movementSign = max(0.1f, m_fvVelocityGoal.z) / abs(max(0.1f, m_fvVelocityGoal.z));
	if (movementSign == 0.0) movementSign = 1.0f;
	double scoreInRightDir = max(0.0f, total.z * movementSign);
	return scoreInRightDir;
}