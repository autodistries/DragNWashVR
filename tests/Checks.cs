using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using Mono.Cecil;
using WalkNWash.VRCompanion;
using static WalkNWash.VRCompanion.OpenXrNative;

internal static class Checks
{
    private static int count;
    private static void Assert(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL: " + name);
        count++;
    }
    private static void Near(float actual, float expected, string name) => Assert(Math.Abs(actual - expected) < .0001, name);
    private static void Size<T>(int bytes) => Assert(Marshal.SizeOf<T>() == bytes, typeof(T).Name + " ABI size");
    private static void Offset<T>(string field, int bytes) => Assert(Marshal.OffsetOf<T>(field).ToInt32() == bytes, typeof(T).Name + "." + field + " ABI offset");
    private static TypeDefinition Type(ModuleDefinition module, string name) => module.Types.Single(t => t.Name == name);
    private static void Method(TypeDefinition type, string name, int args, string returnType)
        => Assert(type.Methods.Any(m => m.Name == name && m.Parameters.Count == args && m.ReturnType.FullName == returnType), type.Name + "." + name);
    private static void Field(TypeDefinition type, string name, string expected = null)
        => Assert(type.Fields.Any(f => f.Name == name && (expected == null || f.FieldType.FullName == expected)), type.Name + "." + name);

    private static void Hooks(ModuleDefinition module)
    {
        var manager = Type(module, "VrVisualizationManager");
        Method(manager, "Update", 0, "System.Void");
        Field(manager, "_isUserSafeModeActive", "System.Boolean");
        Field(manager, "_autoSafeModeEndTime", "System.Single");
        var setup = module.Types.Single(t => t.Name.StartsWith("VrCameraSetup_Core"));
        Method(setup, "InitializeVr", 1, "System.Boolean");
        Method(setup, "UpdatePoses", 0, "System.Void");
        Method(setup, "TeardownVr", 0, "System.Void");
        Assert(setup.Methods.Any(m => m.Name == "RenderEye"), setup.Name + ".RenderEye");
        Field(setup, "_vrRig", "UnityEngine.GameObject");
        if (setup.Name.EndsWith("OpenXR"))
        {
            Field(setup, "_xrInstance", "System.UInt64");
            Field(setup, "_xrSession", "System.UInt64");
            Field(setup, "_locatedViews");
            Field(setup, "_locatedViewState");
            Field(setup, "_currentSessionState");
            Field(Type(module, "XrViewState"), "viewStateFlags");
            Field(Type(module, "XrView"), "pose");
            Field(Type(module, "XrPosef"), "position");
            Field(Type(module, "XrPosef"), "orientation");
        }
        else
        {
            Field(setup, "_hmd");
            Field(setup, "_trackedPoses");
            var system = Type(module, "CVRSystem");
            Method(system, "GetControllerState", 3, "System.Boolean");
            Method(system, "GetTrackedDeviceIndexForControllerRole", 1, "System.UInt32");
            Method(system, "GetInt32TrackedDeviceProperty", 3, "System.Int32");
            Method(system, "IsInputAvailable", 0, "System.Boolean");
            for (int i = 0; i < 5; i++) Field(Type(module, "VRControllerState_t"), "rAxis" + i);
            var axisEnum = Type(module, "EVRControllerAxisType");
            Assert(Convert.ToInt32(axisEnum.Fields.Single(f => f.Name == "k_eControllerAxis_Joystick").Constant) == 2, "OpenVR joystick enum");
            var props = Type(module, "ETrackedDeviceProperty");
            Assert(Convert.ToInt32(props.Fields.Single(f => f.Name == "Prop_Axis0Type_Int32").Constant) == 3002, "OpenVR axis property enum");
        }
        Console.WriteLine("Hook signatures verified: " + setup.Name);
    }

