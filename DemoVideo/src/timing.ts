// Narration script and scene lengths. When a voice clip exists in public/voice/<id>.mp3 its
// measured length (VOICE_SECONDS) sets the scene length; otherwise the estimate is used.

export const FPS = 30;

export interface SceneSpec {
  id: string;
  narration: string;
  /** Fallback length when there is no voice clip yet. */
  estimateSec: number;
}

export const SCENES: SceneSpec[] = [
  {
    id: 's1-intro',
    narration: 'AI-Assisted Home Care. Helping patients get their care right at home, after they leave the hospital.',
    estimateSec: 7,
  },
  {
    id: 's2-case',
    narration:
      "Meet James Carter, a fictional patient we'll follow in this demo. James was treated for a burn on his forearm, and now he has to change the dressing at home, once a day. A nurse shows him how before he leaves — but at home, he'll be on his own.",
    estimateSec: 19,
  },
  {
    id: 's3-record',
    narration:
      'Before James goes home, Nurse Lee puts on the headset and simply performs the procedure, explaining each step out loud. The headset captures what the nurse sees and says, and the AI turns it into step-by-step instructions.',
    estimateSec: 16,
  },
  {
    id: 's4-edit',
    narration:
      "On the phone, the nurse reviews the AI's draft: the care steps, and an oversight checklist of what must be done right. Here the AI dropped a condition in step two, so the nurse corrects it. As soon as it's saved, James's app shows the update.",
    estimateSec: 19,
  },
  {
    id: 's5-guide',
    narration:
      "At home, James puts on the headset. His care guide checks that the supplies are in view, then walks him through every step — with voice instructions, labels on the real objects, and gentle reminders like 'pat, don't rub'. He moves on whenever he's ready.",
    estimateSec: 19,
  },
  {
    id: 's6-report',
    narration:
      "Every session is checked against the nurse's checklist. Each week, the nurse gets a report: what went well, what needs attention, and where the AI couldn't tell. The nurse can forward it to James and add a note in their own words.",
    estimateSec: 18,
  },
  {
    id: 's7-outro',
    narration: "AI-Assisted Home Care. The nurse's guidance, at home, every time.",
    estimateSec: 7,
  },
];

/** Measured voice clip lengths in seconds (filled in after the voiceover is generated). */
export const VOICE_SECONDS: Record<string, number> = {
  's1-intro': 6.84,
  's2-case': 16.12,
  's3-record': 14.84,
  's4-edit': 16.43,
  's5-guide': 17.16,
  's6-report': 14.45,
  's7-outro': 5.56,
};

const LEAD_IN = 0.4; // silence before the voice starts in each scene
const TAIL = 0.9; // breathing room after it ends

export function sceneSeconds(s: SceneSpec): number {
  const v = VOICE_SECONDS[s.id];
  return v ? Math.max(v + LEAD_IN + TAIL, 5) : s.estimateSec;
}

export const VOICE_LEAD_IN = LEAD_IN;

export function totalFrames(): number {
  return SCENES.reduce((n, s) => n + Math.round(sceneSeconds(s) * FPS), 0);
}
