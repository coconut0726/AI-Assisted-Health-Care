using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HomeCare.AR;
using HomeCare.Procedures;
using Meta.XR;
using UnityEngine;

namespace HomeCare.Guidance
{
    /// <summary>The result of one "find this object" request.</summary>
    public sealed class DetectionOutcome
    {
        public string Request;
        public DetectionResult Result;
        public byte[] Jpeg;
        public string FrameSource;
        public int DepthSamples;
        /// <summary>Where the camera was when the image was taken.</summary>
        public Vector3 CameraPosition;
        public readonly List<(DetectedObject detection, DetectedObjectMarker marker)> Placed = new();
        /// <summary>A newer request started before this one finished; nothing was shown.</summary>
        public bool Superseded;
    }

    /// <summary>
    /// Finds real objects by name in the passthrough camera view and anchors guidance on them
    /// (pipeline after Guided Reality, UIST '25):
    /// <list type="number">
    /// <item>Capture a camera frame, its pose, and a depth grid at that moment.</item>
    /// <item>Gemini (through the PC bridge) cleans up the request, returns 2D boxes and step-specific guidance.</item>
    /// <item>Each box is lifted into the room with depth and marked; move / apply steps get an arrow and a
    ///       moving ghost box to the target, wait steps a countdown.</item>
    /// </list>
    /// <see cref="CareGuidanceController"/> calls this from the panel's Find items button (F in the Editor).
    /// </summary>
    public sealed class ObjectDetectionGuidance : MonoBehaviour
    {
        [Header("Object detection (real objects in the camera view)")]
        [Tooltip("Editor: F looks for this. Leave empty to look for the items the current step names.")]
        public string testPrompt = "";
        [Tooltip("Look for the step's items automatically each time a step starts (one Gemini call per step).")]
        public bool findOnNewStep;
        [Tooltip("Distance used when no depth is available (Editor test photo, or depth not ready), metres.")]
        public float defaultDistance = 0.6f;
        [Tooltip("Outlines are never narrower or shorter than this (metres), so small objects stay easy to see.")]
        public float minBoxEdge = 0.05f;
        [Tooltip("Depth samples taken across the image when the frame is captured.")]
        public Vector2Int depthGrid = new(24, 18);
        public float requestTimeoutSeconds = 45;

        const string ScenePermission = "com.oculus.permission.USE_SCENE";

        AfterStepCamera _camera;
        Transform _head, _root;
        GuidanceArrow _arrow;
        EnvironmentRaycastManager _environment;
        readonly List<DetectedObjectMarker> _markers = new();
        readonly List<GameObject> _extras = new();
        int _request;

        public bool Busy { get; private set; }
        public bool HasDepth => _environment != null && _environment.isActiveAndEnabled;

        public void Init(AfterStepCamera camera, Material lineMaterial, Transform head)
        {
            _camera = camera;
            _head = head;
            _root = new GameObject("Detected Objects").transform;
            _root.SetParent(transform, false);
            _arrow = GuidanceArrow.Create(_root, lineMaterial);
#if UNITY_ANDROID && !UNITY_EDITOR
            StartCoroutine(StartDepth());
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        // Depth needs the spatial data permission; ask once, then start the depth raycaster.
        IEnumerator StartDepth()
        {
            if (!EnvironmentRaycastManager.IsSupported)
            {
                Debug.Log("[HomeCare] Depth raycasting is not supported; detected objects use a default distance.");
                yield break;
            }
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(ScenePermission))
            {
                UnityEngine.Android.Permission.RequestUserPermission(ScenePermission);
                float deadline = Time.realtimeSinceStartup + 60;
                while (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(ScenePermission) && Time.realtimeSinceStartup < deadline)
                    yield return null;
                if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(ScenePermission))
                {
                    Debug.LogWarning("[HomeCare] Spatial data permission was not granted; detected objects use a default distance.");
                    yield break;
                }
            }
            _environment = FindAnyObjectByType<EnvironmentRaycastManager>();
            if (_environment == null) _environment = new GameObject("Environment Raycast").AddComponent<EnvironmentRaycastManager>();
        }
#endif

