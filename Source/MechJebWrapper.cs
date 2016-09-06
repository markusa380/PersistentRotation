using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;

namespace PersistentRotation
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class MechJebWrapper : MonoBehaviour
    {
        private static Type mjCore_t;
        private static FieldInfo saTarget_t;
        private static Type mjVesselExtensions_t;
        private static DynamicMethod<Vessel> GetMasterMechJeb;
        private static DynamicMethod<object, string> GetComputerModule;
        static internal bool mjAvailable;

        #region ### MechJeb Enum Imports ###
        public enum SATarget
        {
            OFF = 0,
            KILLROT = 1,
            NODE = 2,
            SURFACE = 3,
            PROGRADE = 4,
            RETROGRADE = 5,
            NORMAL_PLUS = 6,
            NORMAL_MINUS = 7,
            RADIAL_PLUS = 8,
            RADIAL_MINUS = 9,
            RELATIVE_PLUS = 10,
            RELATIVE_MINUS = 11,
            TARGET_PLUS = 12,
            TARGET_MINUS = 13,
            PARALLEL_PLUS = 14,
            PARALLEL_MINUS = 15,
            ADVANCED = 16,
            AUTO = 17,
            SURFACE_PROGRADE = 18,
            SURFACE_RETROGRADE = 19,
            HORIZONTAL_PLUS = 20,
            HORIZONTAL_MINUS = 21,
            VERTICAL_PLUS = 22,
        }
        static private Dictionary<int, SATarget> saTargetMap = new Dictionary<int, SATarget>
        {
            { 0, SATarget.OFF },
            { 1, SATarget.KILLROT },
            { 2, SATarget.NODE },
            { 3, SATarget.SURFACE },
            { 4, SATarget.PROGRADE },
            { 5, SATarget.RETROGRADE },
            { 6, SATarget.NORMAL_PLUS },
            { 7, SATarget.NORMAL_MINUS },
            { 8, SATarget.RADIAL_PLUS },
            { 9, SATarget.RADIAL_MINUS },
            {10, SATarget.RELATIVE_PLUS },
            {11, SATarget.RELATIVE_MINUS },
            {12, SATarget.TARGET_PLUS },
            {13, SATarget.TARGET_MINUS },
            {14, SATarget.PARALLEL_PLUS },
            {15, SATarget.PARALLEL_MINUS },
            {16, SATarget.ADVANCED },
            {17, SATarget.AUTO },
            {18, SATarget.SURFACE_PROGRADE },
            {19, SATarget.SURFACE_RETROGRADE },
            {20, SATarget.HORIZONTAL_PLUS },
            {21, SATarget.HORIZONTAL_MINUS },
            {22, SATarget.VERTICAL_PLUS }
        };
        #endregion

        /* ### MONOBEHAVIOUR METHODS ### */

        void Awake()
        {
            mjAvailable = false;
            try
            {
                mjCore_t = GetExportedType("MechJeb2", "MuMech.MechJebCore");
                if (mjCore_t == null)
                {
                    return;
                }

                mjVesselExtensions_t = GetExportedType("MechJeb2", "MuMech.VesselExtensions");
                if (mjVesselExtensions_t == null)
                {
                    return;
                }

                Type mjModuleSmartass_t = GetExportedType("MechJeb2", "MuMech.MechJebModuleSmartASS");
                if (mjModuleSmartass_t == null)
                {
                    return;
                }

                saTarget_t = mjModuleSmartass_t.GetField("target", BindingFlags.Instance | BindingFlags.Public);
                if (saTarget_t == null)
                {
                    return;
                }

                MethodInfo GetMasterMechJeb_t = mjVesselExtensions_t.GetMethod("GetMasterMechJeb", BindingFlags.Static | BindingFlags.Public);
                if (GetMasterMechJeb_t == null)
                {
                    return;
                }
                GetMasterMechJeb = CreateFunc<Vessel>(GetMasterMechJeb_t);
                if (GetMasterMechJeb == null)
                {
                    return;
                }

                MethodInfo GetComputerModule_t = mjCore_t.GetMethod("GetComputerModule", new Type[] { typeof(string) });
                if (GetComputerModule_t == null)
                {
                    return;
                }
                GetComputerModule = CreateFunc<object, string>(GetComputerModule_t);
                if (GetComputerModule == null)
                {
                    return;
                }


                mjAvailable = true;
            }
            catch
            {
                Debug.LogWarning("[PR] MechJeb exception.");
            }
        }

        /* ### PUBLIC METHODS ### */

        public static bool SmartASS(Vessel vessel)
        {
            object masterMechJeb;
            object smartAss;
            SATarget saTarget;

            masterMechJeb = GetMasterMechJeb(vessel);

            if (masterMechJeb == null)
            {
                return false;
            }

            smartAss = GetComputerModule(masterMechJeb, "MechJebModuleSmartASS");
            if (smartAss == null)
            {
                return false;
            }
            if (mjAvailable)
            {
                object activeSATarget = saTarget_t.GetValue(smartAss);
                saTarget = saTargetMap[(int)activeSATarget];

                if (saTarget != SATarget.OFF)
                {
                    return true;
                }
                else
                {
                    return false; 
                }
            }
            else
            {
                return false;
            }
        }

        /* ### UTILITY ### */

        internal static Type GetExportedType(string assemblyName, string fullTypeName)
        {
            int assyCount = AssemblyLoader.loadedAssemblies.Count;
            for (int assyIndex = 0; assyIndex < assyCount; ++assyIndex)
            {
                AssemblyLoader.LoadedAssembly assy = AssemblyLoader.loadedAssemblies[assyIndex];
                if (assy.name == assemblyName)
                {
                    Type[] exportedTypes = assy.assembly.GetExportedTypes();
                    int typeCount = exportedTypes.Length;
                    for (int typeIndex = 0; typeIndex < typeCount; ++typeIndex)
                    {
                        if (exportedTypes[typeIndex].FullName == fullTypeName)
                        {
                            return exportedTypes[typeIndex];
                        }
                    }
                }
            }

            return null;
        }

        public delegate object DynamicMethod<T>(T param0);
        public delegate object DynamicMethod<T, U>(T param0, U parmam1);

        static internal DynamicMethod<T> CreateFunc<T>(MethodInfo methodInfo)
        {
            // Up front validation:
            ParameterInfo[] parms = methodInfo.GetParameters();
            if (methodInfo.IsStatic)
            {
                if (parms.Length != 1)
                {
                    throw new ArgumentException("CreateFunc<T> called with static method that takes " + parms.Length + " parameters");
                }

                if (typeof(T) != parms[0].ParameterType)
                {
                    // What to do?
                }
            }
            else
            {
                if (parms.Length != 0)
                {
                    throw new ArgumentException("CreateFunc<T> called with non-static method that takes " + parms.Length + " parameters");
                }
            }

            Type[] _argTypes = { typeof(T) };

            // Create dynamic method and obtain its IL generator to
            // inject code.
            DynamicMethod dynam =
                new DynamicMethod(
                "", // name - don't care
                typeof(object), // return type
                _argTypes, // argument types
                typeof(MechJebWrapper));
            ILGenerator il = dynam.GetILGenerator();

            il.Emit(OpCodes.Ldarg_0);

            // Perform actual call.
            // If method is not final a callvirt is required
            // otherwise a normal call will be emitted.
            if (methodInfo.IsFinal)
            {
                il.Emit(OpCodes.Call, methodInfo);
            }
            else
            {
                il.Emit(OpCodes.Callvirt, methodInfo);
            }

            if (methodInfo.ReturnType != typeof(void))
            {
                // If result is of value type it needs to be boxed
                if (methodInfo.ReturnType.IsValueType)
                {
                    il.Emit(OpCodes.Box, methodInfo.ReturnType);
                }
            }
            else
            {
                il.Emit(OpCodes.Ldnull);
            }

            // Emit return opcode.
            il.Emit(OpCodes.Ret);


            return (DynamicMethod<T>)dynam.CreateDelegate(typeof(DynamicMethod<T>));
        }
        static internal DynamicMethod<T, U> CreateFunc<T, U>(MethodInfo methodInfo)
        {
            // Up front validation:
            ParameterInfo[] parms = methodInfo.GetParameters();
            if (methodInfo.IsStatic)
            {
                if (parms.Length != 2)
                {
                    throw new ArgumentException("CreateFunc<T, U> called with static method that takes " + parms.Length + " parameters");
                }

                if (typeof(T) != parms[0].ParameterType)
                {
                    // What to do?
                }
                if (typeof(U) != parms[1].ParameterType)
                {
                    // What to do?
                }
            }
            else
            {
                if (parms.Length != 1)
                {
                    throw new ArgumentException("CreateFunc<T, U> called with non-static method that takes " + parms.Length + " parameters");
                }
                if (typeof(U) != parms[0].ParameterType)
                {
                    // What to do?
                }
            }

            Type[] _argTypes = { typeof(T), typeof(U) };

            // Create dynamic method and obtain its IL generator to
            // inject code.
            DynamicMethod dynam =
                new DynamicMethod(
                "", // name - don't care
                typeof(object), // return type
                _argTypes, // argument types
                typeof(MechJebWrapper));
            ILGenerator il = dynam.GetILGenerator();

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);

            // Perform actual call.
            // If method is not final a callvirt is required
            // otherwise a normal call will be emitted.
            if (methodInfo.IsFinal)
            {
                il.Emit(OpCodes.Call, methodInfo);
            }
            else
            {
                il.Emit(OpCodes.Callvirt, methodInfo);
            }

            if (methodInfo.ReturnType != typeof(void))
            {
                // If result is of value type it needs to be boxed
                if (methodInfo.ReturnType.IsValueType)
                {
                    il.Emit(OpCodes.Box, methodInfo.ReturnType);
                }
            }
            else
            {
                il.Emit(OpCodes.Ldnull);
            }

            // Emit return opcode.
            il.Emit(OpCodes.Ret);


            return (DynamicMethod<T, U>)dynam.CreateDelegate(typeof(DynamicMethod<T, U>));
        }
    }
}