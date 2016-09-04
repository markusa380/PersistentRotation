using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System;
using UnityEngine;
using System.IO;

namespace PersistentRotation
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    class Data : MonoBehaviour
    {
        public static Data instance { get; private set; }

        String GetPath(int counter)
        {
            if (!Directory.Exists(KSPUtil.ApplicationRootPath + "/GameData/PersistentRotation/PluginData"))
                Directory.CreateDirectory(KSPUtil.ApplicationRootPath + "/GameData/PersistentRotation/PluginData");

            return KSPUtil.ApplicationRootPath + "/GameData/PersistentRotation/PluginData/PersistentRotation_" + HighLogic.CurrentGame.Title.TrimEnd("_()SANDBOXCAREERSCIENCE".ToCharArray()).TrimEnd(' ') + "_" + counter.ToString() +".cfg";
        }
        String GetUnusedPath()
        {
            int i = 0;
            while(true)
            {
                if(!File.Exists(GetPath(i)))
                {
                    return (GetPath(i));
                }
                else
                {
                    i++;
                }
            }
        }
        List<String> GetAllPaths()
        {
            int i = 0;
            List<String> paths = new List<String>();
            while (true)
            {
                if (!File.Exists(GetPath(i)))
                {
                    return(paths);
                }
                else
                {
                    paths.Add(GetPath(i));
                    i++;
                }
            }
        }

        public class PRVessel
        {
            //General
            public Vessel vessel;
            public Vector3 momentum;

            //Rotation Mode
            public bool rotation_mode_active;
            public bool dynamic_reference;
            public Quaternion rotation;
            public Vector3 direction;
            public ITargetable reference;

            //Momentum Mode
            public bool momentum_mode_active;
            public float desired_rpm;

            //Legacy Data
            public Vector3 last_position;
            public Transform last_transform;
            public bool last_active;
            public ITargetable last_reference;

            //Activity check
            public bool processed;

            public PRVessel(Vessel _vessel, Vector3 _momentum, bool _rotation_mode_active, bool _dynamic_reference, Quaternion _rotation, Vector3 _direction, ITargetable _reference, bool _momentum_mode_active, float _desired_rpm)
            {
                vessel = _vessel;
                momentum = _momentum;
                rotation_mode_active = _rotation_mode_active;
                dynamic_reference = _dynamic_reference;
                rotation = _rotation;
                direction = _direction;
                reference = _reference;
                momentum_mode_active = _momentum_mode_active;
                desired_rpm = _desired_rpm;
                processed = false;
            }
        }
        public List<PRVessel> PRVessels;
        public PRVessel FindPRVessel(Vessel vessel)
        {
            foreach(PRVessel v in PRVessels)
            {
                if (v.vessel == vessel)
                    return v;
            }
            return SubGenerate(vessel);
        }

        public enum DefaultReferenceMode
        {
            NONE,
            DYNAMIC
        }

        public DefaultReferenceMode default_reference_mode = DefaultReferenceMode.NONE;

        private void Awake()
        {
            instance = this;
        }
        private void Start()
        {
            PRVessels = new List<PRVessel>();
            Load();

            if (FlightGlobals.ActiveVessel.situation == Vessel.Situations.PRELAUNCH)
            {
                FindPRVessel(FlightGlobals.ActiveVessel).momentum = Vector3.zero; //Disables Flipping Vessels on Launchpad after Revert
            }
        }
        private void OnDestroy()
        {
            instance = null;
        }

        public void Save()
        {
            Debug.Log("[PR] Saving Data.");
            try
            {
                ConfigNode save = new ConfigNode();
                save.AddValue("TIME", Planetarium.GetUniversalTime());
                save.AddValue("DEFAULT_REFERENCE_MODE", ((int)default_reference_mode).ToString());
                ConfigNode.CreateConfigFromObject(this, 0, save);

                //Save values per vessel
                foreach(PRVessel v in PRVessels)
                {
                    ConfigNode cn_vessel = save.AddNode(v.vessel.id.ToString());
                    cn_vessel.AddValue("MOMENTUM", KSPUtil.WriteVector(v.momentum));
                    cn_vessel.AddValue("ROTATION_MODE_ACTIVE", v.rotation_mode_active.ToString());
                    cn_vessel.AddValue("DYNAMIC_REFERENCE", v.dynamic_reference.ToString());
                    cn_vessel.AddValue("ROTATION", KSPUtil.WriteQuaternion(v.rotation));
                    cn_vessel.AddValue("DIRECTION", KSPUtil.WriteVector(v.direction));

                    //Get Reference Type and save accordingly
                    if(v.reference != null)
                    {
                        if(v.reference.GetType() == typeof(CelestialBody))
                            cn_vessel.AddValue("REFERENCE", v.reference.GetName());
                        else if(v.reference.GetType() == typeof(Vessel))
                            cn_vessel.AddValue("REFERENCE", v.reference.GetVessel().id.ToString());
                    }
                    else
                        cn_vessel.AddValue("REFERENCE", "NONE");

                    cn_vessel.AddValue("MOMENTUM_MODE_ACTIVE", v.momentum_mode_active.ToString());
                    cn_vessel.AddValue("DESIRED_RPM", v.desired_rpm.ToString());
                }

                save.Save(GetUnusedPath());
            }
            catch (Exception e) { Debug.Log("[PR] Saving not sucessfull: " + e.Message); }
        }
        public void Load()
        {
            //This is called when all persistent rotation data is being loaded from the cfg file.

            #region ### Quicksave selection ###
            ConfigNode temp = null;
            float temp_delta = 0f;
            ConfigNode load = null;
            float load_delta = 0f;
            float oldest_time = 0f;

            List<String> allPaths = GetAllPaths();

            if(allPaths.Count() == 0)
            {
                Debug.Log("[PR] No save files found.");
                return;
            }

            foreach (String path in allPaths)
            {
                temp = ConfigNode.Load(path);
                if (temp == null)
                {
                    Debug.Log("[PR] Couldn't load data: File not found.");
                    continue;
                }


                float time = float.Parse(temp.GetValue("TIME"));
                temp_delta = Mathf.Abs(time - (float)Planetarium.GetUniversalTime());

                if(time > oldest_time)
                {
                    oldest_time = time;
                }

                if(load == null)
                {
                    load = temp;
                    load_delta = temp_delta;
                }
                else
                {
                    if(temp_delta < load_delta)
                    {
                        load = temp;
                        load_delta = temp_delta;
                    }
                }
            }
            #endregion

            //Load global variables

            default_reference_mode = (DefaultReferenceMode)(int.Parse(load.GetValue("DEFAULT_REFERENCE_MODE")));

            //Pregenerate data for all vessels that currently exist

            foreach (Vessel vessel in FlightGlobals.Vessels)
            {
                FindPRVessel(vessel);
            }

            //All vessels should now have data.

            //Load PRVessel data

            foreach (PRVessel v in PRVessels)
            {
                ConfigNode cn_vessel = load.GetNode(v.vessel.id.ToString());

                if(cn_vessel != null) //If node exists at all
                {
                    Debug.Log("[PR] Found node for vessel " + v.vessel.vesselName);
                    v.momentum = KSPUtil.ParseVector3(cn_vessel.GetValue("MOMENTUM"));
                    v.rotation_mode_active = Boolean.Parse(cn_vessel.GetValue("ROTATION_MODE_ACTIVE"));
                    v.dynamic_reference = Boolean.Parse(cn_vessel.GetValue("DYNAMIC_REFERENCE"));
                    v.rotation = KSPUtil.ParseQuaternion(cn_vessel.GetValue("ROTATION"));
                    v.direction = KSPUtil.ParseVector3(cn_vessel.GetValue("DIRECTION"));

                    string reference = cn_vessel.GetValue("REFERENCE");

                    v.reference = null;

                    if (reference != "NONE")
                    {
                        foreach (CelestialBody body in FlightGlobals.Bodies)
                        {
                            if(body.name == reference)
                            {
                                v.reference = body;
                            }
                        }

                        foreach (Vessel vessel in FlightGlobals.Vessels)
                        {
                            if (vessel.id.ToString() == reference)
                            {
                                v.reference = vessel;
                            }
                        }
                    }

                    v.momentum_mode_active = Boolean.Parse(cn_vessel.GetValue("MOMENTUM_MODE_ACTIVE"));
                    v.desired_rpm = float.Parse(cn_vessel.GetValue("DESIRED_RPM"));
                }
            }

            //If old save state is loaded, delete all save files!
            if (float.Parse(load.GetValue("TIME")) < oldest_time)
            {
                Debug.Log("[PR] Reloading old save, flushing data.");

                foreach (String path in GetAllPaths())
                {
                    File.Delete(path);
                }

                //Save current loaded one.
                Save();
            }

            Debug.Log("[PR] Oldest time: " + oldest_time.ToString());
            Debug.Log("[PR] Loaded time: " + load.GetValue("TIME"));

            Interface.instance.desired_rpm_str = FindPRVessel(FlightGlobals.ActiveVessel).desired_rpm.ToString(); //Set desired rpm of active vessel
        }

        private PRVessel SubGenerate(Vessel vessel)
        {
            //v is null, I have to generate a new PRVessel

            Debug.Log("[PR] Generating data for " + vessel.vesselName);

            //Generate Momentum (Add slight spin to unidentified and identified asteroids)
            Vector3 momentum;
            if (vessel.vesselType == VesselType.SpaceObject || vessel.vesselType == VesselType.Unknown)
            {
                float ran_x = (float)UnityEngine.Random.Range(0, 10) / (float)100;
                float ran_y = (float)UnityEngine.Random.Range(0, 10) / (float)100;
                float ran_z = (float)UnityEngine.Random.Range(0, 10) / (float)100;

                momentum = new Vector3(ran_x, ran_y, ran_z);
            }
            else
            {
                momentum = Vector3.zero;
            }

            PRVessel v;

            if(default_reference_mode == DefaultReferenceMode.DYNAMIC)
                v = new PRVessel(vessel, momentum, true, true, vessel.transform.rotation, (vessel.mainBody.position - vessel.transform.position).normalized, vessel.mainBody, false, 0f);
            else
                v = new PRVessel(vessel, momentum, true, false, vessel.transform.rotation, (vessel.mainBody.position - vessel.transform.position).normalized, null, false, 0f);

            PRVessels.Add(v);
            return v;
        }
    }
}