using System;
using UnityEngine;

/// <summary>
/// Owns the live GIST_AVATA / MediaPipe humanoid avatar relationship for the
/// SafeLift flow. It does not acquire pose or sensor data; it only discovers
/// the scene humanoid, registers it as the phase snapshot source, and keeps
/// the live avatar from visually colliding with the post-rep review clone.
/// </summary>
public sealed class SafeLiftAvatarRuntimeController : MonoBehaviour
{
    [SerializeField] private Animator liveAvatarAnimator;
    [SerializeField] private GameObject liveAvatarRoot;
    [SerializeField] private bool showLiveAvatarDuringSets = true;
    [SerializeField] private bool hideLiveAvatarDuringReview = true;
    [Header("Live avatar presentation")]
    [SerializeField] private float liveAvatarDistance = 1.7f;
    [SerializeField] private float liveAvatarLateral = -1.02f;
    [SerializeField] private float liveAvatarVertical = -1.38f;
    [SerializeField] private float liveAvatarScale = 0.68f;
    [SerializeField] private float liveAvatarTargetHeight = 1.45f;
    private bool liveAvatarPlacedForViewer;
    private bool hasSceneAvatarPose;
    private Vector3 sceneAvatarPosition;
    private Quaternion sceneAvatarRotation;
    private Vector3 sceneAvatarScale;

    private SafeLiftUIStateMachine stateMachine;
    private SafeLiftPhasePoseRecorder phaseRecorder;
    private bool configured;

    public Animator LiveAvatarAnimator => liveAvatarAnimator;
    public GameObject LiveAvatarRoot => liveAvatarRoot;
    // Rendering must not depend on an Animator. GIST_AVATA imports can be
    // Generic or can expose the Animator only on a child; the visible scene
    // root is still a valid live avatar in either case.
    public bool HasLiveAvatar => liveAvatarRoot != null;

    private void Awake()
    {
        ResolveDependencies();
    }

    private void OnEnable()
    {
        ResolveDependencies();
        Subscribe();
    }

    private void Start()
    {
        ResolveLiveAvatar();
        RegisterRecorderSource();
        ApplyStateVisibility(stateMachine != null ? stateMachine.State : SafeLiftScreenState.StartDashboard);
    }

    private void Update()
    {
        // Backend polling can restore a screen without emitting the local
        // StateChanged event. Keep the live avatar in sync every frame so it
        // cannot remain at its authoring position or stay hidden after reload.
        if (stateMachine == null) ResolveDependencies();
        if (!HasLiveAvatar) ResolveLiveAvatar();
        if (HasLiveAvatar && stateMachine != null)
            ApplyStateVisibility(stateMachine.State);
    }

    private void OnDisable()
    {
        if (stateMachine != null) stateMachine.StateChanged -= ApplyStateVisibility;
        configured = false;
    }

    public void Configure(SafeLiftUIStateMachine machine, SafeLiftPhasePoseRecorder recorder)
    {
        if (machine != null) stateMachine = machine;
        if (recorder != null) phaseRecorder = recorder;
        Subscribe();
        ResolveLiveAvatar();
        RegisterRecorderSource();
        ApplyStateVisibility(stateMachine != null ? stateMachine.State : SafeLiftScreenState.StartDashboard);
    }

    public bool TryRefreshLiveAvatar()
    {
        liveAvatarAnimator = null;
        liveAvatarRoot = null;
        liveAvatarPlacedForViewer = false;
        ResolveLiveAvatar();
        RegisterRecorderSource();
        return HasLiveAvatar;
    }

    public string StatusText()
    {
        if (!HasLiveAvatar)
            return "Avatar: no humanoid scene avatar found, review uses placeholder fallback";

        return "Avatar: live source '" + liveAvatarRoot.name + "' registered for phase replay";
    }

    public string DiagnosticsLabel()
    {
        if (!HasLiveAvatar) return "avatar fallback";
        return "avatar live:" + liveAvatarRoot.name;
    }

