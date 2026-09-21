namespace WalkNWash.VRCompanion
{
    internal enum ToolController { Left = 1, Right = 2 }

    // Controller IDs remain physical; game actions use semantic roles.
    internal readonly struct HandRoles
    {
        internal readonly bool ToolOnLeft;
        internal HandRoles(bool toolOnLeft) { ToolOnLeft = toolOnLeft; }
        internal int ToolHand => ToolOnLeft ? 1 : 2;
        internal int FreeHand => ToolOnLeft ? 2 : 1;
        internal string ToolName => ToolOnLeft ? "Left" : "Right";
        internal string FreeName => ToolOnLeft ? "Right" : "Left";
        internal float FreeTrigger(float left, float right) => ToolOnLeft ? right : left;
        internal float ToolTrigger(float left, float right) => ToolOnLeft ? left : right;
    }
}
