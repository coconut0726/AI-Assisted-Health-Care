using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace HomeCare.Guidance
{
    /// <summary>
    /// A world-space uGUI button that hands can press in two ways:
    /// touch it with an index fingertip (poke), or aim at it with the hand ray and pinch
    /// (see <see cref="HandPointer"/>). Lights up while a finger or ray is on it.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public sealed class HandPokeButton : MonoBehaviour
    {
        public static readonly List<HandPokeButton> All = new();

        [Tooltip("The fingertip counts as touching within this distance in front of the button (metres).")]
        public float touchDepth = 0.02f;
        [Tooltip("Pull the finger back further than this before the button can fire again (metres).")]
        public float releaseDepth = 0.045f;

        static float s_LastPress;
        Button _button;
        RectTransform _rect;
        Image _image;
        Color _baseColor;
        bool _armed = true, _rayHover;
        float _pressedAt = -1;

        public bool Usable => _button != null && _button.interactable && _button.isActiveAndEnabled;
        public RectTransform Rect => _rect;

        void Awake()
        {
            _button = GetComponent<Button>();
            _rect = (RectTransform)transform;
            _image = GetComponent<Image>();
            _baseColor = _image != null ? _image.color : Color.white;
        }

        void OnEnable() => All.Add(this);
        void OnDisable() { All.Remove(this); _rayHover = false; }

        /// <summary>Where a world ray crosses this button's plane, if it lands inside the button.</summary>
        public bool Raycast(Ray ray, out float distance, out Vector3 point)
        {
            distance = 0;
            point = default;
            var plane = new Plane(-_rect.forward, _rect.position);
            if (!plane.Raycast(ray, out distance) || distance <= 0) return false;
            point = ray.GetPoint(distance);
            var local = _rect.InverseTransformPoint(point);
            return _rect.rect.Contains(new Vector2(local.x, local.y));
        }

        public void SetRayHover(bool hover) => _rayHover = hover;

        public bool Press()
        {
            if (!Usable || Time.unscaledTime - s_LastPress < 0.35f) return false;
            s_LastPress = Time.unscaledTime;
            transform.localScale = Vector3.one * 0.94f;
            _pressedAt = Time.unscaledTime;
            _button.onClick.Invoke();
            return true;
        }

        void Update()
        {
            if (_pressedAt >= 0 && Time.unscaledTime - _pressedAt > 0.15f)
            {
                transform.localScale = Vector3.one;
                _pressedAt = -1;
            }

            bool touching = false, near = false, hover = _rayHover;
            var hands = HandTracking.Instance;
            if (hands != null)
                foreach (var hand in hands.Hands)
                {
                    if (!hand.IsTracked || hand.IsSimulated) continue;
                    Vector3 local = _rect.InverseTransformPoint(hand.IndexTip);
                    if (!_rect.rect.Contains(new Vector2(local.x, local.y))) continue;
                    // Canvas faces the user along -forward, so a finger in front has negative z.
                    float depth = local.z * _rect.lossyScale.z;
                    if (depth > -0.08f && depth < 0.04f) hover = true;
                    if (depth > -touchDepth && depth < 0.04f) touching = true;
                    if (depth > -releaseDepth) near = true;
                }

            if (_image != null)
                _image.color = hover && Usable ?Color.Lerp(_baseColor, new Color(0.35f, 0.85f, 1f), 0.55f) : _baseColor;

            if (!near) _armed = true;
            if (touching && _armed && Press()) _armed = false;
        }
    }
}
