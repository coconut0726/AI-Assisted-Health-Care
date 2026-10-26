using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AHC.VR
{
    /// <summary>
    /// The AI as a character: the ghost from Blender appears where the user is first looking
    /// (a little to the right of straight ahead, near eye height) and then stays at that spot in
    /// the room — it does not follow the user's gaze. It bobs and blinks like its Blender loop,
    /// turns to face the user, and speaks each instruction in a speech bubble.
    /// <see cref="Recenter"/> brings it back in front of the user.
    /// </summary>
    public class GhostCompanion : MonoBehaviour
    {
        const float BubbleScale = 0.0006f;

        [Tooltip("Height of the ghost in metres.")]
        public float height = 0.24f;
        [Tooltip("Where it appears relative to the user's first view, in metres: right, up (from the eyes), forward.")]
        public Vector3 spawnOffset = new(0.42f, -0.08f, 0.9f);
        [Tooltip("Wait this long for head tracking to settle before placing the ghost.")]
        public float settleTime = 0.5f;

        Transform _head;
        Transform _pivot, _anim, _model;
        Transform[] _eyes;
        Vector3[] _eyeScales;
        float _nextBlink, _talkUntil, _hop, _spawnTime;
        bool _placed;
        TextMeshProUGUI _bubbleText;
        GameObject _bubble;

        public static GhostCompanion Spawn(GameObject modelPrefab, Transform parent, Transform head)
        {
            var root = new GameObject("AI Ghost").AddComponent<GhostCompanion>();
            root.transform.SetParent(parent, false);
            root._head = head;
            root._spawnTime = Time.time;
            root.Build(modelPrefab);
            root._pivot.gameObject.SetActive(false); // hidden until placed in view
            root._bubble.SetActive(false);
            return root;
        }

        /// <summary>Place the ghost in front of where the user is looking now.</summary>
        public void Recenter()
        {
            _placed = false;
            _spawnTime = Time.time - settleTime; // place on the next frame
        }

        bool TryPlace()
        {
            // On the headset the first frames can report an untracked head (an all-zero local pose);
            // wait until tracking has had a moment and the head has a real pose.
            float waited = Time.time - _spawnTime;
            if (waited < settleTime) return false;
            bool untracked = _head.localPosition == Vector3.zero && _head.localRotation == Quaternion.identity;
            if (untracked && waited < settleTime + 1.5f) return false; // no headset (Editor): place anyway after 2 s
            Vector3 fwd = Vector3.ProjectOnPlane(_head.forward, Vector3.up);
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.ProjectOnPlane(_head.up, Vector3.up);
            fwd.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            transform.position = _head.position + right * spawnOffset.x + Vector3.up * spawnOffset.y + fwd * spawnOffset.z;
            _pivot.gameObject.SetActive(true);
            _bubble.SetActive(!string.IsNullOrEmpty(_bubbleText.text));
            _hop = 1f; // little "hello" hop as it appears
            return true;
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
            _bubble.SetActive(_placed && !string.IsNullOrEmpty(text)); // shown on placement otherwise
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

            // Appear once in front of the user's first view, then stay put; only turn to face them.
            if (!_placed) { _placed = TryPlace(); if (!_placed) return; }
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
