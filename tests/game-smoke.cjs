"use strict";

const assert = require("node:assert/strict");
const { readFileSync } = require("node:fs");
const { join } = require("node:path");
const vm = require("node:vm");
const source = readFileSync(join(__dirname, "../game.js"), "utf8").replace(/\r\n/g, "\n");

// Evaluate the real logic without starting the DOM, audio, or animation loop.
function section(start, next) {
  const from = source.indexOf(start);
  const to = source.indexOf(next, from + start.length);
  assert.ok(from >= 0 && to > from, `Cannot extract game.js between ${start.trim()} and ${next.trim()}`);
  return source.slice(from, to);
}

let rolls = [];
let consumed = 0;
const math = Object.create(Math);
math.random = () => {
  assert.ok(consumed < rolls.length, "Unexpected random call");
  return rolls[consumed++];
};
const context = vm.createContext({ W: 960, H: 540, Math: math });
vm.runInContext(
  section("  const ENEMY_DEFINITIONS = {", "\n  function loadSave()") +
  section("  function clamp(", "\n  function choose(") +
  section("  function dist(", "\n  function active(") +
  section("  function createEnemy(", "\n  const audio = {"), context);

function method(name, next) {
  return vm.runInContext(`({${section(`    ${name}(`, `\n    ${next}(`)}})`, context)[name];
}
const spawnWave = method("spawnWave", "startBoss");
const collisions = method("collisions", "destroyEnemy");

// Fixed draws cover the spawn thresholds, card count, and RNG consumption order.
for (const [draws, expected] of [
  [[.379, .25, .44, .5, .25, .75, .5], [["card", 1015, 165, 3.5, 1.25], ["card", 1079, 207, 5.25, 1.5]]],
  [[.379, .25, .45, .5, .25], [["card", 1015, 165, 3.5, 1.25]]],
  [[.38, .25, .5, .75], [["hare", 1020, 175, 3.5, 1.25]]],
  [[.72, .25, .5, .75], [["teapot", 1022, 172.5, 3.5, 1.225]]],
  [[.91, .25, .5, .75], [["cat", 1030, 185, 3.5, 1.2]]]
]) {
  rolls = draws;
  consumed = 0;
  const state = { stageTime: 0, enemies: [] };
  spawnWave.call(state);
  assert.equal(consumed, draws.length, "Random draws left unused");
  assert.equal(state.enemies.length, expected.length);
  state.enemies.forEach((enemy, index) => {
    const [type, x, y, phase, shot] = expected[index];
    assert.equal(enemy.type, type);
    assert.deepEqual([enemy.x, enemy.y, enemy.phase], [x, y, phase]);
    assert.ok(Math.abs(enemy.shot - shot) < 1e-12, `${type} shot timer changed`);
  });
}

// Hits exclude the exact contact boundary; grazing excludes its outer boundary.
for (const [distance, hit, graze] of [[19.999, true, false], [20, false, true], [39.999, false, true], [40, false, false]]) {
  const bullet = { x: distance, y: 0, r: 5, life: 4 };
  const events = [];
  const state = {
    hero: { x: 0, y: 0, r: 15 }, shots: [], enemies: [], pickups: [], enemyShots: [bullet],
    stats: { grazes: 13 },
    damageHero() { events.push(["hit", bullet.life]); },
    addScore(amount) { events.push(["score", amount]); },
    setStatus(text) { events.push(["status", text]); }
  };
  collisions.call(state);
  assert.equal(bullet.life, hit ? 0 : 4);
  assert.equal(state.stats.grazes, graze ? 14 : 13);
  assert.deepEqual(events, hit ? [["hit", 0]] : graze ? [["score", 30], ["status", "ギリギリのティータイム！ GRAZE +30"]] : []);
}

console.log("Game smoke checks passed (spawn selection/RNG and hit/graze boundaries).");
