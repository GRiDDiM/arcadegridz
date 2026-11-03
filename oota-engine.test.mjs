import test from "node:test";
import assert from "node:assert/strict";
import * as E from "../public/oota/engine.js";

const run = (g, n, inp = () => ({})) => { for (let i = 0; i < n && g.state === "play"; i++) E.step(g, inp(g, i)); return g; };
const playLevel = (lv, diff = 1, seed = 1, max = 30 * 60 * 8) => run(E.createGame({ difficulty: diff, level: lv, seed }), max, (g) => E.autoInput(g));

test("the campaign is ten levels: seven planets with bosses plus three bonus stages", () => {
  assert.equal(E.LEVELS.length, 10);
  assert.equal(E.LEVELS[0].id, "planetx");
  assert.equal(E.LEVELS.at(-1).id, "earth");
  assert.deepEqual(E.LEVELS.filter(l => l.kind === "bonus").map(l => l.bonus), ["nibiru", "pluto", "sun"]);
  assert.ok(E.LEVELS.at(-1).final);
});

test("a level starts with a formation and the hand of god keeps adding rows", () => {
  const g = E.createGame({ difficulty: 2, level: 0, seed: 1 });
  assert.ok(g.aliens.length >= 8);
  run(g, 90 * 12);
  assert.ok([...new Set(g.aliens.map(a => a.mey))].some(y => y > E.SLOT_Y), "a sweep pushes the formation down");
});

test("orders go to a chosen group of aliens and they keep following them", () => {
  const g = E.createGame({ difficulty: 1, level: 0, seed: 2 });
  run(g, 60);
  const ids = g.aliens.slice(0, 3).map(a => a.id);
  E.step(g, { command: { cmd: E.CMD.ATTACK, ids } });
  run(g, 30);
  for (const id of ids) { const a = g.aliens.find(a => a.id === id); if (a) { assert.equal(a.cmd, E.CMD.ATTACK); assert.equal(a.chosen, true); } }
  assert.ok(g.shots.length > 0 || g.score > 0);
});

test("bugs and tentacles stay locked until the swarm earns them", () => {
  const g = E.createGame({ difficulty: 1, level: 0, seed: 3 });
  run(g, 60);
  const ids = g.aliens.map(a => a.id);
  E.step(g, { command: { cmd: E.CMD.BUGS, ids } });
  assert.ok(g.aliens.every(a => a.cmd !== E.CMD.BUGS));
  g.me.bugs = 1; E.step(g, { command: { cmd: E.CMD.BUGS, ids } });
  assert.ok(g.aliens.some(a => a.cmd === E.CMD.BUGS));
});

test("snatch grabs the nearest loot in reach: green upgrades, red is blocked", () => {
  const g = E.createGame({ difficulty: 1, level: 0, seed: 5 });
  run(g, 90);
  const a = g.aliens[0];
  g.loots.push({ x: a.x, y: a.y + 30, good: false, vy: 0, id: 999 });
  E.step(g, { snatch: true });
  assert.equal(g.combo, 1);
  const before = Object.values(g.me).reduce((s, v) => s + v, 0);
  g.loots.push({ x: a.x, y: a.y + 20, good: true, vy: 0, id: 1000 });
  E.step(g, { snatch: true });
  assert.ok(Object.values(g.me).reduce((s, v) => s + v, 0) > before);
});

test("rush sends the swarm diving, then needs to recharge", () => {
  const g = E.createGame({ difficulty: 1, level: 0, seed: 6 });
  run(g, 90);
  E.step(g, { rush: true });
  assert.ok(g.aliens.some(a => a.dive === 2));
  assert.ok(g.rushCD > 0);
  E.step(g, { rush: true });
  assert.ok(g.events.some(e => e.type === "deny"));
});

test("the camera zooms out as the swarm grows, but never past the cap", () => {
  const g = E.createGame({ difficulty: 1, level: 0, seed: 7 });
  for (let i = 0; i < 50; i++) g.aliens.push({ ...g.aliens[0], id: 900 + i });
  run(g, 400);
  assert.ok(g.z < 0.9 && g.z >= E.ZMIN - 1e-9);
  assert.ok(E.fx1(g) - E.fx0(g) > 196);
});

test("the hero pilot dodges incoming shots instead of strafing at random", () => {
  let dodged = 0, trials = 0;
  for (let seed = 1; seed <= 12; seed++) {
    const g = E.createGame({ difficulty: 3, level: 9, seed });
    run(g, 120);
    const m = g.man; if (!m) continue;
    g.shots.length = 0; g.shots.push({ x: m.x, y: m.y - 50, vy: 4 }, { x: m.x - 3, y: m.y - 46, vy: 4 });
    m.inv = 0; m.shields = 0; const kills = g.kills; trials++;
    run(g, 16);
    if (g.kills === kills) dodged++;
  }
  assert.ok(trials >= 8 && dodged / trials >= 0.6, `ace dodged ${dodged}/${trials}`);
});

test("fleet levels field wingmen; a frigate dome absorbs shots but not dives", () => {
  const g = E.createGame({ difficulty: 2, level: E.LEVELS.findIndex(l => l.id === "venus"), seed: 8 });
  assert.deepEqual(g.ships.map(s => s.kind).sort(), ["fighter", "frigate", "interceptor"]);
  run(g, 120);
  const f = g.ships.find(s => s.kind === "frigate"); assert.ok(f);
  const hp = f.hp, dome = f.dome;
  g.shots.push({ x: f.x, y: f.y - 30, vy: 4 });
  run(g, 10);
  assert.equal(f.hp, hp); assert.ok(f.dome < dome);
});

test("the idle autopilot finishes most runs of every level on EASY", () => {
  for (let lv = 0; lv < E.LEVELS.length; lv++) {
    let wins = 0; for (let seed = 1; seed <= 4; seed++) { const g = playLevel(lv, 1, seed); if (g.state === "clear" || g.state === "won") wins++; }
    assert.ok(wins >= 3, `${E.LEVELS[lv].id}: ${wins}/4`);
  }
});

test("bonus stages end on their timer (or a perfect Pluto) and tally a bonus", () => {
  for (const id of ["sun", "pluto", "nibiru"]) {
    const lv = E.LEVELS.findIndex(l => l.id === id), g = playLevel(lv, 2, 3);
    assert.equal(g.state, "clear"); assert.ok(g.tally && g.tally.bonus > 0, id);
  }
});

test("game over when every alien is gone; the final boss wins the invasion", () => {
  const g = E.createGame({ difficulty: 1, level: 0, seed: 9 });
  run(g, 70); g.aliens.length = 0; E.step(g, {});
  assert.equal(g.state, "over");
  const w = playLevel(9, 1, 2);
  assert.equal(w.state, "won");
});

test("defense mode: the player flies the ship, an alien commander runs the swarm", () => {
  const g = E.createGame({ difficulty: 1, level: 0, seed: 3, mode: "defense" });
  const x0 = g.man.x;
  run(g, 900, (g, i) => ({ fire: true, move: i < 40 ? 1 : 0 }));
  assert.ok(g.aliens.some(a => a.chosen), "the commander gives orders");
  assert.ok(g.score > 0 && g.deaths > 0, "the player's shots kill aliens");
  assert.ok(g.quota > 0 && g.spawned <= g.quota);
});