    private void ResolveDependencies()
    {
        if (stateMachine == null) stateMachine = GetComponent<SafeLiftUIStateMachine>() ?? FindObjectOfType<SafeLiftUIStateMachine>();
        if (phaseRecorder == null) phaseRecorder = GetComponent<SafeLiftPhasePoseRecorder>() ?? SafeLiftPhasePoseRecorder.Instance ?? FindObjectOfType<SafeLiftPhasePoseRecorder>();
    }

    private void Subscribe()
    {
        if (configured || stateMachine == null) return;
        stateMachine.StateChanged += ApplyStateVisibility;
        configured = true;
    }

    private void ResolveLiveAvatar()
    {
        if (liveAvatarAnimator != null && liveAvatarAnimator.gameObject != null)
        {
            liveAvatarRoot = ResolveAvatarRoot(liveAvatarAnimator);
            return;
        }

        Animator best = null;
        var bestScore = int.MinValue;
        foreach (var animator in FindObjectsOfType<Animator>(true))
        {
            if (!IsUsableLiveAvatar(animator)) continue;
            var score = ScoreAnimator(animator);
            if (score <= bestScore) continue;
            best = animator;
            bestScore = score;
        }

        liveAvatarAnimator = best;
        liveAvatarRoot = best != null ? ResolveAvatarRoot(best) : null;

        // Prefer the humanoid that is actually owned by the MediaPipe
        // retarget driver. The dashboard also contains review/XR animators;
        // selecting one of those makes the UI card look static while the
        // real tracked avatar is updated elsewhere.
        var trackingDriver = FindObjectOfType<ActionTrackingAvatarDriver>(true);
        if (trackingDriver != null && trackingDriver.animator != null &&
            trackingDriver.animator.isHuman)
        {
            liveAvatarAnimator = trackingDriver.animator;
            liveAvatarRoot = trackingDriver.avatarRoot != null
                ? trackingDriver.avatarRoot.gameObject
                : ResolveAvatarRoot(trackingDriver.animator);
        }

        // Final fallback for the scene's selected avatar. This also covers a
        // prefab whose visible meshes have no Animator component at all.
        if (liveAvatarRoot == null)
        {
            var direct = GameObject.Find("MyAvatar");
            if (direct != null)
            {
                liveAvatarRoot = direct;
                liveAvatarAnimator = direct.GetComponentInChildren<Animator>(true);
            }
        }
        // The dashboard scene's MyAvatar is the object driven by the primary
        // MediaPipe receiver. Prefer it explicitly over any helper/review
        // Animator that may have a higher discovery score.
        var sceneAvatar = GameObject.Find("MyAvatar");
        if (sceneAvatar != null)
        {
            liveAvatarRoot = sceneAvatar;
            if (liveAvatarAnimator == null)
                liveAvatarAnimator = sceneAvatar.GetComponentInChildren<Animator>(true);
        }
        CacheSceneAvatarPose();
    }

    private void RegisterRecorderSource()
    {
        if (phaseRecorder == null) return;
        if (liveAvatarAnimator == null) ResolveLiveAvatar();
        if (liveAvatarAnimator != null) phaseRecorder.SetSourceAnimator(liveAvatarAnimator, liveAvatarRoot);
    }

