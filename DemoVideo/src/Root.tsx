import { Composition } from 'remotion';
import { Demo } from './Demo';
import { FPS, totalFrames } from './timing';

export function RemotionRoot() {
  return <Composition id="Demo" component={Demo} durationInFrames={totalFrames()} fps={FPS} width={1920} height={1080} />;
}
