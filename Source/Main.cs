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

            GameEvents.onVesselChange.Add(OnVesselChange);
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
            foreach (Vessel vessel in FlightGlobals.Vessels)
            {
                if (vessel.packed)
                {
                    if (vessel.loaded)
                    {
                        if (vessel.Autopilot.Enabled && vessel.IsControllable)
                        {
                            if (data.reference[vessel.id.ToString()] != null)
                            {
                                if (data.reference[vessel.id.ToString()] == lastReference[vessel.id.ToString()])
                                {
                                    PackedRotation(vessel);
                                }
                            }
                        }
                        else
                        {
                            PackedSpin(vessel);
                        }
                    }

                    lastActive[vessel.id.ToString()] = false;
                }
                else
                {
                    //Set momentum
                    if (vessel.Autopilot.Enabled && vessel.IsControllable)
                    {
                        data.momentum[vessel.id.ToString()] = Vector3.zero;
                    }
                    else
                    {
                        data.momentum[vessel.id.ToString()] = vessel.angularVelocity;
                    }

                    //Set direction
                    if (data.reference[vessel.id.ToString()] != null)
                    {
                        data.direction[vessel.id.ToString()] = data.reference[vessel.id.ToString()].GetTransform().position - vessel.transform.position;
                    }
                    else
                    {
                        data.direction[vessel.id.ToString()] = Vector3.zero;
                    }

                    //Set rotation
                    data.rotation[vessel.id.ToString()] = vessel.transform.rotation;

                    if (data.reference[vessel.id.ToString()] != null)
                    {
                        data.direction[vessel.id.ToString()] = data.reference[vessel.id.ToString()].GetTransform().position - vessel.transform.position;

                        if (vessel.Autopilot.Enabled && vessel.Autopilot.Mode == VesselAutopilot.AutopilotMode.StabilityAssist)
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
                }

                lastTransform[vessel.id.ToString()] = vessel.ReferenceTransform;
                lastReference[vessel.id.ToString()] = data.reference[vessel.id.ToString()];
            }
        }
        private void OnDestroy()
        {
            instance = null;
            //Unbind functions from GameEvents
            GameEvents.onVesselChange.Remove(OnVesselChange);
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

        private void OnVesselChange(Vessel vessel)
        {
            activeVessel = vessel;
        }
        private void OnVesselCreate(Vessel vessel)
        {
            StartCoroutine(LateGenerate(vessel));
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
                if (vessel.ActionGroups[KSPActionGroup.SAS] && vessel.IsControllable) //vessel.Autopilot.Enabled does not work at this point!
                {
                    //Reset locked heading
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
                            if (p.rigidbody == null) continue;
                            p.rigidbody.AddTorque(rotation * av, ForceMode.VelocityChange);
                            p.rigidbody.AddForce(Vector3.Cross(rotation * av, (p.rigidbody.position - COM)), ForceMode.VelocityChange);
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

        private IEnumerator LateGenerate(Vessel vessel)
        {
            yield return new WaitForEndOfFrame();
            data.Generate(vessel);

            lastPosition[vessel.id.ToString()] = Vector3.zero;
            lastActive[vessel.id.ToString()] = false;
            lastReference[vessel.id.ToString()] = null;
            lastTransform[vessel.id.ToString()] = vessel.ReferenceTransform;
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
