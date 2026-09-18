namespace WalkNWash.VRCompanion
{
    internal struct TriggerButton
    {
        private bool armed, down;

        internal bool Update(float value, bool enabled)
        {
            if (!enabled || float.IsNaN(value) || float.IsInfinity(value))
            {
                armed = down = false;
                return false;
            }
            if (value <= .35f) { armed = true; down = false; }
            else if (armed && value >= .65f) down = true;
            return down;
        }
    }
}
