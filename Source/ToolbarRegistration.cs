using UnityEngine;
using ToolbarControl_NS;

namespace PersistentRotation
{
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class RegisterToolbar : MonoBehaviour
    {

        void Start()
        {
            ToolbarControl.RegisterMod(Interface.MODID, Interface.MODNAME);
        }
    }
}