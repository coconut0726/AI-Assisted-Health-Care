# AI-Assisted Home Care — demo video

~1:40, 1920×1080, built with [Remotion](https://www.remotion.dev/) (React → MP4).

- Mobile shots are the real app screens from `../Mobile/src`, mounted in a phone frame with a
  fixed app state per shot (`src/states.ts`); the tapped element gets a sage ring.
- VR shots are clips cut from the headset recordings (`public/vr/`).
- Voiceover: ElevenLabs "Lauren B – Warm Feedback Care Agent" (`public/voice/`), one clip per
  scene; subtitles are timed from the clip lengths in `src/timing.ts`.
- James Carter is a fictional case.

```bash
npm install
npm run studio   # preview and scrub in the browser
npm run render   # writes out/ahc-demo.mp4
```

To change the narration: edit the text in `src/timing.ts`, regenerate that scene's clip into
`public/voice/<scene-id>.mp3`, and update its length in `VOICE_SECONDS`.
