using System;
using System.Threading;
using System.Threading.Tasks;
using Meta.XR;
using UnityEngine;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

namespace HomeCare.AR
{
    public sealed class AfterStepCamera : MonoBehaviour
    {
        [Tooltip("Optional explicit test input for Editor Play Mode. Never used in a headset build.")]
        public Texture2D editorTestImage;
        [Tooltip("Editor only: without a test image, capture what the headset camera renders (the virtual scene). Turn off to use the passthrough camera over Link.")]
        public bool captureViewCameraInEditor = true;
        public bool IsEditorTest { get; private set; }
        PassthroughCameraAccess _access;
        const string CameraPermission = "horizonos.permission.HEADSET_CAMERA";

        public async Task<byte[]> CaptureJpegAsync(CancellationToken cancellation) =>
            (await CaptureFrameAsync(cancellation)).Jpeg;

        /// <summary>
        /// Captures a JPEG together with the camera pose and projection at that moment, so a 2D box
        /// found in the image later can be turned into rays in the room.
        /// </summary>
        public async Task<CapturedFrame> CaptureFrameAsync(CancellationToken cancellation)
        {
            IsEditorTest = false;
#if UNITY_EDITOR
            if (editorTestImage != null)
            {
                IsEditorTest = true;
                return EditorFrame(Encode(editorTestImage), (float)editorTestImage.width / editorTestImage.height,
                    "editor_test_image", false);
            }
            if (captureViewCameraInEditor)
            {
                IsEditorTest = true;
                return EditorFrame(RenderView(), 1280f / 960f, "editor_view", true);
            }
#endif
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(CameraPermission))
            {
                bool finished = false;
                var callbacks = new PermissionCallbacks();
                callbacks.PermissionGranted += _ => finished = true;
                callbacks.PermissionDenied += _ => finished = true;
                UnityEngine.Android.Permission.RequestUserPermission(CameraPermission, callbacks);
                float permissionDeadline = Time.realtimeSinceStartup + 60;
                while (!finished && Time.realtimeSinceStartup < permissionDeadline)
                {
                    cancellation.ThrowIfCancellationRequested();
                    await Task.Yield();
                }
                if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(CameraPermission))
                    throw new InvalidOperationException("Headset camera permission was not granted.");
            }
#endif
            if (!PassthroughCameraAccess.IsSupported)
                throw new InvalidOperationException("Passthrough camera is unavailable. Use a supported Quest headset, or assign an explicit Editor test image.");
            if (_access == null)
            {
                var cameraObject = new GameObject("Home Care Camera Access");
                cameraObject.transform.SetParent(transform, false);
                cameraObject.SetActive(false);
                _access = cameraObject.AddComponent<PassthroughCameraAccess>();
                _access.CameraPosition = PassthroughCameraAccess.CameraPositionType.Left;
                _access.RequestedResolution = new Vector2Int(1280, 960);
                _access.MaxFramerate = 30;
                cameraObject.SetActive(true);
            }
            DateTime previousTimestamp = _access.Timestamp;
            float deadline = Time.realtimeSinceStartup + 15;
            while (Time.realtimeSinceStartup < deadline)
            {
                cancellation.ThrowIfCancellationRequested();
                if (_access.IsPlaying && _access.Timestamp != default && _access.Timestamp != previousTimestamp)
                {
                    var texture = _access.GetTexture();
                    if (texture != null && texture.width > 16 && texture.height > 16)
                    {
                        var access = _access;
                        var pose = access.GetCameraPose();
                        return new CapturedFrame
                        {
                            Jpeg = Encode(texture),
                            CameraPose = pose,
                            ViewportToRay = viewport => access.ViewportPointToRay(viewport, pose),
                            Source = "passthrough",
                            MatchesScene = true,
                        };
                    }
                }
                await Task.Yield();
            }
            throw new TimeoutException("No fresh headset camera frame arrived within 15 seconds.");
        }

#if UNITY_EDITOR
        static Camera EditorCamera()
        {
            var rig = FindAnyObjectByType<OVRCameraRig>();
            return rig != null && rig.centerEyeAnchor != null ? rig.centerEyeAnchor.GetComponent<Camera>() : Camera.main;
        }

        /// <summary>Editor frame: rays come from the head camera with the capture's aspect ratio.</summary>
        static CapturedFrame EditorFrame(byte[] jpeg, float aspect, string source, bool matchesScene)
        {
            var camera = EditorCamera();
            var pose = camera != null ? new Pose(camera.transform.position, camera.transform.rotation) : new Pose(Vector3.zero, Quaternion.identity);
            float tanHalf = Mathf.Tan(0.5f * Mathf.Deg2Rad * (camera != null ? camera.fieldOfView : 60f));
            return new CapturedFrame
            {
                Jpeg = jpeg,
                CameraPose = pose,
                ViewportToRay = v => new Ray(pose.position,
                    pose.rotation * new Vector3((2 * v.x - 1) * tanHalf * aspect, (2 * v.y - 1) * tanHalf, 1).normalized),
                Source = source,
                MatchesScene = matchesScene,
            };
        }

        static byte[] RenderView()
        {
            var camera = EditorCamera();
            if (camera == null) throw new InvalidOperationException("No camera to capture in the Editor.");
            var target = RenderTexture.GetTemporary(1280, 960, 24, RenderTextureFormat.ARGB32);
            var previousTarget = camera.targetTexture;
            var previousColor = camera.backgroundColor;
            var previousMask = camera.cullingMask;
            try
            {
                camera.targetTexture = target;
                camera.backgroundColor = new Color(0.55f, 0.55f, 0.55f, 1); // passthrough stand-in
                // The headset camera never sees our panels, so leave the UI layer out here too.
                int ui = LayerMask.NameToLayer("UI");
                if (ui >= 0) camera.cullingMask &= ~(1 << ui);
                camera.Render();
                return Encode(target);
            }
            finally
            {
                camera.targetTexture = previousTarget;
                camera.backgroundColor = previousColor;
                camera.cullingMask = previousMask;
                RenderTexture.ReleaseTemporary(target);
            }
        }
#endif

        static byte[] Encode(Texture source)
        {
            int width = Mathf.Min(source.width, 1280);
            int height = Mathf.Max(1, Mathf.RoundToInt((float)source.height * width / source.width));
            var temporary = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            Texture2D readable = null;
            try
            {
                Graphics.Blit(source, temporary);
                RenderTexture.active = temporary;
                readable = new Texture2D(width, height, TextureFormat.RGB24, false);
                readable.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                readable.Apply();
                return readable.EncodeToJPG(85);
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(temporary);
                if (readable != null) Destroy(readable);
            }
        }
    }

    /// <summary>A camera image plus what is needed to map image points back into the room.</summary>
    public sealed class CapturedFrame
    {
        public byte[] Jpeg;
        /// <summary>World pose of the camera when the image was taken.</summary>
        public Pose CameraPose;
        /// <summary>Viewport point (0,0 bottom-left to 1,1 top-right of the image) to a world ray, using the capture-time pose.</summary>
        public Func<Vector2, Ray> ViewportToRay;
        /// <summary>passthrough | editor_view | editor_test_image</summary>
        public string Source;
        /// <summary>False when the image does not show the current scene (an Editor test photo), so depth cannot be measured.</summary>
        public bool MatchesScene;
    }
}
