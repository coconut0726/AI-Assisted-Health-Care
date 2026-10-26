using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AHC.VR
{
    /// <summary>
    /// The AI as a character: the ghost from Blender hovers at a fixed spot next to the burn on
    /// the table (it does not follow the user's gaze), bobs and blinks like its Blender loop,
    /// turns to face the user, and speaks each instruction in a speech bubble.
    /// Spawned by <see cref="VRDemoController"/> under the workspace when it has a ghost model,
    /// so it moves only when the user repositions the workspace (hold right trigger).
    /// </summary>
    public class GhostCompanion : MonoBehaviour
    {
        const float BubbleScale = 0.0006f;

        [Tooltip("Height of the ghost in metres.")]
        public float height = 0.24f;
        [Tooltip("Fixed spot relative to the workspace origin (the burn), in metres: " +
                 "+X to the user's right, +Y up from the table, +Z away from the user.")]
        public Vector3 anchorOffset = new(0.55f, 0.32f, 0.12f);

        Transform _head;
        Transform _pivot, _anim, _model;
        Transform[] _eyes;
        Vector3[] _eyeScales;
        float _nextBlink, _talkUntil, _hop;
        TextMeshProUGUI _bubbleText;
        GameObject _bubble;

        public static GhostCompanion Spawn(GameObject modelPrefab, Transform workspace, Transform head)
        {
            var root = new GameObject("AI Ghost").AddComponent<GhostCompanion>();
            root.transform.SetParent(workspace, false);
            root.transform.localPosition = root.anchorOffset;
            root._head = head;
            root.Build(modelPrefab);
            return root;
        }

        void Build(GameObject modelPrefab)
        {
            // root → pivot (size, facing, bob) → anim (sway, nod, squash) → model (centred)
            _pivot = new GameObject("Model pivot").transform;
            _pivot.SetParent(transform, false);
            _anim = new GameObject("Float").transform;
            _anim.SetParent(_pivot, false);
            _model = Instantiate(modelPrefab, _anim).transform;
            _model.name = "Ghost";

            // Centre the model so it turns and squashes around its middle.
            var bounds = Bounds(_model);
            _model.localPosition -= _anim.InverseTransformPoint(bounds.center);

            // Make the face point along +Z, i.e. toward the user once the root looks at them.
            var face = FindDeep(_model, "Ghost_Face");
            if (face != null && face.TryGetComponent<Renderer>(out var fr))
            {
                Vector3 toFace = fr.bounds.center - bounds.center;
                if (Vector3.Dot(toFace, transform.forward) < 0) _pivot.localRotation = Quaternion.Euler(0, 180, 0);
            }
            _pivot.localScale = Vector3.one * (height / Mathf.Max(0.001f, bounds.size.y));

            _eyes = System.Array.FindAll(new[] { FindDeep(_model, "Ghost_Eye_L"), FindDeep(_model, "Ghost_Eye_R") }, e => e != null);
            _eyeScales = System.Array.ConvertAll(_eyes, e => e.localScale);

            BuildBubble();
            _nextBlink = Time.time + Random.Range(2f, 4f);
        }

        void BuildBubble()
        {
            var canvas = Ui.WorldCanvas("Speech bubble", transform, new Vector2(10, 10), BubbleScale, null);
            // Bubble floats just above the ghost's ears, so it never covers the panels' buttons.
            canvas.transform.localPosition = new Vector3(0, height * 0.65f, 0);
            canvas.sortingOrder = 12;
            _bubble = canvas.gameObject;

            var card = Ui.Card(canvas.transform, "Bubble", 380, Theme.Panel, new RectOffset(24, 24, 18, 20), 8, 26);
            Ui.Place(card.rectTransform, new Vector2(0.5f, 0), Vector2.zero, 380);
            var head = Ui.Row(card.transform, "Head", 8);
            var dot = Ui.Image(head.transform, "Dot", Theme.Primary, Sprites.Circle());
            Ui.Fixed(dot.gameObject, 12, 12);
            Ui.Text(head.transform, "Care guide", 15, Theme.Primary, bold: true, wrap: false);
            _bubbleText = Ui.Text(card.transform, "", 22, Theme.Text, bold: true);
        }

        /// <summary>Show a line in the bubble and play a little "talking" hop.</summary>
        public void Say(string text)
        {
            if (_bubbleText == null) return;
            _bubble.SetActive(!string.IsNullOrEmpty(text));
            _bubbleText.text = text ?? "";
            _hop = 1f;
            _talkUntil = Time.time + Mathf.Clamp(text?.Length * 0.06f ?? 0f, 1f, 5f);
        }

        public void Repeat()
        {
            _hop = 1f;
            _talkUntil = Time.time + 2f;
        }

        public void SetVisible(bool visible) => gameObject.SetActive(visible);

        void LateUpdate()
        {
            if (_head == null) return;
            float t = Time.time;

            // Stay put at the anchor (editable live in the Inspector); only turn to face the user.
            transform.localPosition = anchorOffset;
            Vector3 look = Vector3.ProjectOnPlane(_head.position - transform.position, Vector3.up);
            if (look.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.LookRotation(look, Vector3.up);
            if (_bubble != null) _bubble.transform.rotation = Quaternion.LookRotation(-look, Vector3.up);

            // Float loop from the Blender animation: 4 s bob, sway, nod and soft squash.
            float phase = t * Mathf.PI * 2f / 4f;
            bool talking = t < _talkUntil;
            _hop = Mathf.MoveTowards(_hop, 0f, Time.deltaTime * 2.5f);
            float bob = Mathf.Sin(phase) * height * 0.06f + Mathf.Sin(_hop * Mathf.PI) * height * 0.12f;
            _pivot.localPosition = new Vector3(0, bob, 0);
            float sway = Mathf.Sin(phase) * 2.3f, nod = Mathf.Sin(phase + Mathf.PI / 2) * 1.7f;
            float talkWobble = talking ? Mathf.Sin(t * 18f) * 2.5f : 0f;
            _anim.localRotation = Quaternion.Euler(nod + talkWobble, 0, sway);
            float squash = Mathf.Sin(phase) * 0.025f + (talking ? Mathf.Abs(Mathf.Sin(t * 9f)) * 0.03f : 0f);
            _anim.localScale = new Vector3(1f + squash * 0.6f, 1f - squash, 1f + squash * 0.6f);

            // Blink every few seconds: eyes squash to 10 % and back over 0.12 s.
            if (_eyes.Length > 0)
            {
                const float blink = 0.12f;
                float since = t - _nextBlink;
                float s = 1f;
                if (since >= 0 && since < blink) s = 1f - 0.9f * (1f - Mathf.Abs(since / (blink / 2) - 1f));
                else if (since >= blink) _nextBlink = t + Random.Range(2.5f, 5f);
                for (int i = 0; i < _eyes.Length; i++)
                    _eyes[i].localScale = new Vector3(_eyeScales[i].x, _eyeScales[i].y * s, _eyeScales[i].z);
            }
        }

        static Bounds Bounds(Transform root)
        {
            var rs = root.GetComponentsInChildren<Renderer>();
            var b = rs.Length > 0 ? rs[0].bounds : new Bounds(root.position, Vector3.one * 0.1f);
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t)
            {
                var f = FindDeep(c, name);
                if (f != null) return f;
            }
            return null;
        }
    }
}