        /// <summary>
        /// Looks for <paramref name="request"/> (one or more object names, or a vague description) in the current
        /// camera view and places markers and cues. Throws on capture, bridge or model errors.
        /// </summary>
        /// <param name="boxesOnly">Only outline what was found (no guidance card, arrow or hint), and add to the boxes
        /// already shown instead of replacing them: for the supply check before any step has started.</param>
        public async Task<DetectionOutcome> FindAsync(string request, ProcedureRequirements procedure, ProcedureStep step,
            IEnumerable<string> stepItemNames, string endpoint, string model, CancellationToken cancellation, bool boxesOnly = false)
        {
            if (_camera == null) throw new InvalidOperationException("Object detection was not initialised.");
            // Invalidate any earlier request; its boxes stay until this result replaces them (a refresh does not blink).
            int id = ++_request;
            Busy = true;
            var outcome = new DetectionOutcome { Request = request };
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                timeout.CancelAfter(TimeSpan.FromSeconds(requestTimeoutSeconds));
                var frame = await _camera.CaptureFrameAsync(timeout.Token);
                var lifter = SpatialLifter.Capture(frame, _environment, Mathf.Max(2, depthGrid.x), Mathf.Max(2, depthGrid.y), defaultDistance);
                outcome.Jpeg = frame.Jpeg;
                outcome.FrameSource = frame.Source;
                outcome.DepthSamples = lifter.DepthSamples;
                outcome.CameraPosition = frame.CameraPose.position;

                string body = DetectionProtocol.CreateRequest(procedure, step, request, stepItemNames, frame.Jpeg, model);
                var response = await VerificationBridgeClient.PostAsync(endpoint, body, timeout.Token);
                outcome.Result = DetectionProtocol.Parse(response);
                if (this == null || id != _request) { outcome.Superseded = true; return outcome; }
                if (boxesOnly)
                {
                    PlaceBoxes(outcome, lifter);
                    return outcome;
                }
                RemoveMarkers();
                Place(outcome, lifter);
                return outcome;
            }
            finally
            {
                if (id == _request) Busy = false;
            }
        }

        void PlaceBoxes(DetectionOutcome outcome, SpatialLifter lifter)
        {
            foreach (var detection in outcome.Result.objects.Where(o => o.found))
            {
                var marker = DetectedObjectMarker.Create(detection, lifter.Lift(detection.ViewportRect), _root, _head, MarkerStyle.Secondary, minBoxEdge);
                _markers.Add(marker);
                outcome.Placed.Add((detection, marker));
            }
        }

        void Place(DetectionOutcome outcome, SpatialLifter lifter)
        {
            var result = outcome.Result;
            bool used = result.visual_type != "none";
            foreach (var detection in result.objects.Where(o => o.found))
            {
                // Figma: objects this step uses get the solid sage outline; the rest the dashed white one.
                var style = used && detection.role != "context" ? MarkerStyle.Active : MarkerStyle.Secondary;
                var marker = DetectedObjectMarker.Create(detection, lifter.Lift(detection.ViewportRect), _root, _head, style, minBoxEdge);
                _markers.Add(marker);
                outcome.Placed.Add((detection, marker));
            }

            var primary = _markers.FirstOrDefault(m => m.Detection == result.Primary) ?? _markers.FirstOrDefault();
            var target = _markers.FirstOrDefault(m => m.Detection == result.Target);
            bool moves = (result.visual_type == "move_to_target" || result.visual_type == "apply_to_target") &&
                         primary != null && target != null && primary != target;
            if (moves) _arrow.Show(primary.SideToward(target.AnchorPoint), target.AnchorPoint);

            if (primary != null)
                primary.ShowCard(CardHeader(result, primary, target), result.guidance_text, result.caution,
                    result.visual_type == "wait" ? result.timer_seconds : 0);

            // Hint chip beside the object's top-right corner (VP3/VP4), clear of the card above it.
            if (primary != null && !string.IsNullOrWhiteSpace(result.hint) && used)
                _extras.Add(DetectedObjectMarker.Chip(_root, primary.RightOfTop, result.hint, _head));
        }

        static string CardHeader(DetectionResult result, DetectedObjectMarker primary, DetectedObjectMarker target) => result.visual_type switch
        {
            "move_to_target" when target != null => "Move to the " + target.Detection.label,
            "apply_to_target" when target != null => "Use on the " + target.Detection.label,
            "wait" => "Wait",
            "none" => "Not needed in this step",
            _ => "For this step",
        };

        /// <summary>Removes all markers and cancels the effect of any request still in flight.</summary>
        public void Clear()
        {
            _request++;
            Busy = false;
            RemoveMarkers();
        }

        /// <summary>The boxes shown in the room now.</summary>
        public IReadOnlyList<DetectedObjectMarker> Markers => _markers;

        void RemoveMarkers()
        {
            foreach (var marker in _markers) if (marker != null) Destroy(marker.gameObject);
            _markers.Clear();
            foreach (var extra in _extras) if (extra != null) Destroy(extra);
            _extras.Clear();
            if (_arrow != null) _arrow.Hide();
        }
    }
}
