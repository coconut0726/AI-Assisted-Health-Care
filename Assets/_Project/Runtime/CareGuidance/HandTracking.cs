using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR.Hands;
#if UNITY_EDITOR
using UnityEngine.InputSystem;
#endif

namespace HomeCare.Guidance
{
    public enum HandSide { Left, Right }

    /// <summary>One hand's pose and gestures for the current frame, in world space.</summary>
    public sealed class TrackedHand
    {
        public HandSide Side;
        public bool IsTracked;
        /// <summary>True for the Editor mouse stand-in. Poke buttons ignore it (the mouse clicks them directly).</summary>
        public bool IsSimulated;
        public Vector3 IndexTip, ThumbTip, Palm, IndexKnuckle;
        public Quaternion PalmRotation = Quaternion.identity;
        public bool IsPinching, IsGrabbing;
        /// <summary>Pinch or fist: the hand is closed around something.</summary>
        public bool IsHolding => IsPinching || IsGrabbing;
        public bool HoldStarted, HoldEnded, PinchStarted;
        /// <summary>Set by <see cref="HandPointer"/> when the hand ray is on a button, so a pinch presses it instead of grabbing.</summary>
        public bool PointingAtUi;
        public float PinchHeldFor;
        /// <summary>Average fingertip-to-palm distance (metres); small means a fist.</summary>
        public float Curl;
        public Vector3 HoldPoint => IsGrabbing && !IsPinching ? Palm : (IndexTip + ThumbTip) * 0.5f;
    }

    /// <summary>
    /// Reads both hands from the XR Hands subsystem (OpenXR hand tracking on Quest) and turns
    /// joint positions into simple gestures: pinch (thumb + index) and grab (fist).
    /// No controllers are used. In the Editor, when no hand is tracked, the mouse acts as the
    /// right hand: hold the left button on an object to pick it up and drag it.
    /// </summary>
    [DefaultExecutionOrder(-500)]
    public sealed class HandTracking : MonoBehaviour
    {
        public static HandTracking Instance { get; private set; }

        [Tooltip("The camera rig's tracking space. Hand joints are reported relative to it.")]
        public Transform trackingSpace;
        [Tooltip("Camera used for the Editor mouse stand-in.")]
        public Camera viewCamera;

        [Header("Gesture thresholds (metres)")]
        public float pinchOn = 0.02f;
        public float pinchOff = 0.035f;
        public float grabOn = 0.05f;
        public float grabOff = 0.065f;

        public readonly TrackedHand Left = new() { Side = HandSide.Left };
        public readonly TrackedHand Right = new() { Side = HandSide.Right };
        public IEnumerable<TrackedHand> Hands { get { yield return Left; yield return Right; } }

        XRHandSubsystem _subsystem;
        static readonly List<XRHandSubsystem> s_Subsystems = new();
#if UNITY_EDITOR
        float _simDepth = 0.5f;
#endif

        void OnEnable() => Instance = this;
        void OnDisable() { if (Instance == this) Instance = null; }

        void Update()
        {
            if (_subsystem == null || !_subsystem.running)
            {
                _subsystem = null;
                SubsystemManager.GetSubsystems(s_Subsystems);
                foreach (var s in s_Subsystems)
                    if (s.running) { _subsystem = s; break; }
            }
            Read(Left, _subsystem != null ? _subsystem.leftHand : default);
            Read(Right, _subsystem != null ? _subsystem.rightHand : default);
#if UNITY_EDITOR
            if (!Right.IsTracked && !Left.IsTracked) SimulateWithMouse(Right);
#endif
            Status = (_subsystem == null ? "Hand tracking: not running" : "Hands:") +
                     (_subsystem == null ? "" : $" L {Describe(Left)} · R {Describe(Right)}");
            if (Time.unscaledTime >= _nextLog)
            {
                _nextLog = Time.unscaledTime + 3f;
                Debug.Log($"[HomeCare] {Status} | subsystems={s_Subsystems.Count} | curl L={Left.Curl:F3} R={Right.Curl:F3} | pinch gap L={Vector3.Distance(Left.IndexTip, Left.ThumbTip):F3} R={Vector3.Distance(Right.IndexTip, Right.ThumbTip):F3}");
            }
        }

