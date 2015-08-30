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

        Dictionary<string, Vector3> lastPosition = new Dictionary<string, Vector3>();
        Dictionary<string, Transform> lastTransform = new Dictionary<string, Transform>();
        Dictionary<string, bool> lastActive = new Dictionary<string, bool>();
        Dictionary<string, ITargetable> lastReference = new Dictionary<string, ITargetable>();

        Vessel activeVessel;
        Data data;

        private void Awake()
        {
            data = new Data();

            GameEvents.onVesselChange.Add(OnVesselChange);
            GameEvents.onVesselGoOnRails.Add(OnVesselGoOnRails);
            GameEvents.onVesselGoOffRails.Add(OnVesselGoOffRails);
            GameEvents.onVesselCreate.Add(OnVesselCreate);
            GameEvents.onShowUI.Add(OnShowUI);
            GameEvents.onHideUI.Add(OnHideUI);
            GameEvents.onVesselWillDestroy.Add(OnVesselWillDestroy);

            //Initialize GUI Stuff
            mainGuid = Guid.NewGuid().GetHashCode();
            bodyGuid = Guid.NewGuid().GetHashCode();
            configGuid = Guid.NewGuid().GetHashCode();

            MainWindowRect = new Rect(Screen.width / 4, 0, 200, 10); //Overwritten by LoadGUI
            BodyWindowRect = new Rect((Screen.width / 2) - 75, Screen.height / 4, 150, 10);
            ConfigWindowRect = new Rect((Screen.width / 2) - 100, Screen.height / 4, 200, 10);

            LoadGUI();

            if (visibility_mode == 2)
            {
                CreateStockToolbar();
            }
            else if (visibility_mode == 3)
            {
                CreateBlizzyToolbar();
            }
        }
        private void Start()
        {
            activeVessel = FlightGlobals.ActiveVessel;
            data.Load();
            data.Clean();
        }
        private void FixedUpdate()
        {
            foreach (Vessel vessel in FlightGlobals.Vessels)
            {
                if(vessel.packed)
                {
                    if (vessel.loaded) //Performance improvement, maybe
                    {
                        if (vessel.Autopilot.Enabled)
                        {
                            if (data.reference[vessel.id.ToString()] != null)
                            {
                                PackedRotation(vessel);
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
                    if (data.reference[vessel.id.ToString()] != null)
                    {
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
                        lastPosition[vessel.id.ToString()] = Vector3.zero;
                        lastActive[vessel.id.ToString()] = false;
                    }
                }

                lastTransform[vessel.id.ToString()] = vessel.ReferenceTransform;
                lastReference[vessel.id.ToString()] = data.reference[vessel.id.ToString()];
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
            Debug.LogWarning("1");
            foreach (Vessel v in FlightGlobals.Vessels)
            {
                Debug.LogWarning("2");
                if (!object.ReferenceEquals(vessel, v))
                {
                    Debug.LogWarning("3");
                    if (object.ReferenceEquals(vessel, data.reference[v.id.ToString()]))
                    {
                        Debug.LogWarning("4");
                        data.reference[v.id.ToString()] = null;
                        Debug.LogWarning("5");
                    }
                }
            }
        }

        private void OnVesselGoOnRails(Vessel vessel)
        {
            //Set momentum
            if (vessel.Autopilot.Enabled)
            {
                data.momentum[vessel.id.ToString()] = Vector3.zero;
            }
            else
            {
                data.momentum[vessel.id.ToString()] = vessel.angularVelocity;
            }

            //Set rotation
            data.rotation[vessel.id.ToString()] = vessel.transform.rotation;

            //Set direction
            if (data.reference[vessel.id.ToString()] != null)
            {
                data.direction[vessel.id.ToString()] = data.reference[vessel.id.ToString()].GetTransform().position - vessel.transform.position;
            }
            else
            {
                data.direction[vessel.id.ToString()] = Vector3.zero;
            }
        }
        private void OnVesselGoOffRails(Vessel vessel)
        {
            if(vessel.Autopilot.Enabled)
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
        private void OnDestroy()
        {
            data.Save();
            SaveGUI();

            //Unbind functions from GameEvents
            GameEvents.onVesselChange.Remove(OnVesselChange);
            GameEvents.onVesselGoOnRails.Remove(OnVesselGoOnRails);
            GameEvents.onVesselGoOffRails.Remove(OnVesselGoOffRails);
            GameEvents.onVesselCreate.Remove(OnVesselCreate);
            GameEvents.onShowUI.Remove(OnShowUI);
            GameEvents.onHideUI.Remove(OnHideUI);
            GameEvents.onVesselWillDestroy.Add(OnVesselWillDestroy);

            DeleteBlizzyToolbar();
            DeleteStockToolbar();
        }

        void PackedSpin(Vessel vessel)
        {
            vessel.SetRotation(Quaternion.AngleAxis(data.momentum[vessel.id.ToString()].magnitude * TimeWarp.CurrentRate, vessel.ReferenceTransform.rotation * data.momentum[vessel.id.ToString()]) * vessel.transform.rotation);
        }
        void PackedRotation(Vessel vessel)
        {
            vessel.SetRotation(Quaternion.FromToRotation(data.direction[vessel.id.ToString()], data.reference[vessel.id.ToString()].GetTransform().position - vessel.transform.position) * data.rotation[vessel.id.ToString()]);
        }
        void AdjustSAS(Vessel vessel)
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

        public IEnumerator LateGenerate(Vessel vessel)
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

        // +++ GRAPHICAL USER INTERFACE +++

        private IButton button;
        private ApplicationLauncherButton stockButton;
        int mainGuid;
        int bodyGuid;
        int configGuid;
        String gui_path
        {
            get { return KSPUtil.ApplicationRootPath + "/GameData/PersistentRotation/GUIconfig.cfg"; }
        }
        Rect BodyWindowRect;
        Rect MainWindowRect;
        Rect ConfigWindowRect;
        bool hidden = false; //If F2 is pressed
        bool disabled = false; //If Toolbar is pressed, overwritten by LoadGUI
        bool showMainWindow = true; //Window visible, overwritten by LoadGUI
        bool showBodyWindow = false;
        bool showConfigWindow = false;
        bool MainWindowActive = false; // minimize-maximize, overwritten by LoadGUI
        int visibility_mode = 1; // 1: Always Visible, 2: Stock Toolbar, 3: Blizzys Toolbar, overwritten by LoadGUI
        Texture close = GameDatabase.Instance.GetTexture("PersistentRotation/Textures/close_w", false);
        Texture options = GameDatabase.Instance.GetTexture("PersistentRotation/Textures/options_w", false);

        void OnGUI()
        {
            if (!hidden && !disabled)
            {
                if (showMainWindow)
                {
                    MainWindowRect = GUILayout.Window(mainGuid, MainWindowRect, MainGUI, "Persistent Rotation");
                }
                if (showBodyWindow)
                {
                    BodyWindowRect = GUILayout.Window(bodyGuid, BodyWindowRect, BodyGUI, "Select Body");
                }
                if (showConfigWindow)
                {
                    ConfigWindowRect = GUILayout.Window(configGuid, ConfigWindowRect, ConfigGUI, "Configure");
                }
            }
        }
        void OnShowUI()
        {
            hidden = false;
        }
        void OnHideUI()
        {
            hidden = true;
        }
        private void MainGUI(int windowID)
        {
            Texture toggle;

            if (MainWindowActive)
            {
                toggle = GameDatabase.Instance.GetTexture("PersistentRotation/Textures/minimize_w", false);
            }
            else
            {
                toggle = GameDatabase.Instance.GetTexture("PersistentRotation/Textures/maximize_w", false);
            }

            GUILayout.BeginVertical();
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(options))
            {
                showConfigWindow = !showConfigWindow;
            }
            if (GUILayout.Button(toggle))
            {
                MainWindowActive = !MainWindowActive;
                MainWindowRect = new Rect(MainWindowRect.x, MainWindowRect.y, 200, 10);
            }
            GUILayout.EndHorizontal();
            if (MainWindowActive)
            {
                if (GUILayout.Button("Body Relative Rotation", GUILayout.ExpandWidth(true)))
                {
                    showBodyWindow = !showBodyWindow;
                }

                if (data.reference[activeVessel.id.ToString()] != null)
                {
                    GUILayout.Label("Current Reference: " + data.reference[activeVessel.id.ToString()].GetName());
                }
                else
                {
                    GUILayout.Label("Current Body: none");
                }

            }
            GUILayout.EndVertical();
            GUI.DragWindow();

        }
        private void BodyGUI(int windowID)
        {
            GUILayout.BeginVertical();
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(close))
            {
                showBodyWindow = false;
            }
            GUILayout.EndHorizontal();
            GUILayout.Label("Select a target in map view\nand press <Set> to use it as reference.");
            if (GUILayout.Button("Set", GUILayout.ExpandWidth(true)))
            {
                if (activeVessel.targetObject.GetType() == typeof(CelestialBody) || activeVessel.targetObject.GetType() == typeof(Vessel))
                    data.reference[activeVessel.id.ToString()] = activeVessel.targetObject;
            }
            if (GUILayout.Button("Unset", GUILayout.ExpandWidth(true)))
            {
                data.reference[activeVessel.id.ToString()] = null;

            }
            GUILayout.Space(10);
            if (GUILayout.Button("Sun", GUILayout.ExpandWidth(true)))
            {
                data.reference[activeVessel.id.ToString()] = Sun.Instance.sun;
            }
            if (GUILayout.Button(activeVessel.mainBody.name, GUILayout.ExpandWidth(true)))
            {
                data.reference[activeVessel.id.ToString()] = activeVessel.mainBody;
            }

            GUILayout.EndVertical();
            GUI.DragWindow();
        }
        private void ConfigGUI(int windowID)
        {
            GUILayout.BeginVertical();
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(close))
            {
                showConfigWindow = false;
            }
            GUILayout.EndHorizontal();
            GUILayout.Label("Configure Visibility");
            if (GUILayout.Button("Always Visible", GUILayout.ExpandWidth(true)))
            {
                visibility_mode = 1;
                DeleteBlizzyToolbar();
                DeleteStockToolbar();
            }
            if (GUILayout.Button("Stock Toolbar", GUILayout.ExpandWidth(true)))
            {
                visibility_mode = 2;
                CreateStockToolbar();
                DeleteBlizzyToolbar();
            }
            if (ToolbarManager.ToolbarAvailable)
            {
                if (GUILayout.Button("Blizzy's Toolbar", GUILayout.ExpandWidth(true)))
                {
                    visibility_mode = 3;
                    CreateBlizzyToolbar();
                    DeleteStockToolbar();
                }
            }
            GUILayout.EndVertical();
            GUI.DragWindow();
        }
        void CreateBlizzyToolbar()
        {
            if (ToolbarManager.ToolbarAvailable)
            {
                // regular button
                button = ToolbarManager.Instance.add("PersistentRotation", "button");
                button.TexturePath = "PersistentRotation/Textures/texture";
                button.ToolTip = "Toggle PersistentRotation";
                button.OnClick += (e) =>
                {
                    disabled = !disabled;
                };
            }
        }
        void DeleteBlizzyToolbar()
        {
            if (ToolbarManager.ToolbarAvailable)
            {
                if (button != null)
                {
                    button.Destroy();
                }
            }
        }
        void CreateStockToolbar()
        {
            if (!stockButton)
            {
                stockButton = ApplicationLauncher.Instance.AddModApplication(
                () =>
                {
                    disabled = !disabled;
                },
                () =>
                {
                    disabled = !disabled;
                },
                null,
                null,
                null,
                null,
                ApplicationLauncher.AppScenes.FLIGHT,
                GameDatabase.Instance.GetTexture("PersistentRotation/Textures/texture", false)
                );
            }
        }
        void DeleteStockToolbar()
        {
            if (stockButton)
            {
                ApplicationLauncher.Instance.RemoveModApplication(stockButton);
                stockButton = null;
            }
        }
        void SaveGUI()
        {
            try
            {
                ConfigNode save = new ConfigNode();
                ConfigNode.CreateConfigFromObject(this, 0, save);

                save.AddValue("disabled", disabled.ToString());
                save.AddValue("mode", visibility_mode.ToString());
                save.AddValue("active", MainWindowActive.ToString());

                save.AddValue("showMain", showMainWindow.ToString());
                save.AddValue("xMain", MainWindowRect.x.ToString());
                save.AddValue("yMain", MainWindowRect.y.ToString());

                save.AddValue("showBody", showBodyWindow.ToString());
                save.AddValue("xBody", BodyWindowRect.x.ToString());
                save.AddValue("yBody", BodyWindowRect.y.ToString());

                save.AddValue("showConfig", showConfigWindow.ToString());
                save.AddValue("xConfig", ConfigWindowRect.x.ToString());
                save.AddValue("yConfig", ConfigWindowRect.y.ToString());

                save.Save(gui_path);
            }
            catch (Exception e) { Debug.Log("[PR] Saving not successful: " + e.Message); }
        }
        void LoadGUI()
        {
            ConfigNode load = ConfigNode.Load(gui_path);
            if (load == null)
            {
                Debug.Log("[PR] Cfg file is empty or not existent!");
                return;
            }

            foreach (ConfigNode.Value s in load.values)
            {
                if (s.name == "disabled")
                    disabled = Convert.ToBoolean(s.value);
                if (s.name == "mode")
                    visibility_mode = Convert.ToInt32(s.value);
                if (s.name == "active")
                    MainWindowActive = Convert.ToBoolean(s.value);

                if (s.name == "showMain")
                    showMainWindow = Convert.ToBoolean(s.value);
                if (s.name == "xMain")
                    MainWindowRect.x = Convert.ToSingle(s.value);
                if (s.name == "yMain")
                    MainWindowRect.y = Convert.ToSingle(s.value);

                if (s.name == "showBody")
                    showBodyWindow = Convert.ToBoolean(s.value);
                if (s.name == "xBody")
                    BodyWindowRect.x = Convert.ToSingle(s.value);
                if (s.name == "yBody")
                    BodyWindowRect.y = Convert.ToSingle(s.value);

                if (s.name == "showConfig")
                    showConfigWindow = Convert.ToBoolean(s.value);
                if (s.name == "xConfig")
                    ConfigWindowRect.x = Convert.ToSingle(s.value);
                if (s.name == "yConfig")
                    ConfigWindowRect.y = Convert.ToSingle(s.value);
            }
        }
    }
}
