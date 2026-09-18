/* Creates the self-authored, isolated review curve for screen_side_arm_raise.
 * Usage: node create_screen_side_arm_raise_fixture.cjs <absolute-output.json>
 * It never writes a production data root, asset directory, or parameter map.
 */
'use strict';
const fs = require('fs');
const path = require('path');
const output = process.argv[2];
if (!output || !path.isAbsolute(output)) throw new Error('Pass an absolute output path.');
const candidate = {
  candidateId: 'screen_side_arm_raise',
  durationSeconds: 2.4,
  curves: [{
    parameterId: 'Param94',
    // Cubism-style segments: baseline -> eased lift -> short hold -> eased return.
    segments: [0, 0, 1, 0.24, 0, 0.64, 15, 0.97, 15, 2, 1.2, 15, 1, 1.52, 15, 2.16, 0, 2.4, 0]
  }]
};
fs.mkdirSync(path.dirname(output), { recursive: true });
fs.writeFileSync(output, JSON.stringify(candidate, null, 2) + '\n', 'utf8');
console.log(output);
