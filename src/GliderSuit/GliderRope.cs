using System;

/// <summary>
/// Whether Jason's Grappling Hook mod has a rope attached. Both mods drive the
/// player's velocity from vp_FPController.FixedMove and cannot share it, so a rope
/// folds the wings and keeps them folded (plan section 11). Neither mod references
/// the other: the Grappling Hook publishes a named value on the AppDomain
/// (Grapple.AttachedSignal there) and this reads it. Without that mod the value is
/// never set and this is always false.
/// </summary>
internal static class GliderRope
{
    /// <summary>Must match Grapple.AttachedSignal in the Grappling Hook source.</summary>
    private const string AttachedSignal = "GrapplingHook.Attached";

    internal static bool Attached
    {
        get { return AppDomain.CurrentDomain.GetData(AttachedSignal) is bool attached && attached; }
    }
}
