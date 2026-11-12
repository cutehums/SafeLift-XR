using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Captures phase poses from the live GIST_AVATA / MediaPipe humanoid avatar
/// when it exists in the scene. The saved snapshots are later replayed by the
/// post-rep review UI. Sensor acquisition is intentionally outside this class.
/// </summary>
public sealed class SafeLiftPhasePoseRecorder : MonoBehaviour
{
    public static SafeLiftPhasePoseRecorder Instance { get; private set; }

    private static readonly string[] PhaseOrder = { "Standing", "Descent", "Bottom", "Ascent" };
    private readonly Dictionary<string, SafeLiftAvatarPoseSnapshot> snapshots = new Dictionary<string, SafeLiftAvatarPoseSnapshot>();

    private Animator sourceAnimator;
    private GameObject sourceAvatarRoot;
    private GameObject reviewClone;
    private string lastCaptureKey = "";
    private float lastCaptureAt;

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void SetSourceAnimator(Animator animator)
    {
        SetSourceAnimator(animator, null);
    }

    public void SetSourceAnimator(Animator animator, GameObject avatarRoot)
    {
        if (animator == null || !animator.isHuman) return;
        if (animator.GetComponentInParent<SafeLiftAvatarReviewController>() != null) return;
        sourceAnimator = animator;
        sourceAvatarRoot = ResolveUsableSourceRoot(animator, avatarRoot);
    }

    public Animator GetSourceAnimator()
    {
        return ResolveSourceAnimator();
    }

    public void ResetSnapshots()
    {
        snapshots.Clear();
        lastCaptureKey = "";
        lastCaptureAt = 0f;
    }

    public void CaptureFromState(SafeLiftState state)
    {
        if (state == null) return;
        Capture(NormalizePhase(state.phase), QualityScore(state.live_tracking_quality));
    }

    public void CaptureDemoPhase(string phase, int repIndex)
    {
        var normalized = NormalizePhase(phase);
        if (string.IsNullOrEmpty(normalized)) normalized = PhaseOrder[Mathf.Abs(repIndex) % PhaseOrder.Length];
        Capture(normalized, 0.5f);
    }

    public bool TryApplyPhaseToReview(Transform reviewParent, string phase, ref GameObject avatarRoot)
    {
        var normalized = NormalizePhase(phase);
        if (string.IsNullOrEmpty(normalized)) normalized = "Standing";

        if (TryEnsureReviewClone(reviewParent, ref avatarRoot))
        {
            if (snapshots.TryGetValue(normalized, out var snapshot) && snapshot.HasBones)
                ApplySnapshot(reviewClone.transform, snapshot);

            reviewClone.name = "SafeLiftReviewAvatarClone_" + normalized;
            return true;
        }

        return false;
    }

    public bool HasSnapshot(string phase)
    {
        var normalized = NormalizePhase(phase);
        return !string.IsNullOrEmpty(normalized) && snapshots.ContainsKey(normalized);
    }

    public int CapturedPhaseCount
    {
        get
        {
            var count = 0;
            foreach (var phase in PhaseOrder)
                if (snapshots.ContainsKey(phase)) count++;
            return count;
        }
    }

    public bool HasCompletePhaseSet => CapturedPhaseCount == PhaseOrder.Length;

    public string SnapshotStatus()
    {
        var present = new List<string>();
        var missing = new List<string>();
        foreach (var phase in PhaseOrder)
        {
            if (snapshots.ContainsKey(phase)) present.Add(phase);
            else missing.Add(phase);
        }

        if (present.Count == 0)
            return "No recorded avatar phase yet - using fallback pose preview.";

        return "Recorded phases: " + string.Join(", ", present)
            + (missing.Count == 0 ? " (complete)" : " / Missing: " + string.Join(", ", missing));
    }

    public string SnapshotStatusForPhase(string phase)
    {
        var normalized = NormalizePhase(phase);
        if (string.IsNullOrEmpty(normalized)) normalized = "Standing";
        if (!snapshots.TryGetValue(normalized, out var snapshot) || snapshot == null || !snapshot.HasBones)
            return normalized + ": fallback preview - no saved humanoid pose yet.";

        return normalized + ": saved humanoid pose from " + snapshot.sourceAvatarName
            + " / quality " + snapshot.qualityScore.ToString("0.00");
    }

    private void Capture(string phase, float qualityScore)
    {
        if (string.IsNullOrEmpty(phase)) return;
        if (Time.unscaledTime - lastCaptureAt < 0.2f && lastCaptureKey == phase) return;

        var animator = ResolveSourceAnimator();
        if (animator == null) return;
        var rootObject = ResolveSourceAvatarRoot(animator);
        if (rootObject == null) return;

        var root = rootObject.transform;
        var transforms = root.GetComponentsInChildren<Transform>(true);
        var bones = new List<SafeLiftAvatarBonePose>(transforms.Length);
        foreach (var t in transforms)
        {
            bones.Add(new SafeLiftAvatarBonePose
            {
                path = PathFromRoot(root, t),
                localPosition = t.localPosition,
                localRotation = t.localRotation,
                localScale = t.localScale
            });
        }

        if (snapshots.TryGetValue(phase, out var previous) && previous.qualityScore > qualityScore)
            return;

        snapshots[phase] = new SafeLiftAvatarPoseSnapshot
        {
            phase = phase,
            sourceAvatarName = rootObject.name,
            timestamp = Time.realtimeSinceStartup,
            qualityScore = qualityScore,
            bones = bones.ToArray()
        };
        lastCaptureKey = phase;
        lastCaptureAt = Time.unscaledTime;
    }