    private void ApplyStateVisibility(SafeLiftScreenState state)
    {
        if (!HasLiveAvatar) return;

        // The dashboard is only a preflight screen. The live Avatar belongs
        // to calibration and exercise screens; showing it on the dashboard
        // makes the initial UI appear to be already in the exercise view.
        var shouldShow = state == SafeLiftScreenState.TutorialCalibration ||
                         state == SafeLiftScreenState.Set1Practice ||
                         state == SafeLiftScreenState.Set2LiveFeedback;

        if (hideLiveAvatarDuringReview &&
            (state == SafeLiftScreenState.PostRepReview || state == SafeLiftScreenState.QRShare))
            shouldShow = false;

        if (!showLiveAvatarDuringSets)
            shouldShow = false;

        // Activate before placement. SkinnedMeshRenderer.bounds are invalid
        // while the avatar hierarchy is inactive, which previously caused the
        // viewer-relative placement to be computed from an empty/off-screen
        // object and made the selected avatar appear to be missing.
        if (shouldShow && !liveAvatarRoot.activeSelf)
            liveAvatarRoot.SetActive(true);

        if (shouldShow && !liveAvatarPlacedForViewer)
            PlaceLiveAvatarForViewer();

        if (!shouldShow && liveAvatarRoot.activeSelf)
            liveAvatarRoot.SetActive(false);
    }

    private void PlaceLiveAvatarForViewer()
    {
        if (!HasLiveAvatar || liveAvatarRoot == null) return;

        // The live humanoid is rendered through SafeLiftAvatarUIPreview. Do
        // not move it in front of the desktop/XR viewer: that produced a
        // second giant, clipped avatar outside the UI card while the RawImage
        // showed a different small copy. Keep the authored scene pose and let
        // the preview camera frame it.
        CacheSceneAvatarPose();
        if (hasSceneAvatarPose)
        {
            var authoredRoot = liveAvatarRoot.transform;
            authoredRoot.position = sceneAvatarPosition;
            authoredRoot.rotation = sceneAvatarRotation;
            authoredRoot.localScale = sceneAvatarScale;
        }
        return;

        /*
        var view = Camera.main;
        if (view == null) view = FindObjectOfType<Camera>();
        // The desktop XR preview can render through an untagged camera while
        // the OpenXR camera is still booting. Do not abandon placement in that
        // window; use the scene's calibrated viewer pose as a deterministic
        // fallback and let the next frame replace it when a camera is found.
        var viewerPosition = view != null ? view.transform.position : new Vector3(0f, 1.45f, 0.8f);
        var viewerForward = view != null ? view.transform.forward : Vector3.forward;
        var viewerUp = view != null ? view.transform.up : Vector3.up;

        var root = liveAvatarRoot.transform;
        var forward = viewerForward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
        forward.Normalize();
        var right = new Vector3(forward.z, 0f, -forward.x);
        var target = viewerPosition + forward * Mathf.Max(1.5f, liveAvatarDistance)
            + right * liveAvatarLateral + viewerUp * liveAvatarVertical;

        root.position = target;
        root.rotation = Quaternion.LookRotation(viewerPosition - root.position, Vector3.up);
        root.localScale = Vector3.one;
        // Imported avatar prefabs can arrive with renderers disabled or with a
        // SkinnedMeshRenderer that stops updating when the old scene position is
        // outside the camera frustum. Make the live presentation self-contained.
        var renderers = liveAvatarRoot.GetComponentsInChildren<Renderer>(true);
        var enabledRendererCount = 0;
        foreach (var renderer in renderers)
        {
            if (renderer == null) continue;
            renderer.enabled = true;
            renderer.forceRenderingOff = false;
            if (renderer is SkinnedMeshRenderer skinned)
                skinned.updateWhenOffscreen = true;
            if (renderer.enabled && renderer.gameObject.activeInHierarchy)
                enabledRendererCount++;
        }

        // Imported avatars do not share a reliable authoring unit scale.
        // Normalize from actual world bounds so the avatar is visible as a
        // full body instead of being microscopic or filling the whole view.
        var bounds = new Bounds(root.position, Vector3.zero);
        var hasBounds = false;
        foreach (var renderer in renderers)
        {
            if (renderer == null || !renderer.enabled) continue;
            if (!hasBounds) { bounds = renderer.bounds; hasBounds = true; }
            else bounds.Encapsulate(renderer.bounds);
        }
        var fitScale = Mathf.Clamp(liveAvatarScale, 0.35f, 1.2f);
        if (hasBounds && bounds.size.y > 0.01f)
            fitScale = Mathf.Clamp(liveAvatarTargetHeight / bounds.size.y, 0.05f, 2.0f);
        root.localScale = Vector3.one * fitScale;

        // Keep the feet on the viewer's reference height after fitting.
        bounds = new Bounds(root.position, Vector3.zero);
        hasBounds = false;
        foreach (var renderer in renderers)
        {
            if (renderer == null || !renderer.enabled) continue;
            if (!hasBounds) { bounds = renderer.bounds; hasBounds = true; }
            else bounds.Encapsulate(renderer.bounds);
        }
        if (hasBounds)
            root.position += Vector3.up * ((viewerPosition.y + liveAvatarVertical) - bounds.min.y);
        liveAvatarPlacedForViewer = true;
        Debug.Log("SafeLift live avatar placed for viewer: root=" + root.name
            + " position=" + root.position.ToString("F2")
            + " scale=" + root.localScale.x.ToString("F2")
            + " renderers=" + renderers.Length + "/" + enabledRendererCount);
        */
    }

