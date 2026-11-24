// Tokens from the Figma "Mobile" page (same values as Mobile/src/styles.css).
export const color = {
  bg: '#f5f8f2',
  surface: '#ffffff',
  primary: '#5b8760',
  primaryDark: '#3f6b45',
  primaryBg: '#e2eedd',
  text: '#1f2a22',
  muted: '#7b877d',
  border: '#e6ece1',
  seg: '#eaf0e5',
  placeholder: '#c9d8c5',
  red: '#c2573f',
  redBg: '#fbe5dd',
  green: '#4c7a51',
  greenBg: '#e1efdc',
  amber: '#a9781f',
  amberBg: '#faf0cf',
  lav: '#6c6fa3',
  lavBg: '#e9e9f6',
  avatar: '#8fae8b',
} as const;

export const font = {
  regular: 'Outfit_400Regular',
  medium: 'Outfit_500Medium',
  semibold: 'Outfit_600SemiBold',
  bold: 'Outfit_700Bold',
} as const;

export const shadow = {
  shadowColor: '#3d6142',
  shadowOpacity: 0.09,
  shadowRadius: 18,
  shadowOffset: { width: 0, height: 6 },
  elevation: 3,
} as const;

export const shadowUp = {
  shadowColor: '#3d6142',
  shadowOpacity: 0.08,
  shadowRadius: 16,
  shadowOffset: { width: 0, height: -4 },
  elevation: 6,
} as const;

export type Tone = 'primary' | 'neutral' | 'red' | 'amber' | 'green' | 'lav';

export const tone: Record<Tone, { bg: string; fg: string }> = {
  primary: { bg: color.primaryBg, fg: color.primaryDark },
  neutral: { bg: color.seg, fg: color.muted },
  red: { bg: color.redBg, fg: color.red },
  amber: { bg: color.amberBg, fg: color.amber },
  green: { bg: color.greenBg, fg: color.green },
  lav: { bg: color.lavBg, fg: color.lav },
};
