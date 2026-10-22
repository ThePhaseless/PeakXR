using System.Collections;

namespace PeakXR.Sandbox;

/// <summary>Steps owned by WP3 (locomotion, stamina, body). Already wired into <c>HandSteps</c> after the
/// affordance steps, so the package adds steps here and never edits the step list.</summary>
internal sealed partial class SelfTestRunner
{
    // Static only while empty (CA1822); drop it with the first step.
    static IEnumerator MovementSteps()
    {
        yield break;
    }
}
