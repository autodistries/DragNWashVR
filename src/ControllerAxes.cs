namespace WalkNWash.VRCompanion
{
    // OpenVR devices advertise the meaning of each axis; don't assume pad = axis 0.
    internal struct ControllerAxes
    {
        private int joystickPlusOne, trackpadPlusOne;
        internal void Observe(int index, int type)
        {
            if (type == 2 && joystickPlusOne == 0) joystickPlusOne = index + 1;
            if (type == 1 && trackpadPlusOne == 0) trackpadPlusOne = index + 1;
        }
        internal int Index => (joystickPlusOne != 0 ? joystickPlusOne : trackpadPlusOne) - 1;
        internal bool TrackpadOnly => joystickPlusOne == 0 && trackpadPlusOne != 0;
    }
}
