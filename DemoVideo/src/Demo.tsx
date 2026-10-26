import { loadFont } from '@remotion/google-fonts/Outfit';
import { AbsoluteFill, Audio, Sequence, Series, staticFile } from 'remotion';
import { Captions } from './components/Frame';
import { CaseScene, EditScene, GuideScene, IntroScene, OutroScene, RecordScene, ReportScene } from './scenes';
import { FPS, SCENES, sceneSeconds, VOICE_LEAD_IN, VOICE_SECONDS } from './timing';

loadFont('normal', { weights: ['400', '500', '600', '700'] });

const VIEWS: Record<string, () => JSX.Element> = {
  's1-intro': IntroScene,
  's2-case': CaseScene,
  's3-record': RecordScene,
  's4-edit': EditScene,
  's5-guide': GuideScene,
  's6-report': ReportScene,
  's7-outro': OutroScene,
};

export function Demo() {
  return (
    <AbsoluteFill style={{ background: '#F5F8F2' }}>
      <Series>
        {SCENES.map((s) => {
          const View = VIEWS[s.id];
          const voice = VOICE_SECONDS[s.id];
          const frames = Math.round(sceneSeconds(s) * FPS);
          return (
            <Series.Sequence key={s.id} durationInFrames={frames} name={s.id}>
              <View />
              {voice && (
                <Sequence from={Math.round(VOICE_LEAD_IN * FPS)} layout="none">
                  <Audio src={staticFile(`voice/${s.id}.mp3`)} />
                </Sequence>
              )}
              <Captions text={s.narration} voiceSeconds={voice ?? sceneSeconds(s) - VOICE_LEAD_IN - 0.9} />
            </Series.Sequence>
          );
        })}
      </Series>
    </AbsoluteFill>
  );
}
