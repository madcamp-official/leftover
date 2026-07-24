using UnityEditor;
using UnityEngine;

/// <summary>
/// Measures the authored right-hand trajectory of every bundled sword clip.
/// This keeps horizontal/vertical combat mapping based on the actual animation
/// data instead of clip names or visual guesswork.
/// </summary>
public static class BossDuelSwordClipAudit
{
    private const string FighterPath =
        "Assets/EEJANAI_Team/FreeSwordAnimations/Prefabs/EEJANAIbotSword1.prefab";
    private const string AnimationFolder =
        "Assets/EEJANAI_Team/FreeSwordAnimations/Animations/";

    [MenuItem("Tools/Boss Duel/Audit Sword Clip Trajectories")]
    public static void Audit()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FighterPath);
        if (prefab == null)
        {
            Debug.LogError("[SwordClipAudit] Fighter prefab is missing.");
            return;
        }

        GameObject instance = Object.Instantiate(prefab);
        instance.hideFlags = HideFlags.HideAndDontSave;
        Animator animator = instance.GetComponentInChildren<Animator>(true);
        Transform hand = animator != null && animator.isHuman
            ? animator.GetBoneTransform(HumanBodyBones.RightHand)
            : null;
        if (hand == null)
        {
            Debug.LogError("[SwordClipAudit] Humanoid right hand was not found.");
            Object.DestroyImmediate(instance);
            return;
        }

        AnimationMode.StartAnimationMode();
        try
        {
            for (int clipNumber = 1; clipNumber <= 9; clipNumber++)
            {
                AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(
                    AnimationFolder + $"slash{clipNumber}.anim");
                if (clip == null)
                    continue;

                const int sampleCount = 36;
                Vector3 previous = Vector3.zero;
                Vector3 minimum = new(float.PositiveInfinity, float.PositiveInfinity,
                    float.PositiveInfinity);
                Vector3 maximum = new(float.NegativeInfinity, float.NegativeInfinity,
                    float.NegativeInfinity);
                Vector3 travel = Vector3.zero;

                for (int sample = 0; sample <= sampleCount; sample++)
                {
                    float normalized = sample / (float)sampleCount;
                    AnimationMode.BeginSampling();
                    AnimationMode.SampleAnimationClip(instance, clip, clip.length * normalized);
                    AnimationMode.EndSampling();

                    // Measure in the humanoid Animator's own coordinate frame;
                    // the prefab root has an additional presentation rotation.
                    Vector3 point = animator.transform.InverseTransformPoint(hand.position);
                    minimum = Vector3.Min(minimum, point);
                    maximum = Vector3.Max(maximum, point);
                    if (sample > 0)
                    {
                        Vector3 delta = point - previous;
                        travel += new Vector3(Mathf.Abs(delta.x), Mathf.Abs(delta.y),
                            Mathf.Abs(delta.z));
                    }
                    previous = point;
                }

                Vector3 range = maximum - minimum;
                // Forward travel describes reach, not whether the blade path is
                // a horizontal or vertical cut. Compare only the lateral and
                // vertical hand travel when classifying the authored clip.
                float lateralScore = travel.x;
                float verticalScore = travel.y;
                string dominant = lateralScore > verticalScore * 1.2f
                    ? "HORIZONTAL"
                    : verticalScore > lateralScore * 1.2f ? "VERTICAL" : "MIXED";
                Debug.Log(
                    $"[SwordClipAudit] slash{clipNumber} length={clip.length:F3} " +
                    $"range=({range.x:F3},{range.y:F3},{range.z:F3}) " +
                    $"travel=({travel.x:F3},{travel.y:F3},{travel.z:F3}) " +
                    $"lateralScore={lateralScore:F3} verticalScore={verticalScore:F3} " +
                    $"forwardTravel={travel.z:F3} " +
                    $"dominant={dominant}");
            }
        }
        finally
        {
            AnimationMode.StopAnimationMode();
            Object.DestroyImmediate(instance);
        }
    }
}
