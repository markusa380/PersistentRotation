using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System;
using UnityEngine;

namespace PersistentRotation
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    class Main : MonoBehaviour
    {

        public static Main instance { get; private set; }
        private Data data;

        public Dictionary<string, Vector3> lastPosition = new Dictionary<string, Vector3>();
        public Dictionary<string, Transform> lastTransform = new Dictionary<string, Transform>();
        public Dictionary<string, bool> lastActive = new Dictionary<string, bool>();
        public Dictionary<string, ITargetable> lastReference = new Dictionary<string, ITargetable>();

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
            if(activeVessel != FlightGlobals.ActiveVessel)
            {
                activeVessel = FlightGlobals.ActiveVessel;
                Interface.instance.desired_rpm_str = data.desired_rpm[activeVessel.id.ToString()].ToString();
            }

            foreach (Vessel vessel in FlightGlobals.Vessels)
            {
                if(data.use_default_reference[vessel.id.ToString()])
                {
                    if (!data.reference[vessel.id.ToString()].Equals(vessel.mainBody)) //Default Mode; Continous update of reference to mainBody
                    {
                        data.reference[vessel.id.ToString()] = vessel.mainBody;
                        data.direction[vessel.id.ToString()] = data.reference[vessel.id.ToString()].GetTransform().position - vessel.transform.position;
                        data.rotation[vessel.id.ToString()] = vessel.transform.rotation;
                        lastActive[vessel.id.ToString()] = false;
                    }
                }

                if (vessel.packed)
                {
                    #region ### PACKED ###
                    if (vessel.loaded) //is okay, rotation doesnt need to be persistent when rotating
                    {
                        if (!data.momentum_mode_active[vessel.id.ToString()] && vessel.Autopilot.Enabled && vessel.IsControllable) //C1
                        {
                            if (data.rotation_mode_active[vessel.id.ToString()] == true && data.reference[vessel.id.ToString()] != null) //C2
                            {
                                if (data.reference[vessel.id.ToString()] == lastReference[vessel.id.ToString()])
                                {
                                    PackedRotation(vessel);
                                }
                            }
                        }
                        else
                        {
                            PackedSpin(vessel); //NOT CONTROLLABLE
                        }
                    }

                    lastActive[vessel.id.ToString()] = false;

                    #endregion
                }
                else
                {
                    #region ### UNPACKED ###
                    //Update Momentum when unpacked
                    if (!data.momentum_mode_active[vessel.id.ToString()] && vessel.Autopilot.Enabled && vessel.IsControllable) //C1
                    {
                        data.momentum[vessel.id.ToString()] = Vector3.zero;
                    }
                    else
                    {
                        data.momentum[vessel.id.ToString()] = vessel.angularVelocity;
                    }

                    //Apply Momentum to activeVessel using Fly-By-Wire
                    if (data.momentum_mode_active[vessel.id.ToString()] && vessel.Autopilot.Enabled) //C1 \ IsControllable
                    {
                        float desired_rpm = (vessel.angularVelocity.magnitude * 60f * (1f / Time.fixedDeltaTime)) / 360f;
                        if (data.desired_rpm[vessel.id.ToString()] >= 0)
                        {
                            vessel.ctrlState.roll = Mathf.Clamp((data.desired_rpm[vessel.id.ToString()] - desired_rpm), -1f, +1f);
                        }
                        else
                        {
                            vessel.ctrlState.roll = -Mathf.Clamp((-data.desired_rpm[vessel.id.ToString()] - desired_rpm), -1f, +1f);
                        }
                    }

                    //Update rotation
                    data.rotation[vessel.id.ToString()] = vessel.transform.rotation; //MAYBE AAAAAA

                    //Adjust SAS for Relative Rotation
                    if (data.rotation_mode_active[vessel.id.ToString()] && data.reference[vessel.id.ToString()] != null) //C2
                    {
                        //Update direction
                        data.direction[vessel.id.ToString()] = data.reference[vessel.id.ToString()].GetTransform().position - vessel.transform.position;

                        if (!data.momentum_mode_active[vessel.id.ToString()] && vessel.Autopilot.Enabled && vessel.Autopilot.Mode == VesselAutopilot.AutopilotMode.StabilityAssist)
                        {
                            if (lastActive[vessel.id.ToString()] && data.reference[vessel.id.ToString()] == lastReference[vessel.id.ToString()])
                            {
                                AdjustSAS(vessel);
                            }
                            lastActive[vessel.id.ToString()] = true;
                        }
                        else
                        {
                            lastActive[vessel.id.ToString()] = false;
                        }

                        lastPosition[vessel.id.ToString()] = (Vector3d)lastTransform[vessel.id.ToString()].position - data.reference[vessel.id.ToString()].GetTransform().position;
                    }
                    else
                    {
                        data.direction[vessel.id.ToString()] = Vector3.zero;
                        lastPosition[vessel.id.ToString()] = Vector3.zero;
                        lastActive[vessel.id.ToString()] = false;
                    }
                    #endregion
                }

                lastTransform[vessel.id.ToString()] = vessel.ReferenceTransform;
                lastReference[vessel.id.ToString()] = data.reference[vessel.id.ToString()];
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

            lastPosition[vessel.id.ToString()] = Vector3.zero;
            lastActive[vessel.id.ToString()] = false;
            lastReference[vessel.id.ToString()] = null;
            lastTransform[vessel.id.ToString()] = vessel.ReferenceTransform;
        }

        private void OnVesselWillDestroy(Vessel vessel)
        {
            Debug.Log("[PR] Deleting " + vessel.vesselName + " as reference.");

            foreach (Vessel v in FlightGlobals.Vessels)
            {
                if (!data.reference.ContainsKey(v.id.ToString()))
                {
                    data.Generate(v);
                }
                else
                {
                    if (!object.ReferenceEquals(vessel, v))
                    {
                        if (object.ReferenceEquals(vessel, data.reference[v.id.ToString()]))
                        {
                            data.reference[v.id.ToString()] = null;
                        }
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
            if (vessel.situation != Vessel.Situations.LANDED || vessel.situation != Vessel.Situations.SPLASHED)
            {
                if (vessel.ActionGroups[KSPActionGroup.SAS] && vessel.IsControllable && !data.momentum_mode_active[vessel.id.ToString()] && data.rotation_mode_active[vessel.id.ToString()]) //vessel.Autopilot.Enabled does not work at this point!
                {
                    //Reset momentum_mode_active heading
                    vessel.Autopilot.SAS.lockedHeading = vessel.ReferenceTransform.rotation;

                    //Set relative rotation if there is a reference
                    if (data.reference[vessel.id.ToString()] != null)
                    {
                        vessel.SetRotation(Quaternion.FromToRotation(data.direction[vessel.id.ToString()], data.reference[vessel.id.ToString()].GetTransform().position - vessel.transform.position) * data.rotation[vessel.id.ToString()]);
                    }
                }
                else
                {
                    Vector3 av = data.momentum[vessel.id.ToString()];
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
        private void PackedSpin(Vessel vessel)
        {
            if(vessel.situation != Vessel.Situations.LANDED || vessel.situation != Vessel.Situations.SPLASHED)
                vessel.SetRotation(Quaternion.AngleAxis(data.momentum[vessel.id.ToString()].magnitude * TimeWarp.CurrentRate, vessel.ReferenceTransform.rotation * data.momentum[vessel.id.ToString()]) * vessel.transform.rotation);
        }
        private void PackedRotation(Vessel vessel)
        {
            if (vessel.situation != Vessel.Situations.LANDED || vessel.situation != Vessel.Situations.SPLASHED)
                vessel.SetRotation(Quaternion.FromToRotation(data.direction[vessel.id.ToString()], data.reference[vessel.id.ToString()].GetTransform().position - vessel.transform.position) * data.rotation[vessel.id.ToString()]);
        }
        private void AdjustSAS(Vessel vessel)
        {
            if (data.reference != null)
            {
                if (lastTransform.ContainsKey(vessel.id.ToString()) && lastPosition.ContainsKey(vessel.id.ToString()))
                {
                    Vector3d newPosition = (Vector3d)lastTransform[vessel.id.ToString()].position - data.reference[vessel.id.ToString()].GetTransform().position;
                    QuaternionD delta = FromToRotation(lastPosition[vessel.id.ToString()], newPosition);
                    QuaternionD adjusted = delta * (QuaternionD)vessel.Autopilot.SAS.lockedHeading;
                    vessel.Autopilot.SAS.lockedHeading = adjusted;
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
