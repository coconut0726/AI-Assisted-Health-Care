using System;
using System.Text.RegularExpressions;
using Meta.WitAi.TTS.Utilities;
using Oculus.Voice.Dictation;
using Oculus.VoiceSDK.Utilities;
using UnityEngine;

namespace AHC.VR
{
    /// <summary>
    /// The care guide's voice, through Meta Voice SDK (Wit.ai, free): <see cref="Speak"/> reads a line aloud
    /// (TTS) and <see cref="ToggleListening"/> turns one spoken sentence into text (dictation), raised as
    /// <see cref="Heard"/>. Set up with AHC ▸ Set Up Voice, after adding your Wit.ai app in Meta ▸ Voice SDK.
    /// Without a Wit configuration it stays silent and the demo works as before.
    /// </summary>
    public sealed class VoiceGuide : MonoBehaviour
    {
        public TTSSpeaker speaker;
        public AppDictationExperience dictation;
        [Tooltip("Read the guide's lines aloud.")]
        public bool speakLines = true;

        /// <summary>A full sentence the user said.</summary>
        public event Action<string> Heard;
        /// <summary>Listening started or stopped, or the partial text changed.</summary>
        public event Action Changed;

        public bool CanSpeak => speaker != null && speaker.TTSService != null;
        public bool CanListen => dictation != null && dictation.Configuration != null;
        public bool Listening { get; private set; }
        /// <summary>What the user is saying right now (partial transcription) while listening.</summary>
        public string Partial { get; private set; } = "";

        void Start()
        {
            // Voice SDK does not log in device builds; report each step ourselves (adb logcat -s Unity | grep "[AHC] Voice").
            Debug.Log($"[AHC] Voice: speak={CanSpeak} listen={CanListen} mic={MicPermissionsManager.HasMicPermission()}");
            if (speaker != null)
            {
                var s = speaker.Events;
                s.OnLoadBegin.AddListener((_, clip) => Debug.Log("[AHC] Voice: loading speech"));
                s.OnLoadFailed.AddListener((_, clip, error) => Debug.LogWarning("[AHC] Voice: speech failed to load: " + error));
                s.OnLoadSuccess.AddListener((_, clip) => Debug.Log("[AHC] Voice: speech loaded"));
                s.OnPlaybackStart.AddListener((_, clip) => Debug.Log("[AHC] Voice: speaking"));
                s.OnPlaybackComplete.AddListener((_, clip) => Debug.Log("[AHC] Voice: finished speaking"));
                s.OnPlaybackCancelled.AddListener((_, clip, reason) => Debug.Log("[AHC] Voice: speech cancelled: " + reason));
            }
            if (dictation == null) return;
            var e = dictation.DictationEvents;
            e.OnStartListening.AddListener(() => { Listening = true; Partial = ""; Debug.Log("[AHC] Voice: listening"); Changed?.Invoke(); });
            e.OnStoppedListening.AddListener(() => { Listening = false; Debug.Log("[AHC] Voice: stopped listening"); Changed?.Invoke(); });
            e.OnPartialTranscription.AddListener(text => { Partial = text ?? ""; Changed?.Invoke(); });
            e.OnFullTranscription.AddListener(text =>
            {
                Partial = "";
                Debug.Log("[AHC] Voice: heard \"" + text + "\"");
                if (!string.IsNullOrWhiteSpace(text)) Heard?.Invoke(text.Trim());
                Changed?.Invoke();
            });
            e.OnError.AddListener((error, message) => Debug.LogWarning($"[AHC] Voice input error: {error} {message}", this));
        }

        /// <summary>Reads <paramref name="text"/> aloud, cutting off anything still being said.</summary>
        public void Speak(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            if (!speakLines || !CanSpeak) { Debug.Log("[AHC] Voice: not speaking (speakLines=" + speakLines + ", speak=" + CanSpeak + ")"); return; }
            speaker.Stop();
            // Wit TTS reads symbols literally; keep the words.
            speaker.Speak(Regex.Replace(text, @"[•·…]", ", "));
        }

        public void StopSpeaking() { if (speaker != null) speaker.Stop(); }

        /// <summary>Starts listening for one sentence, or stops early. Asks for the microphone the first time.</summary>
        public void ToggleListening()
        {
            if (!CanListen) return;
            if (Listening) { dictation.Deactivate(); return; }
            if (!MicPermissionsManager.HasMicPermission())
            {
                MicPermissionsManager.RequestMicPermission(_ => { if (this != null) StartListening(); });
                return;
            }
            StartListening();
        }

        void StartListening()
        {
            StopSpeaking(); // don't transcribe the guide's own voice
            dictation.Activate();
        }
    }
}