    private void CacheSceneAvatarPose()
    {
        if (hasSceneAvatarPose || liveAvatarRoot == null) return;
        var root = liveAvatarRoot.transform;
        sceneAvatarPosition = root.position;
        sceneAvatarRotation = root.rotation;
        sceneAvatarScale = root.localScale;
        hasSceneAvatarPose = true;
    }

    private static bool IsUsableLiveAvatar(Animator animator)
    {
        if (animator == null) return false;
        // Some GIST_AVATA imports use a Generic avatar even though the scene
        // object is the selected visible avatar. Keep it eligible for live
        // presentation; humanoid-only pose replay remains optional.
        if (!animator.isHuman &&
            !Contains(animator.gameObject.name, "myavatar") &&
            !Contains(animator.transform.root != null ? animator.transform.root.name : string.Empty, "myavatar"))
            return false;
        if (animator.GetComponentInParent<SafeLiftAvatarReviewController>() != null) return false;

        var root = animator.transform.root;
        var rootName = root != null ? root.name : string.Empty;
        var name = animator.gameObject.name;
        // MyAvatar may be instantiated below the fallback world canvas. Do
        // not reject it merely because that canvas is its scene root; only
        // reject unrelated canvas helpers.
        if ((Contains(rootName, "SafeLiftDemoWorldCanvas") && !Contains(name, "myavatar"))
            || Contains(name, "SafeLiftReviewAvatar") || Contains(rootName, "SafeLiftReviewAvatar"))
            return false;
        if (Contains(rootName, "SafeLiftReviewAvatarClone") || Contains(name, "SafeLiftReviewAvatarClone"))
            return false;

        return true;
    }

    private static int ScoreAnimator(Animator animator)
    {
        var root = animator.transform.root;
        var text = ((root != null ? root.name : string.Empty) + " " + animator.gameObject.name).ToLowerInvariant();
        var score = 0;
        if (text.Contains("myavatar")) score += 1000;
        if (text.Contains("avatar")) score += 30;
        if (text.Contains("mediapipe")) score += 20;
        if (text.Contains("action")) score += 10;
        if (text.Contains("gist")) score += 10;
        if (animator.gameObject.activeInHierarchy) score += 5;
        return score;
    }

    private static GameObject ResolveAvatarRoot(Animator animator)
    {
        if (animator == null) return null;

        var cursor = animator.transform;
        while (cursor.parent != null)
        {
            var parentName = cursor.parent.name;
            if (Contains(parentName, "SafeLiftXRRuntime") || Contains(parentName, "SafeLiftDemoWorldCanvas"))
                break;
            cursor = cursor.parent;
        }

        return cursor.gameObject;
    }

    private static bool Contains(string value, string token)
    {
        return !string.IsNullOrEmpty(value) && value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
