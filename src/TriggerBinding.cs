namespace WalkNWash.VRCompanion
{
    internal static class TriggerBinding
    {
        // OpenVR's index trigger is Axis1 / button 33. Other one-dimensional
        // axes can also report type Trigger (notably xrizer's Axis2 squeeze).
        internal static float OpenVrValue(int axis1Type, float axis1, ulong pressed)
            => axis1Type == 3 ? axis1 : (pressed & (1UL << 33)) != 0 ? 1 : 0;
    }
}
