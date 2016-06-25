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
            if (!Directory.Exists(KSPUtil.ApplicationRootPath + "/PluginData/PersistentRotation"))
                Directory.CreateDirectory(KSPUtil.ApplicationRootPath + "/PluginData/PersistentRotation");

            return KSPUtil.ApplicationRootPath + "/PluginData/PersistentRotation/PersistentRotation_" + HighLogic.CurrentGame.Title.TrimEnd("_()SANDBOXCAREERSCIENCE".ToCharArray()).TrimEnd(' ') + "_" + counter.ToString() +".cfg";
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

        #region ### Initialize Dictionaries ###
        public Dictionary<string, Vector3> momentum = new Dictionary<string, Vector3>();
        public Dictionary<string, bool> rotation_mode_active = new Dictionary<string, bool>(); //Rotation mode active
        public Dictionary<string, bool> use_default_reference = new Dictionary<string, bool>(); //Default Rotation mode active
        public Dictionary<string, Quaternion> rotation = new Dictionary<string, Quaternion>(); //Rotation of vessel
        public Dictionary<string, Vector3> direction = new Dictionary<string, Vector3>(); //Direction to reference, only necessary for Packed Rotation
        public Dictionary<string, ITargetable> reference = new Dictionary<string, ITargetable>(); //Reference Body for packed and unpacked Rotation
        public Dictionary<string, bool> momentum_mode_active = new Dictionary<string, bool>(); //Momentum mode active
        public Dictionary<string, float> desired_rpm = new Dictionary<string, float>(); //Desired RPM
        #endregion

        private void Awake()
        {
            instance = this;
        }
        private void Start()
        {
            Load();

            if (FlightGlobals.ActiveVessel.situation == Vessel.Situations.PRELAUNCH)
            {
                momentum[FlightGlobals.ActiveVessel.id.ToString()] = Vector3.zero;
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
                ConfigNode.CreateConfigFromObject(this, 0, save);

                #region ### Save momentum of all vessels ###
                ConfigNode cn_momentum = save.AddNode("MOMENTUM");
                foreach (KeyValuePair<string, Vector3> entry in momentum)
                {
                    cn_momentum.AddValue(entry.Key, KSPUtil.WriteVector(entry.Value));
                }
                #endregion

                #region ### Save rotation_mode_active status ###
                ConfigNode cn_rotation_mode_active = save.AddNode("ROTATION_MODE_ACTIVE");
                foreach (KeyValuePair<string, bool> entry in rotation_mode_active)
                {
                    cn_rotation_mode_active.AddValue(entry.Key, entry.Value.ToString());
                }
                #endregion

                #region ### Save use_default_reference value ###
                ConfigNode cn_use_default_reference = save.AddNode("USE_DEFAULT_REFERENCE");
                foreach (KeyValuePair<string, bool> entry in use_default_reference)
                {
                    cn_use_default_reference.AddValue(entry.Key, entry.Value.ToString());
                }
                #endregion

                #region ### Save rotation of all vessels ###
                ConfigNode cn_rotation = save.AddNode("ROTATION");
                foreach (KeyValuePair<string, Quaternion> entry in rotation)
                {
                    cn_rotation.AddValue(entry.Key, KSPUtil.WriteQuaternion(entry.Value));
                }
                #endregion

                #region ### Save direction to the mainBody for each vessel ###
                ConfigNode cn_direction = save.AddNode("DIRECTION");
                foreach (KeyValuePair<string, Vector3> entry in direction)
                {
                    cn_direction.AddValue(entry.Key, KSPUtil.WriteVector(entry.Value));
                }
                #endregion

                #region ### Save direction reference object for each vessel ###
                ConfigNode cn_reference = save.AddNode("REFERENCE");
                foreach (KeyValuePair<string, ITargetable> entry in reference)
                {
                    if (entry.Value != null)
                    {
                        if (entry.Value.GetType() == typeof(CelestialBody))
                        {
                            cn_reference.AddValue(entry.Key, entry.Value.GetName());
                        }
                        else if (entry.Value.GetType() == typeof(Vessel))
                        {
                            cn_reference.AddValue(entry.Key, entry.Value.GetVessel().id.ToString());
                        }
                        else
                        {
                            Debug.LogError("[PR] Wrong Reference Type!");
                            cn_reference.AddValue(entry.Key, "NONE");
                        }
                    }
                    else
                    {
                        cn_reference.AddValue(entry.Key, "NONE");
                    }
                }
                #endregion

                #region ### Save momentum_mode_active status ###
                ConfigNode cn_momentum_mode_active = save.AddNode("MOMENTUM_MODE_ACTIVE");
                foreach (KeyValuePair<string, bool> entry in momentum_mode_active)
                {
                    cn_momentum_mode_active.AddValue(entry.Key, entry.Value.ToString());
                }
                #endregion

                #region ### Save desired_rpm ###
                ConfigNode cn_desired_rpm = save.AddNode("DESIRED_RPM");
                foreach (KeyValuePair<string, float> entry in desired_rpm)
                {
                    cn_desired_rpm.AddValue(entry.Key, entry.Value.ToString());
                }
                #endregion

                save.Save(GetUnusedPath());
            }
            catch (Exception e) { Debug.Log("[PR] Saving not sucessfull: " + e.Message); }
        }
        public void Load()
        {
            //This is called when all persistent rotation data is being loaded from the cfg file.

            momentum.Clear();
            rotation.Clear();
            direction.Clear();
            reference.Clear();
            momentum_mode_active.Clear();

            foreach (Vessel vessel in FlightGlobals.Vessels)
            {
                Generate(vessel);
            }

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

            #region ### Load momentum ###
            ConfigNode cn_momentum = load.GetNode("MOMENTUM");
            foreach (ConfigNode.Value s in cn_momentum.values)
            {
                momentum[s.name] = KSPUtil.ParseVector3(s.value);
            }
            #endregion

            #region ### Load rotation_mode_active ###
            ConfigNode cn_rotation_mode_active = load.GetNode("ROTATION_MODE_ACTIVE");
            foreach (ConfigNode.Value s in cn_rotation_mode_active.values)
            {
                rotation_mode_active[s.name] = Boolean.Parse(s.value);
            }
            #endregion

            #region ### Load use_default_reference ###
            ConfigNode cn_use_default_reference = load.GetNode("USE_DEFAULT_REFERENCE");
            foreach (ConfigNode.Value s in cn_use_default_reference.values)
            {
                use_default_reference[s.name] = Boolean.Parse(s.value);
            }
            #endregion

            #region ### Load rotation ###
            ConfigNode cn_rotation = load.GetNode("ROTATION");
            foreach (ConfigNode.Value s in cn_rotation.values)
            {
                rotation[s.name] = KSPUtil.ParseQuaternion(s.value);
            }
            #endregion

            #region ### Load direction ###
            ConfigNode cn_direction = load.GetNode("DIRECTION");
            foreach (ConfigNode.Value s in cn_direction.values)
            {
                direction[s.name] = KSPUtil.ParseVector3(s.value);
            }
            #endregion

            #region ### Load reference ###
            ConfigNode cn_reference = load.GetNode("REFERENCE");
            foreach (ConfigNode.Value s in cn_reference.values)
            {
                if (s.value != "NONE")
                {
                    reference[s.name] = null;
                    foreach (CelestialBody body in FlightGlobals.Bodies) //Check all bodies
                    {
                        if (body.name == s.value)
                        {
                            reference[s.name] = body;
                        }
                    }

                    foreach (Vessel vessel in FlightGlobals.Vessels) // Check all vessels
                    {
                        if (vessel.id.ToString() == s.value)
                        {
                            reference[s.name] = vessel;
                        }
                    }
                }
                else
                {
                    reference[s.name] = null;
                }
            }
            #endregion

            #region ### Load momentum_mode_active ###
            ConfigNode cn_momentum_mode_active = load.GetNode("MOMENTUM_MODE_ACTIVE");
            foreach (ConfigNode.Value s in cn_momentum_mode_active.values)
            {
                momentum_mode_active[s.name] = Boolean.Parse(s.value);
            }
            #endregion

            #region ### Load desired_rpm ###
            ConfigNode cn_desired_rpm = load.GetNode("DESIRED_RPM");
            foreach (ConfigNode.Value s in cn_desired_rpm.values)
            {
                desired_rpm[s.name] = float.Parse(s.value);
            }
            #endregion

            Clean();

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

            Interface.instance.desired_rpm_str = desired_rpm[FlightGlobals.ActiveVessel.id.ToString()].ToString(); //Set desired rpm of active vessel
        }
        public void Clean()
        {
            Debug.Log("[PR] Cleaning Data.");

            #region ### Clean vessel momentum ###
            Dictionary<string, Vector3> temp_momentum = new Dictionary<string, Vector3>();
            foreach (Vessel vessel in FlightGlobals.Vessels)
            {
                if (vessel.vesselType == VesselType.SpaceObject || vessel.vesselType == VesselType.Unknown)
                {
                    float ran_x = (float)UnityEngine.Random.Range(0, 10) / (float)100;
                    float ran_y = (float)UnityEngine.Random.Range(0, 10) / (float)100;
                    float ran_z = (float)UnityEngine.Random.Range(0, 10) / (float)100;

                    temp_momentum[vessel.id.ToString()] = new Vector3(ran_x, ran_y, ran_z);
                }
                else
                {
                    temp_momentum[vessel.id.ToString()] = Vector3.zero;
                }
            }
            foreach (KeyValuePair<string, Vector3> entry in momentum)
            {
                temp_momentum[entry.Key] = entry.Value;
            }
            momentum.Clear();
            foreach (Vessel vessel in FlightGlobals.Vessels)
            {
                momentum[vessel.id.ToString()] = temp_momentum[vessel.id.ToString()];
            }
            temp_momentum.Clear();
            #endregion

            #region ### Clean vessel rotation_mode_active ###
            Dictionary<string, bool> temp_rotation_mode_active = new Dictionary<string, bool>();
            foreach (Vessel vessel in FlightGlobals.Vessels)
            {
                temp_rotation_mode_active[vessel.id.ToString()] = false;
            }
            foreach (KeyValuePair<string, bool> entry in rotation_mode_active)
            {
                temp_rotation_mode_active[entry.Key] = entry.Value;
            }
            rotation_mode_active.Clear();
            foreach (Vessel vessel in FlightGlobals.Vessels)
            {
                rotation_mode_active[vessel.id.ToString()] = temp_rotation_mode_active[vessel.id.ToString()];
            }
            temp_rotation_mode_active.Clear();
            #endregion

            #region ### Clean vessel use_default_reference ###
            Dictionary<string, bool> temp_use_default_reference = new Dictionary<string, bool>();
            foreach (Vessel vessel in FlightGlobals.Vessels)
            {
                temp_use_default_reference[vessel.id.ToString()] = false;
            }
            foreach (KeyValuePair<string, bool> entry in use_default_reference)
            {
                temp_use_default_reference[entry.Key] = entry.Value;
            }
            use_default_reference.Clear();
            foreach (Vessel vessel in FlightGlobals.Vessels)
            {
                use_default_reference[vessel.id.ToString()] = temp_use_default_reference[vessel.id.ToString()];
            }
            temp_use_default_reference.Clear();
            #endregion

            #region ### Clean vessel rotation ###
            Dictionary<string, Quaternion> temp_rotation = new Dictionary<string, Quaternion>();
            foreach (Vessel vessel in FlightGlobals.Vessels)
            {
                temp_rotation[vessel.id.ToString()] = vessel.transform.rotation;
            }
            foreach (KeyValuePair<string, Quaternion> entry in rotation)
            {
                temp_rotation[entry.Key] = entry.Value;
            }
            rotation.Clear();
            foreach (Vessel vessel in FlightGlobals.Vessels)
            {
                rotation[vessel.id.ToString()] = temp_rotation[vessel.id.ToString()];
            }
            temp_rotation.Clear();
            #endregion

            #region ### Clean vessel direction ###
            Dictionary<string, Vector3> temp_direction = new Dictionary<string, Vector3>();
            foreach (Vessel vessel in FlightGlobals.Vessels)
            {
                temp_direction[vessel.id.ToString()] = (vessel.mainBody.position - vessel.transform.position).normalized;
            }
            foreach (KeyValuePair<string, Vector3> entry in direction)
            {
                temp_direction[entry.Key] = entry.Value;
            }
            direction.Clear();
            foreach (Vessel vessel in FlightGlobals.Vessels)
            {
                direction[vessel.id.ToString()] = temp_direction[vessel.id.ToString()];
            }
            temp_direction.Clear();
            #endregion

            #region ### Clean vessel reference ###
            Dictionary<string, ITargetable> temp_reference = new Dictionary<string, ITargetable>();
            foreach (Vessel vessel in FlightGlobals.Vessels)
            {
                temp_reference[vessel.id.ToString()] = null;
            }
            foreach (KeyValuePair<string, ITargetable> entry in reference)
            {
                temp_reference[entry.Key] = entry.Value;
            }
            reference.Clear();
            foreach (Vessel vessel in FlightGlobals.Vessels)
            {
                reference[vessel.id.ToString()] = temp_reference[vessel.id.ToString()];
            }
            temp_reference.Clear();
            #endregion

            #region ### Clean vessel momentum_mode_active ###
            Dictionary<string, bool> temp_momentum_mode_active = new Dictionary<string, bool>();
            foreach (Vessel vessel in FlightGlobals.Vessels)
            {
                temp_momentum_mode_active[vessel.id.ToString()] = false;
            }
            foreach (KeyValuePair<string, bool> entry in momentum_mode_active)
            {
                temp_momentum_mode_active[entry.Key] = entry.Value;
            }
            momentum_mode_active.Clear();
            foreach (Vessel vessel in FlightGlobals.Vessels)
            {
                momentum_mode_active[vessel.id.ToString()] = temp_momentum_mode_active[vessel.id.ToString()];
            }
            temp_momentum_mode_active.Clear();
            #endregion

            #region ### Clean vessel desired_rpm ###
            Dictionary<string, float> temp_desired_rpm = new Dictionary<string, float>();
            foreach (Vessel vessel in FlightGlobals.Vessels)
            {
                temp_desired_rpm[vessel.id.ToString()] = 0f;
            }
            foreach (KeyValuePair<string, float> entry in desired_rpm)
            {
                temp_desired_rpm[entry.Key] = entry.Value;
            }
            desired_rpm.Clear();
            foreach (Vessel vessel in FlightGlobals.Vessels)
            {
                desired_rpm[vessel.id.ToString()] = temp_desired_rpm[vessel.id.ToString()];
            }
            temp_desired_rpm.Clear();
            #endregion
        }
        public void Generate(Vessel vessel)
        {
            if(rotation.ContainsKey(vessel.id.ToString()) && momentum.ContainsKey(vessel.id.ToString()) && reference.ContainsKey(vessel.id.ToString()) && direction.ContainsKey(vessel.id.ToString()) && momentum_mode_active.ContainsKey(vessel.id.ToString()))
            {
                Debug.Log("[PR] " + vessel.vesselName + " already has data.");
                return;
            }

            Debug.Log("[PR] Generating data for " + vessel.vesselName);

            rotation_mode_active[vessel.id.ToString()] = true;
            use_default_reference[vessel.id.ToString()] = true;
            rotation[vessel.id.ToString()] = vessel.transform.rotation;
            direction[vessel.id.ToString()] = (vessel.mainBody.position - vessel.transform.position).normalized;
            reference[vessel.id.ToString()] = vessel.mainBody;
            momentum_mode_active[vessel.id.ToString()] = false;
            desired_rpm[vessel.id.ToString()] = 0f;

            if (vessel.vesselType == VesselType.SpaceObject || vessel.vesselType == VesselType.Unknown)
            {
                float ran_x = (float)UnityEngine.Random.Range(0, 10) / (float)100;
                float ran_y = (float)UnityEngine.Random.Range(0, 10) / (float)100;
                float ran_z = (float)UnityEngine.Random.Range(0, 10) / (float)100;

                momentum[vessel.id.ToString()] = new Vector3(ran_x, ran_y, ran_z);
            }
            else
            {
                momentum[vessel.id.ToString()] = Vector3.zero;
            }
        }
    }
} 
