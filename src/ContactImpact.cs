namespace WalkNWash.VRCompanion
{
    // One impact per inward stroke. Shared by the physical hand and offline tests.
    internal struct ContactImpact
    {
        private bool latched, hasImpact;
        private float lastImpact;

        internal bool Update(bool contacting, float inwardSpeed, float now, float threshold, float rearmSpeed)
        {
            if (!contacting) { latched = false; return false; }
            if (inwardSpeed <= rearmSpeed && (!hasImpact || now - lastImpact > .08f)) latched = false;
            if (latched || inwardSpeed < threshold || (hasImpact && now - lastImpact < .12f)) return false;
            latched = hasImpact = true;
            lastImpact = now;
            return true;
        }
    }
}
