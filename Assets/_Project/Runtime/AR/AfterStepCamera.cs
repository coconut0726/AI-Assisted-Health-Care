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

        public async Task<byte[]> CaptureJpegAsync(CancellationToken cancellation)
        {
            IsEditorTest = false;
#if UNITY_EDITOR
            if (editorTestImage != null)
            {
                IsEditorTest = true;
                return Encode(editorTestImage);
            }
            if (captureViewCameraInEditor)
            {
                IsEditorTest = true;
                return RenderView();
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
                    if (texture != null && texture.width > 16 && texture.height > 16) return Encode(texture);
                }
                await Task.Yield();
            }
            throw new TimeoutException("No fresh headset camera frame arrived within 15 seconds.");
        }

#if UNITY_EDITOR
        static byte[] RenderView()
        {
            var rig = FindAnyObjectByType<OVRCameraRig>();
            var camera = rig != null && rig.centerEyeAnchor != null ? rig.centerEyeAnchor.GetComponent<Camera>() : Camera.main;
            if (camera == null) throw new InvalidOperationException("No camera to capture in the Editor.");
            var target = RenderTexture.GetTemporary(1280, 960, 24, RenderTextureFormat.ARGB32);
            var previousTarget = camera.targetTexture;
            var previousColor = camera.backgroundColor;
            try
            {
                camera.targetTexture = target;
                camera.backgroundColor = new Color(0.55f, 0.55f, 0.55f, 1); // passthrough stand-in
                camera.Render();
                return Encode(target);
            }
            finally
            {
                camera.targetTexture = previousTarget;
                camera.backgroundColor = previousColor;
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
}
