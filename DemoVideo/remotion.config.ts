import path from 'path';
import { Config } from '@remotion/cli/config';

Config.setVideoImageFormat('jpeg');
Config.setJpegQuality(92);

// The mobile screens are the real app components from ../Mobile/src. Point every import of
// React / React Router at this project's copies so app and video share one React instance.
Config.overrideWebpackConfig((config) => ({
  ...config,
  resolve: {
    ...config.resolve,
    alias: {
      ...(config.resolve?.alias ?? {}),
      react: path.resolve(process.cwd(), 'node_modules/react'),
      'react-dom': path.resolve(process.cwd(), 'node_modules/react-dom'),
      'react-router-dom': path.resolve(process.cwd(), 'node_modules/react-router-dom'),
    },
  },
}));