        /// <summary>Short on-panel summary, e.g. "Hands: L tracked · R pinch".</summary>
        public string Status { get; private set; } = "";
        float _nextLog;

        static string Describe(TrackedHand h) =>
            !h.IsTracked ? "not seen" : h.IsSimulated ? "mouse" : h.IsPinching ? "pinch" : h.IsGrabbing ? "fist" : "tracked";

        void Read(TrackedHand hand, XRHand source)
        {
            bool wasHolding = hand.IsHolding;
            hand.IsSimulated = false;
            Pose index = default, thumb = default, palm = default;
            hand.IsTracked = source.isTracked &&
                             TryJoint(source, XRHandJointID.IndexTip, out index) &&
                             TryJoint(source, XRHandJointID.ThumbTip, out thumb) &&
                             TryJoint(source, XRHandJointID.Palm, out palm);
            if (!hand.IsTracked)
            {
                hand.IsPinching = hand.IsGrabbing = false;
                Finish(hand, wasHolding);
                return;
            }
            hand.IndexTip = index.position;
            hand.ThumbTip = thumb.position;
            hand.Palm = palm.position;
            hand.IndexKnuckle = TryJoint(source, XRHandJointID.IndexProximal, out var knuckle) ? knuckle.position : palm.position;
            hand.PalmRotation = palm.rotation;

            float pinch = Vector3.Distance(hand.IndexTip, hand.ThumbTip);
            hand.IsPinching = hand.IsPinching ? pinch < pinchOff : pinch < pinchOn;

            float curl = 0;
            int count = 0;
            foreach (var id in new[] { XRHandJointID.IndexTip, XRHandJointID.MiddleTip, XRHandJointID.RingTip, XRHandJointID.LittleTip })
                if (TryJoint(source, id, out var tip)) { curl += Vector3.Distance(tip.position, hand.Palm); count++; }
            curl = count > 0 ? curl / count : float.MaxValue;
            hand.Curl = curl;
            hand.IsGrabbing = hand.IsGrabbing ? curl < grabOff : curl < grabOn;
            Finish(hand, wasHolding);
        }

        static void Finish(TrackedHand hand, bool wasHolding)
        {
            hand.PinchStarted = hand.IsPinching && hand.PinchHeldFor == 0;
            hand.HoldStarted = !wasHolding && hand.IsHolding;
            hand.HoldEnded = wasHolding && !hand.IsHolding;
            hand.PinchHeldFor = hand.IsPinching ? hand.PinchHeldFor + Time.deltaTime : 0;
        }

        bool TryJoint(XRHand hand, XRHandJointID id, out Pose pose)
        {
            if (!hand.GetJoint(id).TryGetPose(out pose)) return false;
            if (trackingSpace != null)
                pose = new Pose(trackingSpace.TransformPoint(pose.position), trackingSpace.rotation * pose.rotation);
            return true;
        }

#if UNITY_EDITOR
        void SimulateWithMouse(TrackedHand hand)
        {
            bool wasHolding = hand.IsHolding;
            var mouse = Mouse.current;
            var cam = viewCamera != null ? viewCamera : Camera.main;
            hand.IsTracked = mouse != null && cam != null;
            if (!hand.IsTracked) { hand.IsPinching = hand.IsGrabbing = false; Finish(hand, wasHolding); return; }
            hand.IsSimulated = true;
            var ray = cam.ScreenPointToRay(mouse.position.ReadValue());
            if (mouse.leftButton.wasPressedThisFrame)
            {
                bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
                _simDepth = !overUi && Physics.Raycast(ray, out var hit, 5f) ? hit.distance : 0.5f;
                hand.IsPinching = !overUi;
            }
            else if (!mouse.leftButton.isPressed) hand.IsPinching = false;
            hand.IsGrabbing = false;
            hand.IndexTip = hand.ThumbTip = hand.Palm = hand.IndexKnuckle = ray.GetPoint(_simDepth);
            hand.PalmRotation = cam.transform.rotation;
            Finish(hand, wasHolding);
        }
#endif
    }
}
