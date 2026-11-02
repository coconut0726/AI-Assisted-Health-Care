using System;
using System.Collections.Generic;
using UnityEngine;

namespace HomeCare.Guidance
{
    /// <summary>
    /// Pick up a <see cref="CareItem"/> by pinching or making a fist next to it; it follows the
    /// hand until the hand opens. Raises events so the guidance can react to what is held.
    /// </summary>
    public sealed class HandGrabber : MonoBehaviour
    {
        [Tooltip("How close the pinch point or palm must be to an item's box to pick it up (metres).")]
        public float reach = 0.035f;

        public event Action<CareItem, TrackedHand> Grabbed;
        public event Action<CareItem, TrackedHand> Released;

        sealed class Hold
        {
            public CareItem item;
            public Vector3 positionOffset;
            public Quaternion rotationOffset;
        }

        readonly Dictionary<HandSide, Hold> _holds = new();

        public CareItem HeldBy(HandSide side) => _holds.TryGetValue(side, out var h) ? h.item : null;

        public bool IsHeld(CareItem item)
        {
            foreach (var h in _holds.Values) if (h.item == item) return true;
            return false;
        }

        void Update()
        {
            var hands = HandTracking.Instance;
            if (hands == null) return;
            foreach (var hand in hands.Hands)
            {
                _holds.TryGetValue(hand.Side, out var hold);
                if (hold != null && (hold.item == null || !hand.IsTracked || !hand.IsHolding))
                {
                    _holds.Remove(hand.Side);
                    if (hold.item != null) Released?.Invoke(hold.item, hand);
                    continue;
                }
                if (hold != null)
                {
                    var pose = HandPose(hand);
                    hold.item.transform.SetPositionAndRotation(pose.position + pose.rotation * hold.positionOffset,
                        pose.rotation * hold.rotationOffset);
                    continue;
                }
                if (hand.HoldStarted && !hand.PointingAtUi) TryGrab(hand);
            }
        }

        static Pose HandPose(TrackedHand hand) => new(hand.HoldPoint, hand.PalmRotation);

        void TryGrab(TrackedHand hand)
        {
            CareItem best = null;
            float bestDistance = reach;
            foreach (var item in CareItem.FindAllInScene())
            {
                if (!item.grabbable || IsHeld(item)) continue;
                float d = item.DistanceTo(hand.HoldPoint);
                if (d <= bestDistance) { best = item; bestDistance = d; }
            }
            if (best == null) return;
            var pose = HandPose(hand);
            var inverse = Quaternion.Inverse(pose.rotation);
            _holds[hand.Side] = new Hold
            {
                item = best,
                positionOffset = inverse * (best.transform.position - pose.position),
                rotationOffset = inverse * best.transform.rotation,
            };
            Grabbed?.Invoke(best, hand);
        }
    }
}
