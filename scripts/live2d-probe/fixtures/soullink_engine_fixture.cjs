'use strict';

function createManualClock() {
  let current = 0;
  return { set(value) { current = Number(value) / 1000; }, now() { return current; } };
}

function createCase({ testCase, clock }) {
  return {
    step(timeSeconds) {
      const current = clock?.now ? clock.now() : timeSeconds;
      const intensity = Number(testCase.intensity);
      const phase = Math.min(1, current / 2);
      const wave = Math.sin(phase * Math.PI);
      const pitch = current <= 1.1 ? -4 * intensity * wave : undefined;
      const values = { ParamAngleX: 8 * intensity * wave };
      if (pitch !== undefined) values.ParamAngleY = pitch;
      return { live2dParams: values };
    }
  };
}

module.exports = { packageName: 'soullink-fixture-engine', version: 'fixture-1', createManualClock, createCase };
