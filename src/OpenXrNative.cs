using System;
using System.Runtime.InteropServices;

namespace WalkNWash.VRCompanion
{
    // OpenXR 1.0 ABI. XrBool32 is uint; handles/XrPath are 64-bit in this x64 game.
    internal static class OpenXrNative
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        internal struct ActionSetInfo
        {
            internal int type;
            internal IntPtr next;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] internal string name;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string localizedName;
            internal uint priority;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        internal struct ActionInfo
        {
            internal int type;
            internal IntPtr next;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] internal string name;
            internal int actionType;
            internal uint countSubactionPaths;
            internal IntPtr subactionPaths;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string localizedName;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Binding { internal ulong action, path; }
        [StructLayout(LayoutKind.Sequential)]
        internal struct SuggestedBindings
        {
            internal int type;
            internal IntPtr next;
            internal ulong profile;
            internal uint count;
            internal IntPtr bindings;
        }
        [StructLayout(LayoutKind.Sequential)]
        internal struct SetList
        {
            internal int type;
            internal IntPtr next;
            internal uint count;
            internal IntPtr sets;
        }
        [StructLayout(LayoutKind.Sequential)]
        internal struct ActiveSet { internal ulong set, subactionPath; }
        [StructLayout(LayoutKind.Sequential)]
        internal struct GetInfo
        {
            internal int type;
            internal IntPtr next;
            internal ulong action, subactionPath;
        }
        [StructLayout(LayoutKind.Sequential)]
        internal struct VectorState
        {
            internal int type;
            internal IntPtr next;
            internal float x, y;
            internal uint changedSinceLastSync;
            internal long lastChangeTime;
            internal uint isActive;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct FloatState
        {
            internal int type;
            internal IntPtr next;
            internal float value;
            internal uint changedSinceLastSync;
            internal long lastChangeTime;
            internal uint isActive;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct BooleanState
        {
            internal int type;
            internal IntPtr next;
            internal uint value, changedSinceLastSync;
            internal long lastChangeTime;
            internal uint isActive;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Pose
        {
            internal float qx, qy, qz, qw, x, y, z;
        }
        [StructLayout(LayoutKind.Sequential)]
        internal struct ActionSpaceInfo
        {
            internal int type;
            internal IntPtr next;
            internal ulong action, subactionPath;
            internal Pose pose;
        }
        [StructLayout(LayoutKind.Sequential)]
        internal struct SpaceLocation
        {
            internal int type;
            internal IntPtr next;
            internal ulong flags;
            internal Pose pose;
        }
        [StructLayout(LayoutKind.Sequential)]
        internal struct PoseState
        {
            internal int type;
            internal IntPtr next;
            internal uint isActive;
        }

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        internal delegate int CreateSpace(ulong session, ref ActionSpaceInfo info, out ulong space);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        internal delegate int LocateSpace(ulong space, ulong baseSpace, long time, ref SpaceLocation location);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        internal delegate int ReadPose(ulong session, ref GetInfo info, ref PoseState state);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        internal delegate int GetProc(ulong instance, [MarshalAs(UnmanagedType.LPStr)] string name, out IntPtr pointer);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        internal delegate int StringToPath(ulong instance, [MarshalAs(UnmanagedType.LPStr)] string path, out ulong result);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        internal delegate int CreateSet(ulong instance, ref ActionSetInfo info, out ulong set);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        internal delegate int CreateAction(ulong set, ref ActionInfo info, out ulong action);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        internal delegate int Suggest(ulong instance, ref SuggestedBindings bindings);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        internal delegate int SetOperation(ulong session, ref SetList info);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        internal delegate int ReadVector(ulong session, ref GetInfo info, ref VectorState state);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        internal delegate int ReadFloat(ulong session, ref GetInfo info, ref FloatState state);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        internal delegate int ReadBoolean(ulong session, ref GetInfo info, ref BooleanState state);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        internal delegate int DestroySet(ulong set);

        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern IntPtr GetModuleHandleW(string name);
        [DllImport("kernel32", CharSet = CharSet.Ansi, ExactSpelling = true)]
        internal static extern IntPtr GetProcAddress(IntPtr module, string name);

        internal sealed class NativeArray<T> : IDisposable where T : struct
        {
            internal IntPtr Pointer { get; private set; }
            internal NativeArray(params T[] values)
            {
                int size = Marshal.SizeOf(typeof(T));
                Pointer = Marshal.AllocHGlobal(size * values.Length);
                for (int i = 0; i < values.Length; i++) Marshal.StructureToPtr(values[i], Pointer + i * size, false);
            }
            public void Dispose()
            {
                if (Pointer != IntPtr.Zero) Marshal.FreeHGlobal(Pointer);
                Pointer = IntPtr.Zero;
            }
        }
    }
}