    private bool TryEnsureReviewClone(Transform reviewParent, ref GameObject avatarRoot)
    {
        if (reviewClone != null)
        {
            avatarRoot = reviewClone;
            return true;
        }

        var animator = ResolveSourceAnimator();
        if (animator == null) return false;
        var sourceRoot = ResolveSourceAvatarRoot(animator);
        if (sourceRoot == null) return false;

        reviewClone = Instantiate(sourceRoot, reviewParent, false);
        reviewClone.name = "SafeLiftReviewAvatarClone";
        reviewClone.SetActive(true);
        StripRuntimeComponents(reviewClone);
        reviewClone.transform.localPosition = new Vector3(0f, 42f, -35f);
        reviewClone.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        reviewClone.transform.localScale = Vector3.one * 55f;
        avatarRoot = reviewClone;
        return true;
    }

    private Animator ResolveSourceAnimator()
    {
        if (sourceAnimator != null && sourceAnimator.gameObject != null)
        {
            if (sourceAvatarRoot == null) sourceAvatarRoot = ResolveUsableSourceRoot(sourceAnimator, null);
            return sourceAnimator;
        }

        foreach (var animator in FindObjectsOfType<Animator>(true))
        {
            if (animator == null || !animator.isHuman) continue;
            if (animator.GetComponentInParent<SafeLiftAvatarReviewController>() != null) continue;
            if (Contains(animator.name, "ReviewAvatar")) continue;
            if (Contains(animator.transform.root.name, "SafeLiftDemoWorldCanvas")) continue;
            sourceAnimator = animator;
            sourceAvatarRoot = ResolveUsableSourceRoot(animator, null);
            return sourceAnimator;
        }

        return null;
    }

    private GameObject ResolveSourceAvatarRoot(Animator animator)
    {
        if (sourceAvatarRoot != null && sourceAvatarRoot.scene.IsValid()) return sourceAvatarRoot;
        sourceAvatarRoot = ResolveUsableSourceRoot(animator, null);
        return sourceAvatarRoot;
    }

    private static GameObject ResolveUsableSourceRoot(Animator animator, GameObject preferredRoot)
    {
        if (animator == null) return null;

        if (preferredRoot != null
            && preferredRoot.scene.IsValid()
            && !preferredRoot.GetComponentInParent<SafeLiftAvatarReviewController>()
            && !Contains(preferredRoot.name, "SafeLiftDemoWorldCanvas")
            && !Contains(preferredRoot.name, "ReviewAvatar"))
            return preferredRoot;

        var cursor = animator.transform;
        while (cursor.parent != null)
        {
            var parentName = cursor.parent.name;
            if (Contains(parentName, "SafeLiftXRRuntime") || Contains(parentName, "SafeLiftDemoWorldCanvas") || Contains(parentName, "PostRepAvatarReviewPanel"))
                break;
            cursor = cursor.parent;
        }

        return cursor.gameObject;
    }

    private static void StripRuntimeComponents(GameObject root)
    {
        foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour == null) continue;
            var typeName = behaviour.GetType().Name;
            if (typeName.Contains("Receiver") || typeName.Contains("Driver") || typeName.Contains("Hud") || typeName.Contains("RuntimeMarker"))
                Destroy(behaviour);
        }
    }

    private static void ApplySnapshot(Transform root, SafeLiftAvatarPoseSnapshot snapshot)
    {
        var map = new Dictionary<string, Transform>();
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            map[PathFromRoot(root, t)] = t;

        foreach (var bone in snapshot.bones)
        {
            if (bone == null || string.IsNullOrEmpty(bone.path)) continue;
            if (!map.TryGetValue(bone.path, out var target)) continue;
            target.localPosition = bone.localPosition;
            target.localRotation = bone.localRotation;
            target.localScale = bone.localScale;
        }
    }

    private static string NormalizePhase(string phase)
    {
        if (string.IsNullOrWhiteSpace(phase)) return "";
        var p = phase.Trim().ToLowerInvariant();
        if (p.Contains("stand")) return "Standing";
        if (p.Contains("desc")) return "Descent";
        if (p.Contains("bottom")) return "Bottom";
        if (p.Contains("asc")) return "Ascent";
        return "";
    }

    private static bool Contains(string value, string token)
    {
        return !string.IsNullOrEmpty(value) && value.IndexOf(token, System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static float QualityScore(string quality)
    {
        if (string.IsNullOrWhiteSpace(quality)) return 0.25f;
        var q = quality.ToLowerInvariant();
        if (q.Contains("good")) return 1f;
        if (q.Contains("ok") || q.Contains("fair")) return 0.65f;
        if (q.Contains("bad") || q.Contains("uncertain")) return 0.1f;
        return 0.35f;
    }

    private static string PathFromRoot(Transform root, Transform t)
    {
        if (root == t) return ".";
        var stack = new Stack<string>();
        var cursor = t;
        while (cursor != null && cursor != root)
        {
            stack.Push(cursor.name);
            cursor = cursor.parent;
        }
        return string.Join("/", stack.ToArray());
    }
}