    private static int Main(string[] args)
    {
        ControlMath.Deadzone(.1f, -.1f, .2f, out var x, out var y);
        Near(x, 0, "resting stick x"); Near(y, 0, "resting stick y");
        ControlMath.Deadzone(.6f, 0, .2f, out x, out y);
        Near(x, .5f, "analog movement remains proportional");
        ControlMath.Deadzone(1, 1, .2f, out x, out y);
        Near(x * x + y * y, 1, "diagonal movement capped");
        ControlMath.Deadzone(float.NaN, 1, .2f, out x, out y);
        Near(x + y, 0, "invalid stick releases movement");
        bool latched = false;
        Near(ControlMath.Snap(.8f, 30, ref latched), 30, "snap right");
        Near(ControlMath.Snap(1, 30, ref latched), 0, "held stick does not repeatedly snap");
        Near(ControlMath.Snap(-1, 30, ref latched), 0, "opposite direction requires neutral");
        ControlMath.Snap(0, 30, ref latched);
        Near(ControlMath.Snap(-.8f, 30, ref latched), -30, "snap left after release");
        Near(ControlMath.SmoothTurn(.1f, .2f, 90, 1f / 90), 0, "turn deadzone");
        Near(ControlMath.SmoothTurn(1, .2f, 90, 1f / 90) * 90, 90, "90 Hz turn speed");
        Near(ControlMath.SmoothTurn(1, .2f, 90, 1f / 45) * 45, 90, "45 Hz turn speed");
        Near(ControlMath.SmoothTurn(-.6f, .2f, 90, 1f / 90), -.5f, "proportional left turn");
        Near(ControlMath.SmoothTurn(1, .2f, 90, 5), 9, "long frame cannot cause giant turn");

        ControlMath.Origin(10, 2, 20, .4f, 1.7f, -.2f, 0, 1, out x, out y, out var z);
        Near(x + .4f, 10, "calibration x aligns to player");
        Near(y + 1.7f, 2, "headset height not counted twice");
        Near(z - .2f, 20, "calibration z aligns to player");
        ControlMath.Origin(12, 2, 20, .4f, 1.7f, -.2f, 0, 1, out x, out y, out z);
        Near(x + .4f, 12, "origin follows player translation");
        Near(x + .5f, 12.1f, "physical lean remains additive");
        ControlMath.Origin(10, 2, 20, .4f, 1.7f, -.2f, 90, 2, out x, out y, out z);
        Near(x + 2 * -.2f, 10, "snap rotation preserves calibrated eye x");
        Near(y + 2 * 1.7f, 2, "world scale applied to tracking baseline");
        Near(z - 2 * .4f, 20, "snap rotation preserves calibrated eye z");

        Assert(IntPtr.Size == 8, "x64 test process");
        Size<ActionSetInfo>(216); Offset<ActionSetInfo>("priority", 208);
        Size<ActionInfo>(224); Offset<ActionInfo>("subactionPaths", 88); Offset<ActionInfo>("localizedName", 96);
        Size<Binding>(16); Size<SuggestedBindings>(40); Offset<SuggestedBindings>("bindings", 32);
        Size<SetList>(32); Offset<SetList>("sets", 24);
        Size<ActiveSet>(16); Size<GetInfo>(32);
        Size<VectorState>(48); Offset<VectorState>("lastChangeTime", 32); Offset<VectorState>("isActive", 40);

        string game = Path.GetFullPath(args.Length == 0 ? ".." : args[0]);
        using (var module = ModuleDefinition.ReadModule(Path.Combine(game, "DragNWash_Data/Managed/Assembly-CSharp.dll")))
        {
            Method(Type(module, "PlayerController"), "Update", 0, "System.Void");
            var look = Type(module, "LookController");
            Method(look, "LateUpdate", 0, "System.Void");
            Method(look, "ViewUpdate", 0, "System.Void");
            Method(look, "GetLookPosition", 0, "UnityEngine.Vector3");
            Method(look, "SetLookRotation", 1, "System.Void");
            Field(look, "smoothedLook", "UnityEngine.Vector2");
            Field(look, "smoothingVelocity", "UnityEngine.Vector2");
        }
        string plugins = Path.Combine(game, "BepInEx/plugins");
        foreach (string path in Directory.GetFiles(plugins, "UnityVRMod.dll", SearchOption.AllDirectories))
            using (var module = ModuleDefinition.ReadModule(path)) Hooks(module);
        foreach (string name in new[] { "UnityVRMod.zip", "UnityVRMod-openvr.zip" })
        {
            string path = Path.Combine(plugins, name);
            if (!File.Exists(path)) continue;
            using var zip = ZipFile.OpenRead(path);
            var entry = zip.Entries.Single(e => e.Name == "UnityVRMod.dll");
            using var memory = new MemoryStream();
            using (var stream = entry.Open()) stream.CopyTo(memory);
            memory.Position = 0;
            using var module = ModuleDefinition.ReadModule(memory);
            Hooks(module);
        }
        Console.WriteLine(count + " checks passed (math, native ABI, installed game/mod signatures).");
        return 0;
    }
}
