import { defineConfig } from '@rsbuild/core';
import { pluginBabel } from '@rsbuild/plugin-babel';
import { pluginSolid } from '@rsbuild/plugin-solid';
import { pluginTypeCheck } from '@rsbuild/plugin-type-check';

const prod = process.env.NODE_ENV === 'production';
export default defineConfig({
  plugins: [
    pluginTypeCheck(),
    pluginBabel({
      include: /\.(?:jsx|tsx)$/,
    }),
    pluginSolid(),
  ],
  dev: {
    hmr: false,
    liveReload: false,
  },
  output: {
    inlineScripts: prod,
    inlineStyles: prod,
    sourceMap: {
      js: !prod && 'cheap-source-map',
    },
  },
  html: {
    title: '最终幻想14投影台整理助手',
    scriptLoading: 'module',
  },
  server: {
    htmlFallback: false,
    proxy: {
      '/data': 'http://localhost:8014',
      '/icon': 'http://localhost:8014',
    },
  },
  tools: {
    bundlerChain: chain => chain
      .output
        .publicPath('auto')
        .end()
      // .optimization.concatenateModules(false).end()  // for bundle analyze
      .experiments({
        rspackFuture: {
          bundlerInfo: {
            force: false,
          },
        },
      }).end(),
  },
  performance: {
    // bundleAnalyze: {},
    chunkSplit: {
      strategy: 'all-in-one',
    },
  },
});
