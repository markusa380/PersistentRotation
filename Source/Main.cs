using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System;
using UnityEngine;
using PersistentRotation;

namespace PersistentRotation
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    class Main : MonoBehaviour
    {
        public const float threshold = 0.05f;

        public static Main instance { get; private set; }
        private Data data;

        public Vessel activeVessel;

        private void Awake()
        {
            instance = this;

            GameEvents.onVesselGoOnRails.Add(OnVesselGoOnRails);
            GameEvents.onVesselGoOffRails.Add(OnVesselGoOffRails);
            GameEvents.onVesselCreate.Add(OnVesselCreate);
            GameEvents.onVesselWillDestroy.Add(OnVesselWillDestroy);
            GameEvents.onGameStateSave.Add(OnGameStateSave);
        }

        private void Start()
        {
            activeVessel = FlightGlobals.ActiveVessel;
            data = Data.instance;
        }
        private void FixedUpdate()
        {
            if (activeVessel != FlightGlobals.ActiveVessel)
            {
                activeVessel = FlightGlobals.ActiveVessel;
                Interface.instance.desired_rpm_str = data.FindPRVessel(activeVessel).desired_rpm.ToString();
            }

            foreach (Vessel vessel in FlightGlobals.Vessels)
            {
                Data.PRVessel v = data.FindPRVessel(vessel);
                if(v.use_default_reference)
                {
                    if (!v.reference.Equals(vessel.mainBody)) //Default Mode; Continous update of reference to mainBody
                    {
                        v.reference = vessel.mainBody;
                        v.direction = v.reference.GetTransform().position - vessel.transform.position;
                        v.rotation = vessel.transform.rotation;
                        v.last_active = false;
                    }
                }

                if (vessel.packed)
                {
                    #region ### PACKED ###
                    if (vessel.loaded) //is okay, rotation doesnt need to be persistent when rotating
                    {
                        if (!v.momentum_mode_active && vessel.Autopilot.Enabled && vessel.IsControllable && v.momentum.magnitude < threshold) //C1
                        {
                            if (v.rotation_mode_active == true && v.reference != null) //C2
                            {
                                if (v.reference == v.last_reference)
                                {
                                    PackedRotation(v);
                                }
                            }
                        }
                        else
                        {
                            PackedSpin(v); //NOT CONTROLLABLE
                        }
                    }

                    v.last_active = false;

                    #endregion
                }
                else
                {
                    #region ### UNPACKED ###
                    //Update Momentum when unpacked
                    if (!v.momentum_mode_active && vessel.Autopilot.Enabled && vessel.IsControllable && vessel.angularVelocity.magnitude < threshold) //C1
                    {
                        v.momentum = Vector3.zero;
                    }
                    else
                    {
                        v.momentum = vessel.angularVelocity;
                    }

                    //Apply Momentum to activeVessel using Fly-By-Wire
                    if (v.momentum_mode_active && vessel.Autopilot.Enabled) //C1 \ IsControllable
                    {
                        float desired_rpm = (vessel.angularVelocity.magnitude * 60f * (1f / Time.fixedDeltaTime)) / 360f;
                        if (v.desired_rpm >= 0)
                        {
                            vessel.ctrlState.roll = Mathf.Clamp((v.desired_rpm - desired_rpm), -1f, +1f);
                        }
                        else
                        {
                            vessel.ctrlState.roll = -Mathf.Clamp((-v.desired_rpm - desired_rpm), -1f, +1f);
                        }
                    }

                    //Update rotation
                    v.rotation = vessel.transform.rotation;

                    //Adjust SAS for Relative Rotation
                    if (v.rotation_mode_active && v.reference != null) //C2
                    {
                        //Update direction
                        v.direction = v.reference.GetTransform().position - vessel.transform.position;

                        if (!v.momentum_mode_active && vessel.Autopilot.Enabled && vessel.Autopilot.Mode == VesselAutopilot.AutopilotMode.StabilityAssist)
                        {
                            if (v.last_active && v.reference == v.last_reference)
                            {
                                AdjustSAS(v);
                            }
                            v.last_active = true;
                        }
                        else
                        {
                            v.last_active = false;
                        }

                        v.last_position = (Vector3d)v.last_transform.position - v.reference.GetTransform().position;
                    }
                    else
                    {
                        v.direction = Vector3.zero;
                        v.last_position = Vector3.zero;
                        v.last_active = false;
                    }
                    #endregion
                }

                v.last_transform = vessel.ReferenceTransform;
                v.last_reference = v.reference;
            }
        }
        private void OnDestroy()
        {
            instance = null;
            //Unbind functions from GameEvents
            GameEvents.onVesselGoOnRails.Remove(OnVesselGoOnRails);
            GameEvents.onVesselGoOffRails.Remove(OnVesselGoOffRails);
            GameEvents.onVesselCreate.Remove(OnVesselCreate);
            GameEvents.onVesselWillDestroy.Remove(OnVesselWillDestroy);
            GameEvents.onGameStateSave.Remove(OnGameStateSave);
        }
        private void OnGameStateSave(ConfigNode config)
        {
            if (data)
            {
                data.Save();
            }
        }

        private void OnVesselCreate(Vessel vessel)
        {
            //Wait for Vessel to be created
            StartCoroutine(LateGenerate(vessel));
        }
        private IEnumerator LateGenerate(Vessel vessel)
        {
            yield return new WaitForEndOfFrame();
            data.Generate(vessel);
            Data.PRVessel v = data.FindPRVessel(vessel);

            v.last_position = Vector3.zero;
            v.last_active = false;
            v.last_reference = null;
            v.last_transform = vessel.ReferenceTransform;
        }

        private void OnVesselWillDestroy(Vessel vessel)
        {
            Debug.Log("[PR] Deleting " + vessel.vesselName + " as reference.");

            foreach (Vessel _vessel in FlightGlobals.Vessels)
            {
                Data.PRVessel v = data.FindPRVessel(_vessel);

                if (!object.ReferenceEquals(_vessel, v))
                {
                    if (object.ReferenceEquals(vessel, v.reference))
                    {
                        v.reference = null;
                    }
                }
            }
        }

        private void OnVesselGoOnRails(Vessel vessel)
        {
             //Nothing to do here
        }
        private void OnVesselGoOffRails(Vessel vessel)
        {
            Data.PRVessel v = data.FindPRVessel(vessel);
            if (vessel.situation != Vessel.Situations.LANDED || vessel.situation != Vessel.Situations.SPLASHED)
            {
                if (vessel.ActionGroups[KSPActionGroup.SAS] && vessel.IsControllable && !v.momentum_mode_active && v.rotation_mode_active && v.momentum.magnitude < threshold) //vessel.Autopilot.Enabled does not work at this point!
                {
                    //Reset momentum_mode_active heading
                    vessel.Autopilot.SAS.lockedHeading = vessel.ReferenceTransform.rotation;

                    //Set relative rotation if there is a reference
                    if (v.reference != null)
                    {
                        vessel.SetRotation(Quaternion.FromToRotation(v.direction, v.reference.GetTransform().position - vessel.transform.position) * v.rotation);
                    }
                }
                else
                {
                    Vector3 av = v.momentum;
                    Vector3 COM = vessel.findWorldCenterOfMass();
                    Quaternion rotation;
                    rotation = vessel.ReferenceTransform.rotation;

                    //Applying force on every part
                    foreach (Part p in vessel.parts)
                    {
                        try
                        {
                            if (p.GetComponent<Rigidbody>() == null) continue;
                            p.GetComponent<Rigidbody>().AddTorque(rotation * av, ForceMode.VelocityChange);
                            p.GetComponent<Rigidbody>().AddForce(Vector3.Cross(rotation * av, (p.GetComponent<Rigidbody>().position - COM)), ForceMode.VelocityChange);
                        }
                        catch (NullReferenceException nre)
                        {
                            Debug.Log("[PR] NullReferenceException in OnVesselGoOffRails: " + nre.Message);
                        }
                    }
                }
            }
        }
        private void PackedSpin(Data.PRVessel v)
        {
            
            if(v.vessel.situation != Vessel.Situations.LANDED || v.vessel.situation != Vessel.Situations.SPLASHED)
                v.vessel.SetRotation(Quaternion.AngleAxis(v.momentum.magnitude * TimeWarp.CurrentRate, v.vessel.ReferenceTransform.rotation * v.momentum) * v.vessel.transform.rotation);
        }
        private void PackedRotation(Data.PRVessel v)
        {
            if (v.vessel.situation != Vessel.Situations.LANDED || v.vessel.situation != Vessel.Situations.SPLASHED)
                v.vessel.SetRotation(Quaternion.FromToRotation(v.direction, v.reference.GetTransform().position - v.vessel.transform.position) * v.rotation);
        }
        private void AdjustSAS(Data.PRVessel v)
        {
            if (v.reference != null)
            {
                if (v.last_transform != null && v.last_position != null)
                {
                    Vector3d newPosition = (Vector3d)v.last_transform.position - v.reference.GetTransform().position;
                    QuaternionD delta = FromToRotation(v.last_position, newPosition);
                    QuaternionD adjusted = delta * (QuaternionD)v.vessel.Autopilot.SAS.lockedHeading;
                    v.vessel.Autopilot.SAS.lockedHeading = adjusted;
                }
            }
        }

        public QuaternionD FromToRotation(Vector3d fromv, Vector3d tov) //Stock FromToRotation() doesn't work correctly
        {
            Vector3d cross = Vector3d.Cross(fromv, tov);
            double dot = Vector3d.Dot(fromv, tov);
            double wval = dot + Math.Sqrt(fromv.sqrMagnitude * tov.sqrMagnitude);
            double norm = 1.0 / Math.Sqrt(cross.sqrMagnitude + wval * wval);
            return new QuaternionD(cross.x * norm, cross.y * norm, cross.z * norm, wval * norm);
        }
    }
}
