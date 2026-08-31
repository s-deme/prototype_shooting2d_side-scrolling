(() => {
  "use strict";

  const canvas = document.querySelector("#gameCanvas");
  const ctx = canvas.getContext("2d");
  const W = canvas.width;
  const H = canvas.height;
  const $ = (selector) => document.querySelector(selector);
  const $$ = (selector) => [...document.querySelectorAll(selector)];
  const UI = {
    score: $("#scoreValue"), best: $("#bestValue"), stage: $("#stageValue"), lives: $("#livesValue"),
    title: $("#titlePanel"), pause: $("#pausePanel"), result: $("#resultPanel"), help: $("#helpPanel"), settings: $("#settingsPanel"),
    status: $("#statusText"), powerTip: $("#powerTip"), toast: $("#toast"), boss: $("#bossPanel"), bossBar: $("#bossBar"), bossPercent: $("#bossPercent"),
    reportScore: $("#reportScore"), reportKills: $("#reportKills"), reportCapsules: $("#reportCapsules"), resultHeading: $("#result-heading"), resultEyebrow: $("#resultEyebrow"), resultCopy: $("#resultCopy"),
    achievements: $("#achievementList"), cells: $$(".power-cell"),
    sound: $("#soundSetting"), shake: $("#shakeSetting"), motion: $("#motionSetting"), contrast: $("#contrastSetting")
  };

  const STORAGE_KEY = "alice-clockwork-sky-v1";
  const defaultSave = {
    best: 0,
    achievements: [],
    settings: { sound: true, shake: true, motion: false, contrast: false }
  };
  let save = loadSave();
  let selectedMode = "story";
  let toastTimer = 0;
  const keys = new Set();
  const touchKeys = new Set();
  const palette = {
    night: "#11112a", deep: "#0a0920", lavender: "#8474b6", mist: "#cbc2e5", gold: "#f7ca6f",
    pink: "#f296b8", red: "#dd567b", mint: "#8ce8c4", blue: "#8cc7ff", ink: "#251839", cream: "#fff4d8"
  };
  const POWER_DEFINITIONS = [
    { key: "speed", label: "SPEED", cap: 3 },
    { key: "missile", label: "MISSILE", cap: 2 },
    { key: "double", label: "DOUBLE", cap: 1 },
    { key: "laser", label: "LASER", cap: 1 },
    { key: "option", label: "OPTION", cap: 3 },
    { key: "shield", label: "SHIELD", cap: 1 }
  ];
  const MODE_LIVES = { story: 3, garden: 5 };
  const ENEMY_DEFINITIONS = {
    card: { radius: 15, health: 2.4, speed: 135, score: 90, healthGrowth: .45, speedGrowth: 14, spawnOffset: 55, yRange: [60, H - 60], shotRange: [1, 2] },
    hare: { radius: 19, health: 4.4, speed: 105, score: 175, healthGrowth: 1, speedGrowth: 9, spawnOffset: 60, yRange: [80, H - 80], shotRange: [.65, 1.45], usesBaseY: true },
    teapot: { radius: 25, health: 8, speed: 62, score: 300, healthGrowth: 1.4, speedGrowth: 0, spawnOffset: 62, yRange: [75, H - 75], shotRange: [.7, 1.4] },
    cat: { radius: 30, health: 13, speed: 74, score: 520, healthGrowth: 2, speedGrowth: 0, spawnOffset: 70, yRange: [100, H - 100], shotRange: [1.2, 1.2], usesBaseY: true },
    queen: { radius: 68, health: 290, speed: 88, score: 8000, spawnOffset: 150, boss: true, shotDelay: 1.5 }
  };

  function loadSave() {
    try {
      const stored = JSON.parse(localStorage.getItem(STORAGE_KEY));
      return {
        ...defaultSave,
        ...stored,
        settings: { ...defaultSave.settings, ...(stored?.settings || {}) },
        achievements: Array.isArray(stored?.achievements) ? stored.achievements : []
      };
    } catch { return structuredClone(defaultSave); }
  }
  function persist() { try { localStorage.setItem(STORAGE_KEY, JSON.stringify(save)); } catch { /* local saves are optional */ } }
  function fmt(value) { return Math.max(0, Math.floor(value)).toString().padStart(6, "0"); }
  function clamp(value, min, max) { return Math.max(min, Math.min(max, value)); }
  function random(min, max) { return min + Math.random() * (max - min); }
  function choose(items) { return items[Math.floor(Math.random() * items.length)]; }
  function dist(ax, ay, bx, by) { return Math.hypot(ax - bx, ay - by); }
  function active(code) { return keys.has(code) || touchKeys.has(code); }
  function createUpgrades() { return Object.fromEntries(POWER_DEFINITIONS.map(({ key }) => [key, 0])); }
  function createEnemy(type, x, y, difficulty = 0) {
    const definition = ENEMY_DEFINITIONS[type];
    const enemy = {
      type,
      x,
      y,
      r: definition.radius,
      hp: definition.health + difficulty * (definition.healthGrowth || 0),
      maxHp: definition.health,
      vx: definition.speed + difficulty * (definition.speedGrowth || 0),
      vy: 0,
      phase: definition.boss ? 0 : random(0, 7),
      value: definition.score,
      shot: definition.shotDelay ?? random(...definition.shotRange)
    };
    if (definition.usesBaseY) enemy.baseY = y;
    if (definition.boss) Object.assign(enemy, { boss: true, pattern: 0, entered: false });
    return enemy;
  }

  const audio = {
    context: null,
    wake() {
      if (!save.settings.sound) return;
      try {
        this.context ??= new (window.AudioContext || window.webkitAudioContext)();
        if (this.context.state === "suspended") this.context.resume();
      } catch { /* sound remains an enhancement */ }
    },
    beep(kind) {
      if (!save.settings.sound || !this.context) return;
      const now = this.context.currentTime;
      const presets = {
        shot: [410, .035, "square", .025], hit: [110, .075, "sawtooth", .035], collect: [720, .08, "sine", .045],
        power: [520, .16, "triangle", .06], boom: [70, .19, "sawtooth", .06], warning: [190, .12, "square", .045], win: [880, .23, "sine", .07]
      };
      const [frequency, duration, type, volume] = presets[kind] || presets.shot;
      const oscillator = this.context.createOscillator();
      const gain = this.context.createGain();
      oscillator.type = type; oscillator.frequency.setValueAtTime(frequency, now);
      if (kind === "collect" || kind === "win") oscillator.frequency.exponentialRampToValueAtTime(frequency * 1.48, now + duration);
      if (kind === "boom") oscillator.frequency.exponentialRampToValueAtTime(28, now + duration);
      gain.gain.setValueAtTime(volume, now); gain.gain.exponentialRampToValueAtTime(.0001, now + duration);
      oscillator.connect(gain); gain.connect(this.context.destination); oscillator.start(now); oscillator.stop(now + duration);
    }
  };

  const game = {
    state: "title",
    mode: "story",
    elapsed: 0,
    stageTime: 0,
    world: 0,
    spawnTimer: .7,
    ambientTimer: 0,
    statusTimer: 0,
    score: 0,
    shake: 0,
    flash: 0,
    bossStarted: false,
    clearDelay: 0,
    enemies: [], shots: [], enemyShots: [], pickups: [], particles: [], ribbons: [],
    stars: [],
    hero: null,
    stats: { kills: 0, capsules: 0, shots: 0, grazes: 0 },
    powerCursor: -1,
    upgrades: createUpgrades(),
    init() {
      this.stars = Array.from({ length: 90 }, () => ({ x: random(0, W), y: random(0, H), size: random(.5, 2.5), speed: random(.15, 1.15), color: choose([palette.cream, palette.blue, palette.lavender]) }));
      this.ribbons = Array.from({ length: 11 }, (_, index) => ({ x: index * 115 + random(-30, 30), y: random(300, 500), scale: random(.5, 1.2), color: index % 2 ? "#503b74" : "#2e315e" }));
      this.showTitle();
      requestAnimationFrame((time) => this.loop(time));
    },
    reset(mode = selectedMode) {
      this.mode = mode;
      this.state = "playing";
      this.elapsed = 0; this.stageTime = 0; this.world = 0; this.spawnTimer = .8; this.ambientTimer = 0; this.statusTimer = 0;
      this.score = 0; this.shake = 0; this.flash = 0; this.bossStarted = false; this.clearDelay = 0;
      this.enemies = []; this.shots = []; this.enemyShots = []; this.pickups = []; this.particles = [];
      this.stats = { kills: 0, capsules: 0, shots: 0, grazes: 0 };
      this.powerCursor = -1;
      this.upgrades = createUpgrades();
      const lives = MODE_LIVES[mode] ?? MODE_LIVES.story;
      this.hero = { x: 155, y: H / 2, r: 15, speed: 255, hp: 3, lives, invincible: 2.2, fireTimer: 0, missileTimer: 0, shieldTimer: 0, optionAngle: 0, hitPulse: 0 };
      this.hidePanels(); this.updateHud(); this.updatePower(); this.setStatus("TEA GARDENへの降下を開始。時計うさぎを追え。");
      UI.boss.hidden = true; audio.wake(); audio.beep("power");
    },
    showTitle() {
      this.state = "title"; this.hidePanels(); UI.title.hidden = false; UI.boss.hidden = true;
      this.setStatus("LOOKING FOR A RABBIT HOLE…"); this.updateHud(); this.updatePower();
    },
    hidePanels() { [UI.title, UI.pause, UI.result, UI.help, UI.settings].forEach((panel) => { panel.hidden = true; }); },
    togglePause() {
      if (this.state === "playing") { this.state = "paused"; UI.pause.hidden = false; this.setStatus("THE CLOCK HAS PAUSED"); }
      else if (this.state === "paused") { this.state = "playing"; UI.pause.hidden = true; this.setStatus("飛行を再開。ティーカップを傾けろ。"); }
    },
    end(won) {
      if (this.state === "result") return;
      this.state = "result"; UI.result.hidden = false; UI.boss.hidden = true;
      if (this.score > save.best) { save.best = this.score; persist(); }
      if (won) {
        this.unlock("CROWN BREAKER", "赤の女王をお茶会から追い出した");
        UI.resultEyebrow.textContent = "THE LOOKING GLASS OPENS";
        UI.resultHeading.textContent = "夢は、まだ続く。";
        UI.resultCopy.textContent = "女王の命令は砕け、ティーガーデンに風が戻った。あなたは次の穴を見つけるまで飛び続ける。";
        audio.beep("win");
      } else {
        UI.resultEyebrow.textContent = "THE QUEEN'S DECREE";
        UI.resultHeading.textContent = "首を…はねないで。";
        UI.resultCopy.textContent = "アリス・クラフトは安全な雲に戻された。強化の順番を考えて、もう一度お茶会へ。";
        audio.beep("boom");
      }
      UI.reportScore.textContent = fmt(this.score); UI.reportKills.textContent = this.stats.kills; UI.reportCapsules.textContent = this.stats.capsules;
      this.updateHud(); this.updateAchievements();
    },
    update(dt) {
      this.world += dt * (this.state === "playing" ? 72 : 20) * (save.settings.motion ? .24 : 1);
      this.flash = Math.max(0, this.flash - dt * 1.8);
      this.shake = Math.max(0, this.shake - dt * 1.6);
      this.stars.forEach((star) => { star.x -= star.speed * dt * (this.state === "playing" ? 80 : 18); if (star.x < -4) { star.x = W + 4; star.y = random(0, H); } });
      if (this.state !== "playing") { this.updateParticles(dt); return; }
      const h = this.hero;
      this.elapsed += dt; this.stageTime += dt; h.fireTimer -= dt; h.missileTimer -= dt; h.invincible = Math.max(0, h.invincible - dt); h.hitPulse = Math.max(0, h.hitPulse - dt); h.shieldTimer = Math.max(0, h.shieldTimer - dt);
      let dx = 0; let dy = 0;
      if (active("ArrowLeft") || active("KeyA") || active("left")) dx--;
      if (active("ArrowRight") || active("KeyD") || active("right")) dx++;
      if (active("ArrowUp") || active("KeyW") || active("up")) dy--;
      if (active("ArrowDown") || active("KeyS") || active("down")) dy++;
      if (dx || dy) { const norm = 1 / Math.hypot(dx, dy); const velocity = h.speed + this.upgrades.speed * 42; h.x = clamp(h.x + dx * norm * velocity * dt, 38, W * .6); h.y = clamp(h.y + dy * norm * velocity * dt, 37, H - 37); }
      if (active("Space") || active("KeyJ") || active("fire")) this.fire();
      if (this.stageTime > 48 && !this.bossStarted) this.startBoss();
      if (!this.bossStarted) { this.spawnTimer -= dt; if (this.spawnTimer <= 0) { this.spawnWave(); this.spawnTimer = Math.max(.34, 1.05 - this.stageTime * .011 - (this.mode === "garden" ? .16 : 0)); } }
      h.optionAngle += dt * 2.1;
      this.updateEnemies(dt); this.updateShots(dt); this.updatePickups(dt); this.updateParticles(dt); this.collisions(); this.updateBossHud();
      this.statusTimer += dt;
      if (this.statusTimer > 9 && !this.bossStarted) { this.statusTimer = 0; this.setStatus(choose(["時計うさぎの足跡を発見。", "カード兵の隊列に注意。", "カプセルは左下のメーターを進める。", "赤いバラには、近づかないこと。"])); }
    },
    fire() {
      const h = this.hero;
      const rate = Math.max(.078, .19 - this.upgrades.speed * .019);
      if (h.fireTimer > 0) return;
      h.fireTimer = rate; this.stats.shots++;
      const originX = h.x + 19; const originY = h.y;
      this.shots.push({ x: originX, y: originY, vx: 620, vy: 0, w: 17, h: 5, damage: this.upgrades.laser ? 2.6 : 1.15, kind: this.upgrades.laser ? "laser" : "shot", life: 1.65, pierce: this.upgrades.laser ? 2 : 0, tint: this.upgrades.laser ? palette.mint : palette.gold });
      if (this.upgrades.double) {
        this.shots.push({ x: originX, y: originY - 7, vx: 580, vy: -105, w: 12, h: 4, damage: 1, kind: "double", life: 1.45, pierce: 0, tint: palette.pink });
        this.shots.push({ x: originX, y: originY + 7, vx: 580, vy: 105, w: 12, h: 4, damage: 1, kind: "double", life: 1.45, pierce: 0, tint: palette.pink });
      }
      for (let index = 0; index < this.upgrades.option; index++) {
        const pos = this.optionPosition(index);
        this.shots.push({ x: pos.x + 6, y: pos.y, vx: 570, vy: 0, w: 12, h: 4, damage: .82, kind: "option", life: 1.5, pierce: 0, tint: palette.blue });
      }
      if (this.upgrades.missile && h.missileTimer <= 0) {
        h.missileTimer = Math.max(.28, .7 - this.upgrades.missile * .15);
        this.shots.push({ x: originX, y: originY + 11, vx: 350, vy: 110, w: 10, h: 8, damage: 2.4, kind: "missile", life: 2, pierce: 0, tint: palette.red });
      }
      this.spark(originX, originY, 2, palette.cream, 55); audio.beep("shot");
    },
    optionPosition(index) { const h = this.hero; const angle = h.optionAngle + index * (Math.PI * 2 / Math.max(1, this.upgrades.option)); return { x: h.x - 4 + Math.cos(angle) * (36 + index * 7), y: h.y + Math.sin(angle) * (25 + index * 5) }; },
    spawnWave() {
      const difficulty = 1 + this.stageTime / 55;
      const roll = Math.random();
      if (roll < .38) {
        const definition = ENEMY_DEFINITIONS.card;
        const y = random(...definition.yRange); const count = Math.random() < .45 ? 2 : 1;
        for (let index = 0; index < count; index++) this.enemies.push(createEnemy("card", W + definition.spawnOffset + index * 64, clamp(y + index * 42, 40, H - 40), difficulty));
      } else if (roll < .72) {
        const definition = ENEMY_DEFINITIONS.hare;
        this.enemies.push(createEnemy("hare", W + definition.spawnOffset, random(...definition.yRange), difficulty));
      } else if (roll < .91) {
        const definition = ENEMY_DEFINITIONS.teapot;
        this.enemies.push(createEnemy("teapot", W + definition.spawnOffset, random(...definition.yRange), difficulty));
      } else {
        const definition = ENEMY_DEFINITIONS.cat;
        this.enemies.push(createEnemy("cat", W + definition.spawnOffset, random(...definition.yRange), difficulty));
      }
    },
    startBoss() {
      this.bossStarted = true; this.setStatus("警報：赤の女王がティーガーデンを封鎖した。"); showToast("BOSS APPROACHING — RED QUEEN"); audio.beep("warning");
      this.enemies.push(createEnemy("queen", W + ENEMY_DEFINITIONS.queen.spawnOffset, H / 2));
      UI.boss.hidden = false;
    },
    updateEnemies(dt) {
      const h = this.hero;
      for (const enemy of this.enemies) {
        enemy.phase += dt;
        this.moveEnemy(enemy, dt);
        enemy.shot -= dt;
        if (enemy.shot <= 0 && (enemy.x < W - 6 || enemy.boss)) {
          this.enemyFire(enemy, h);
          enemy.shot = this.enemyShotDelay(enemy);
        }
      }
      this.enemies = this.enemies.filter((enemy) => enemy.x > -130 && enemy.hp > 0);
    },
    moveEnemy(enemy, dt) {
      switch (enemy.type) {
        case "card": enemy.x -= enemy.vx * dt; enemy.y += Math.sin(enemy.phase * 2.5) * 28 * dt; break;
        case "hare": enemy.x -= enemy.vx * dt; enemy.y = enemy.baseY + Math.sin(enemy.phase * 2.5) * 52; break;
        case "teapot": enemy.x -= enemy.vx * dt; enemy.y += Math.sin(enemy.phase * 1.8) * 13 * dt; break;
        case "cat": enemy.x -= enemy.vx * dt; enemy.y = enemy.baseY + Math.sin(enemy.phase * 1.7) * 78; break;
        case "queen":
          if (!enemy.entered) { enemy.x -= enemy.vx * dt; if (enemy.x < 770) enemy.entered = true; }
          else { enemy.x = 770 + Math.sin(enemy.phase * .65) * 36; enemy.y = H / 2 + Math.sin(enemy.phase * 1.25) * 112; }
          break;
      }
    },
    enemyShotDelay(enemy) {
      if (!enemy.boss) return enemy.type === "teapot" ? 1.7 : 1.3;
      return .82 / (1 + (1 - enemy.hp / enemy.maxHp) * .85);
    },
    enemyFire(enemy, hero) {
      const angle = Math.atan2(hero.y - enemy.y, hero.x - enemy.x);
      if (enemy.type === "card") return;
      if (enemy.type === "hare") { this.enemyShots.push({ x: enemy.x - 15, y: enemy.y + 4, vx: Math.cos(angle) * 190, vy: Math.sin(angle) * 190, r: 5, life: 4, kind: "watch", color: palette.gold }); return; }
      if (enemy.type === "teapot") { [-.28, 0, .28].forEach((spread) => this.enemyShots.push({ x: enemy.x - 20, y: enemy.y, vx: Math.cos(angle + spread) * 165, vy: Math.sin(angle + spread) * 165, r: 6, life: 4, kind: "tea", color: palette.pink })); return; }
      if (enemy.type === "cat") { [-.34, -.17, 0, .17, .34].forEach((spread) => this.enemyShots.push({ x: enemy.x - 25, y: enemy.y, vx: Math.cos(angle + spread) * 150, vy: Math.sin(angle + spread) * 150, r: 5, life: 4.2, kind: "smile", color: palette.mint })); return; }
      if (enemy.type === "queen") {
        enemy.pattern++;
        if (enemy.pattern % 3 === 0) {
          for (let i = 0; i < 12; i++) { const a = i * Math.PI * 2 / 12 + enemy.phase; this.enemyShots.push({ x: enemy.x - 35, y: enemy.y, vx: Math.cos(a) * 145 - 45, vy: Math.sin(a) * 145, r: 7, life: 4.5, kind: "heart", color: i % 2 ? palette.pink : palette.red }); }
        } else {
          [-.38, -.19, 0, .19, .38].forEach((spread) => this.enemyShots.push({ x: enemy.x - 42, y: enemy.y - 5, vx: Math.cos(angle + spread) * 225, vy: Math.sin(angle + spread) * 225, r: 7, life: 4, kind: "heart", color: palette.red }));
        }
      }
    },
    updateShots(dt) {
      for (const shot of this.shots) {
        shot.life -= dt;
        if (shot.kind === "missile") {
          const target = this.findMissileTarget(shot);
          if (target) shot.vy += clamp(target.y - shot.y, -170, 170) * dt * 1.8;
          shot.vy = clamp(shot.vy, -260, 260);
        }
        shot.x += shot.vx * dt; shot.y += shot.vy * dt;
      }
      for (const shot of this.enemyShots) { shot.life -= dt; shot.x += shot.vx * dt; shot.y += shot.vy * dt; }
      this.shots = this.shots.filter((shot) => shot.life > 0 && shot.x < W + 80 && shot.y > -70 && shot.y < H + 70);
      this.enemyShots = this.enemyShots.filter((shot) => shot.life > 0 && shot.x > -70 && shot.x < W + 70 && shot.y > -70 && shot.y < H + 70);
    },
    findMissileTarget(shot) {
      let target = null;
      let nearestDistance = Infinity;
      for (const enemy of this.enemies) {
        if (enemy.x <= shot.x - 20) continue;
        const distance = dist(enemy.x, enemy.y, shot.x, shot.y);
        if (distance < nearestDistance) { target = enemy; nearestDistance = distance; }
      }
      return target;
    },
    updatePickups(dt) {
      for (const pickup of this.pickups) { pickup.x -= 95 * dt; pickup.y += Math.sin((pickup.phase += dt) * 4) * 18 * dt; pickup.spin += dt * 3; }
      this.pickups = this.pickups.filter((pickup) => pickup.x > -30);
    },
    collisions() {
      const h = this.hero;
      for (const shot of this.shots) {
        for (const enemy of this.enemies) {
          if (enemy.hp <= 0 || shot.hit === enemy) continue;
          if (Math.abs(shot.x - enemy.x) < enemy.r + shot.w && Math.abs(shot.y - enemy.y) < enemy.r + shot.h) {
            enemy.hp -= shot.damage; shot.hit = enemy; this.spark(shot.x, shot.y, 2, shot.tint, 45); audio.beep("hit");
            if (shot.pierce > 0) { shot.pierce--; shot.hit = null; } else shot.life = 0;
            if (enemy.hp <= 0) this.destroyEnemy(enemy);
          }
        }
      }
      for (const bullet of this.enemyShots) {
        if (dist(bullet.x, bullet.y, h.x, h.y) < bullet.r + h.r) { bullet.life = 0; this.damageHero(); }
        else if (dist(bullet.x, bullet.y, h.x, h.y) < bullet.r + h.r + 20) { this.stats.grazes++; if (this.stats.grazes % 14 === 0) { this.addScore(30); this.setStatus("ギリギリのティータイム！ GRAZE +30"); } }
      }
      for (const enemy of this.enemies) if (!enemy.boss && dist(enemy.x, enemy.y, h.x, h.y) < enemy.r + h.r) { enemy.hp = 0; this.destroyEnemy(enemy); this.damageHero(); }
      for (const pickup of this.pickups) if (dist(pickup.x, pickup.y, h.x, h.y) < 29) { pickup.x = -100; this.collectPickup(); }
    },
    destroyEnemy(enemy) {
      if (enemy.dead) return;
      enemy.dead = true; enemy.hp = 0; this.stats.kills++; this.addScore(enemy.value); this.explosion(enemy.x, enemy.y, enemy.boss ? 52 : 20, enemy.boss ? palette.pink : palette.gold); audio.beep("boom");
      if (!enemy.boss && Math.random() < (enemy.type === "teapot" ? .52 : .19)) this.pickups.push({ x: enemy.x, y: enemy.y, phase: random(0, 6), spin: 0 });
      if (enemy.boss) { this.clearDelay = 2.6; this.setStatus("女王の冠が砕けた。出口が見える！"); this.unlock("TEA PARTY END", "赤の女王の冠を砕いた"); }
    },
    damageHero() {
      const h = this.hero;
      if (h.invincible > 0) return;
      if (h.shieldTimer > 0) { h.shieldTimer = Math.max(0, h.shieldTimer - 2.8); h.invincible = .45; this.setStatus("ティーカップの盾が攻撃を弾いた！"); this.spark(h.x, h.y, 12, palette.blue, 120); audio.beep("hit"); return; }
      h.hp--; h.invincible = 2.1; h.hitPulse = 1; this.shake = save.settings.shake ? .52 : 0; this.flash = .32; this.explosion(h.x, h.y, 16, palette.pink); audio.beep("boom");
      if (h.hp <= 0) { h.lives--; if (h.lives <= 0) { this.end(false); return; } h.hp = 3; h.x = 145; h.y = H / 2; this.setStatus("アリス・クラフトを雲から回収。飛行再開！"); }
      this.updateHud();
    },
    collectPickup() {
      this.stats.capsules++; this.addScore(250); this.powerCursor = Math.min(5, this.powerCursor + 1); this.spark(this.hero.x, this.hero.y, 13, palette.gold, 105); this.setStatus("TEA CAPSULE を獲得。SHIFTで強化を選択。"); this.updatePower(); audio.beep("collect");
      if (this.stats.capsules >= 7) this.unlock("CURIOUS COLLECTOR", "7個のティーカプセルを集めた");
    },
    activatePower() {
      if (this.state !== "playing") return;
      if (this.powerCursor < 0) { showToast("先に TEA CAPSULE を集めよう"); return; }
      const power = POWER_DEFINITIONS[this.powerCursor];
      if (power.key === "shield") { this.hero.shieldTimer = 12; this.upgrades.shield = 1; }
      else this.upgrades[power.key] = Math.min(power.cap, this.upgrades[power.key] + 1);
      this.powerCursor = -1; this.spark(this.hero.x, this.hero.y, 20, palette.mint, 145); this.shake = save.settings.shake ? .18 : 0; this.setStatus(`${power.label} を発動。もっと深く落ちていこう。`); this.updatePower(); audio.beep("power");
      if (this.upgrades.option >= 3) this.unlock("THREE GRINS", "チェシャ猫オプションを3機揃えた");
      if (this.upgrades.laser && this.upgrades.missile && this.upgrades.option >= 2) this.unlock("FULL TEA SET", "強化されたティーセットを完成させた");
    },
    addScore(amount) { this.score += amount; this.updateHud(); },
    updateParticles(dt) {
      for (const particle of this.particles) { particle.life -= dt; particle.x += particle.vx * dt; particle.y += particle.vy * dt; particle.vy += (particle.gravity || 0) * dt; particle.size *= .988; }
      this.particles = this.particles.filter((particle) => particle.life > 0 && particle.size > .3);
      if (this.clearDelay > 0 && this.state === "playing") { this.clearDelay -= dt; if (this.clearDelay <= 0) this.end(true); }
    },
    spark(x, y, count, color, speed) { for (let i = 0; i < count; i++) { const angle = random(0, Math.PI * 2); const magnitude = random(speed * .3, speed); this.particles.push({ x, y, vx: Math.cos(angle) * magnitude, vy: Math.sin(angle) * magnitude, life: random(.22, .65), size: random(1.3, 3.5), color, gravity: 12 }); } },
    explosion(x, y, count, color) { this.spark(x, y, count, color, 160); this.spark(x, y, Math.floor(count * .6), palette.cream, 100); this.shake = save.settings.shake ? Math.max(this.shake, count > 35 ? .62 : .2) : 0; },
    setStatus(text) { UI.status.textContent = text; },
    updateHud() { const h = this.hero; UI.score.textContent = fmt(this.score); UI.best.textContent = fmt(Math.max(save.best, this.score)); UI.stage.textContent = this.bossStarted ? "01 · RED QUEEN" : "01 · TEA GARDEN"; UI.lives.textContent = h ? "♥ ".repeat(Math.max(0, h.lives)).trim() || "—" : "♥ ♥ ♥"; },
    updatePower() {
      UI.cells.forEach((cell, index) => { const power = POWER_DEFINITIONS[index]; cell.classList.toggle("ready", this.powerCursor === index); cell.classList.toggle("owned", power.key === "shield" ? (this.hero?.shieldTimer > 0) : this.upgrades[power.key] > 0); });
      if (this.powerCursor < 0) UI.powerTip.textContent = "CAPSULEを取って能力を選択";
      else UI.powerTip.textContent = `${POWER_DEFINITIONS[this.powerCursor].label} 点灯中 — SHIFT / K で発動`;
    },
    updateBossHud() {
      const queen = this.enemies.find((enemy) => enemy.boss);
      if (!queen) return;
      const ratio = clamp(queen.hp / queen.maxHp, 0, 1); UI.bossBar.style.width = `${ratio * 100}%`; UI.bossPercent.textContent = `${Math.ceil(ratio * 100)}%`;
    },
    unlock(title, description) {
      if (save.achievements.some((achievement) => achievement.title === title)) return;
      save.achievements.push({ title, description }); persist(); this.updateAchievements(); showToast(`MEMENTO: ${title}`);
    },
    updateAchievements() {
      UI.achievements.innerHTML = "";
      if (!save.achievements.length) { const li = document.createElement("li"); li.textContent = "赤の女王を倒して、最初の記念品を見つけよう。"; UI.achievements.append(li); return; }
      save.achievements.slice(-3).forEach((achievement) => { const li = document.createElement("li"); li.title = achievement.description; li.textContent = achievement.title; UI.achievements.append(li); });
    },
    loop(last) {
      const now = performance.now(); const dt = Math.min(.034, ((now - (last || now)) / 1000) || .016); this.update(dt); this.render(); requestAnimationFrame((time) => this.loop(time));
    },
    render() {
      const high = save.settings.contrast;
      const shakeX = this.shake && save.settings.shake ? random(-1, 1) * this.shake * 8 : 0; const shakeY = this.shake && save.settings.shake ? random(-1, 1) * this.shake * 8 : 0;
      ctx.save(); ctx.clearRect(0, 0, W, H); ctx.translate(shakeX, shakeY); this.drawBackground(high); this.drawWorld(); this.drawPickups(); this.drawShots(); this.drawEnemies(); if (this.hero) this.drawHero(); this.drawParticles(); if (this.flash) { ctx.fillStyle = `rgba(255, 218, 230, ${this.flash * .36})`; ctx.fillRect(-20, -20, W + 40, H + 40); } ctx.restore();
    },
    drawBackground(high) {
      const gradient = ctx.createLinearGradient(0, 0, 0, H); gradient.addColorStop(0, high ? "#030308" : "#1f1842"); gradient.addColorStop(.6, high ? "#09051b" : "#16153a"); gradient.addColorStop(1, high ? "#000" : "#382144"); ctx.fillStyle = gradient; ctx.fillRect(0, 0, W, H);
      for (const star of this.stars) { ctx.globalAlpha = .35 + star.size / 5; ctx.fillStyle = star.color; ctx.fillRect(star.x, star.y, star.size, star.size); } ctx.globalAlpha = 1;
      ctx.save(); ctx.globalAlpha = .24; ctx.strokeStyle = high ? "#8a8aff" : "#7464a7"; ctx.lineWidth = 1;
      const offset = (this.world * .22) % 46;
      for (let x = -46; x < W + 46; x += 46) { ctx.beginPath(); ctx.moveTo(x - offset, 0); ctx.lineTo(x - 160 - offset, H); ctx.stroke(); }
      for (let y = 54; y < H; y += 46) { ctx.beginPath(); ctx.moveTo(0, y); ctx.lineTo(W, y + 115); ctx.stroke(); } ctx.restore();
      ctx.save(); ctx.globalAlpha = .17; ctx.fillStyle = palette.lavender;
      for (let i = 0; i < 5; i++) { const x = ((i * 250 - this.world * .34) % (W + 260)) - 130; const y = 85 + i * 68; ctx.beginPath(); ctx.ellipse(x, y, 110, 20, 0, 0, Math.PI * 2); ctx.ellipse(x + 80, y - 8, 70, 18, 0, 0, Math.PI * 2); ctx.fill(); } ctx.restore();
    },
    drawWorld() {
      const travel = this.world;
      for (const ribbon of this.ribbons) {
        const x = ((ribbon.x - travel * .42) % (W + 180)) + (ribbon.x - travel * .42 < -180 ? W + 180 : 0);
        const y = ribbon.y;
        ctx.save(); ctx.translate(x, y); ctx.scale(ribbon.scale, ribbon.scale); ctx.globalAlpha = .78;
        ctx.fillStyle = ribbon.color; ctx.fillRect(-24, 20, 48, 124); ctx.fillStyle = "#67475d"; ctx.beginPath(); ctx.arc(0, 20, 29, Math.PI, 0); ctx.fill(); ctx.fillStyle = "#e8b2bb"; ctx.beginPath(); ctx.arc(0, 9, 14, Math.PI, 0); ctx.fill(); ctx.fillStyle = "#e1cc83"; ctx.fillRect(-2, 26, 4, 112); ctx.restore();
      }
      ctx.save(); ctx.globalAlpha = .32; const floor = H - 38; const cell = 42; const offset = (-travel * .7) % (cell * 2);
      for (let row = 0; row < 3; row++) for (let col = -1; col < Math.ceil(W / cell) + 2; col++) { const dark = (row + col) % 2; ctx.fillStyle = dark ? "#573752" : "#d5a1a8"; ctx.fillRect(col * cell + offset, floor + row * cell * .42, cell, cell * .42); } ctx.restore();
      const clockX = W - 145; const clockY = 78; ctx.save(); ctx.globalAlpha = .2; ctx.translate(clockX, clockY); ctx.strokeStyle = palette.gold; ctx.lineWidth = 4; ctx.beginPath(); ctx.arc(0, 0, 37, 0, Math.PI * 2); ctx.stroke(); ctx.beginPath(); ctx.moveTo(0, 0); ctx.lineTo(Math.cos(this.world * .08) * 24, Math.sin(this.world * .08) * 24); ctx.moveTo(0, 0); ctx.lineTo(Math.cos(-this.world * .14) * 20, Math.sin(-this.world * .14) * 20); ctx.stroke(); ctx.restore();
    },
    drawHero() {
      const h = this.hero; if (h.invincible > 0 && Math.floor(h.invincible * 10) % 2 === 0) return;
      ctx.save(); ctx.translate(h.x, h.y);
      if (h.shieldTimer > 0) { ctx.globalAlpha = .65 + Math.sin(this.world * 7) * .2; ctx.strokeStyle = palette.blue; ctx.lineWidth = 3; ctx.beginPath(); ctx.arc(0, 0, 29 + Math.sin(this.world * 6) * 2, 0, Math.PI * 2); ctx.stroke(); ctx.globalAlpha = 1; }
      ctx.fillStyle = "#e5e8ff"; ctx.beginPath(); ctx.moveTo(-22, 0); ctx.lineTo(-5, -16); ctx.lineTo(19, -9); ctx.lineTo(26, 0); ctx.lineTo(19, 9); ctx.lineTo(-5, 16); ctx.closePath(); ctx.fill();
      ctx.fillStyle = palette.pink; ctx.beginPath(); ctx.moveTo(-10, -10); ctx.lineTo(-3, -27); ctx.lineTo(2, -10); ctx.moveTo(-8, 10); ctx.lineTo(-1, 25); ctx.lineTo(4, 10); ctx.fill();
      ctx.fillStyle = palette.ink; ctx.beginPath(); ctx.arc(6, 0, 8, 0, Math.PI * 2); ctx.fill(); ctx.fillStyle = palette.gold; ctx.beginPath(); ctx.arc(7, 0, 4, 0, Math.PI * 2); ctx.fill();
      ctx.fillStyle = palette.mint; ctx.fillRect(-27, -5, 9, 10); ctx.restore();
      for (let i = 0; i < this.upgrades.option; i++) { const pos = this.optionPosition(i); this.drawOption(pos.x, pos.y, i); }
    },
    drawOption(x, y, index) { ctx.save(); ctx.translate(x, y); ctx.fillStyle = "#e7e7ff"; ctx.beginPath(); ctx.arc(0, 0, 8, 0, Math.PI * 2); ctx.fill(); ctx.fillStyle = palette.blue; ctx.beginPath(); ctx.arc(-3, -2, 2, 0, Math.PI * 2); ctx.arc(3, -2, 2, 0, Math.PI * 2); ctx.fill(); ctx.strokeStyle = palette.pink; ctx.lineWidth = 2; ctx.beginPath(); ctx.arc(0, 1, 4, .2, Math.PI - .2); ctx.stroke(); ctx.restore(); },
    drawShots() {
      for (const shot of this.shots) { ctx.save(); ctx.translate(shot.x, shot.y); if (shot.kind === "missile") { ctx.fillStyle = shot.tint; ctx.beginPath(); ctx.moveTo(9, 0); ctx.lineTo(-7, -5); ctx.lineTo(-4, 0); ctx.lineTo(-7, 5); ctx.closePath(); ctx.fill(); ctx.fillStyle = palette.gold; ctx.fillRect(-10, -2, 5, 4); } else { ctx.globalAlpha = .8; ctx.fillStyle = shot.tint; ctx.shadowColor = shot.tint; ctx.shadowBlur = shot.kind === "laser" ? 12 : 6; ctx.fillRect(-shot.w / 2, -shot.h / 2, shot.w, shot.h); if (shot.kind === "laser") { ctx.globalAlpha = .45; ctx.fillRect(-shot.w / 2 - 10, -1, shot.w + 15, 2); } } ctx.restore(); }
      for (const shot of this.enemyShots) { ctx.save(); ctx.translate(shot.x, shot.y); ctx.fillStyle = shot.color; ctx.shadowColor = shot.color; ctx.shadowBlur = 8; if (shot.kind === "heart") { ctx.rotate(Math.atan2(shot.vy, shot.vx) + Math.PI / 2); ctx.beginPath(); ctx.moveTo(0, shot.r); ctx.bezierCurveTo(-shot.r * 1.5, 0, -shot.r, -shot.r, 0, -shot.r * .35); ctx.bezierCurveTo(shot.r, -shot.r, shot.r * 1.5, 0, 0, shot.r); ctx.fill(); } else { ctx.beginPath(); ctx.arc(0, 0, shot.r, 0, Math.PI * 2); ctx.fill(); if (shot.kind === "watch") { ctx.strokeStyle = palette.ink; ctx.lineWidth = 1; ctx.beginPath(); ctx.moveTo(0, 0); ctx.lineTo(0, -3); ctx.stroke(); } } ctx.restore(); }
    },
    drawPickups() { for (const pickup of this.pickups) { ctx.save(); ctx.translate(pickup.x, pickup.y); ctx.rotate(pickup.spin); ctx.shadowColor = palette.gold; ctx.shadowBlur = 13; ctx.fillStyle = palette.gold; ctx.beginPath(); ctx.moveTo(0, -12); ctx.lineTo(9, 0); ctx.lineTo(0, 12); ctx.lineTo(-9, 0); ctx.closePath(); ctx.fill(); ctx.fillStyle = palette.cream; ctx.fillRect(-2, -5, 4, 10); ctx.restore(); } },
    drawEnemies() { for (const enemy of this.enemies) { if (enemy.type === "card") this.drawCard(enemy); if (enemy.type === "hare") this.drawHare(enemy); if (enemy.type === "teapot") this.drawTeapot(enemy); if (enemy.type === "cat") this.drawCat(enemy); if (enemy.type === "queen") this.drawQueen(enemy); } },
    drawHealth(enemy, width) { if (enemy.boss || enemy.hp >= enemy.maxHp) return; ctx.save(); ctx.translate(enemy.x, enemy.y - enemy.r - 11); ctx.fillStyle = "#27152e"; ctx.fillRect(-width / 2, 0, width, 4); ctx.fillStyle = palette.pink; ctx.fillRect(-width / 2, 0, width * clamp(enemy.hp / enemy.maxHp, 0, 1), 4); ctx.restore(); },
    drawCard(enemy) { ctx.save(); ctx.translate(enemy.x, enemy.y); ctx.rotate(Math.sin(enemy.phase * 2) * .16); ctx.fillStyle = palette.cream; ctx.fillRect(-14, -20, 28, 40); ctx.strokeStyle = palette.red; ctx.lineWidth = 2; ctx.strokeRect(-14, -20, 28, 40); ctx.fillStyle = palette.red; ctx.font = "bold 22px Georgia"; ctx.textAlign = "center"; ctx.fillText("♥", 0, 8); ctx.restore(); this.drawHealth(enemy, 30); },
    drawHare(enemy) { ctx.save(); ctx.translate(enemy.x, enemy.y); ctx.fillStyle = "#e9e3ff"; ctx.beginPath(); ctx.ellipse(0, 2, 18, 15, 0, 0, Math.PI * 2); ctx.fill(); ctx.fillStyle = palette.pink; ctx.beginPath(); ctx.ellipse(-8, -18, 5, 17, -.25, 0, Math.PI * 2); ctx.ellipse(7, -18, 5, 17, .25, 0, Math.PI * 2); ctx.fill(); ctx.fillStyle = palette.ink; ctx.beginPath(); ctx.arc(-6, -2, 2, 0, Math.PI * 2); ctx.arc(6, -2, 2, 0, Math.PI * 2); ctx.fill(); ctx.strokeStyle = palette.gold; ctx.lineWidth = 3; ctx.beginPath(); ctx.arc(16, 9, 8, 0, Math.PI * 2); ctx.stroke(); ctx.restore(); this.drawHealth(enemy, 34); },
    drawTeapot(enemy) { ctx.save(); ctx.translate(enemy.x, enemy.y); ctx.fillStyle = "#cfa3db"; ctx.beginPath(); ctx.ellipse(0, 3, 25, 19, 0, 0, Math.PI * 2); ctx.fill(); ctx.fillRect(-9, -26, 18, 10); ctx.fillStyle = palette.cream; ctx.beginPath(); ctx.arc(0, -16, 10, Math.PI, 0); ctx.fill(); ctx.strokeStyle = palette.pink; ctx.lineWidth = 5; ctx.beginPath(); ctx.arc(-23, 2, 12, Math.PI / 2, Math.PI * 1.5); ctx.stroke(); ctx.fillStyle = palette.red; ctx.beginPath(); ctx.moveTo(23, -3); ctx.lineTo(43, -12); ctx.lineTo(24, 8); ctx.fill(); ctx.restore(); this.drawHealth(enemy, 42); },
    drawCat(enemy) { ctx.save(); ctx.translate(enemy.x, enemy.y); ctx.globalAlpha = .85; ctx.fillStyle = palette.mint; ctx.beginPath(); ctx.ellipse(0, 0, 30, 19, 0, 0, Math.PI * 2); ctx.fill(); ctx.beginPath(); ctx.moveTo(-22, -12); ctx.lineTo(-18, -36); ctx.lineTo(-5, -18); ctx.moveTo(10, -18); ctx.lineTo(24, -35); ctx.lineTo(25, -9); ctx.fill(); ctx.fillStyle = palette.ink; ctx.beginPath(); ctx.arc(-11, -2, 4, 0, Math.PI * 2); ctx.arc(11, -2, 4, 0, Math.PI * 2); ctx.fill(); ctx.strokeStyle = palette.ink; ctx.lineWidth = 3; ctx.beginPath(); ctx.arc(0, 5, 13, .15, Math.PI - .15); ctx.stroke(); ctx.restore(); this.drawHealth(enemy, 52); },
    drawQueen(enemy) { ctx.save(); ctx.translate(enemy.x, enemy.y); ctx.globalAlpha = enemy.entered ? 1 : .6; ctx.fillStyle = "#8c3158"; ctx.beginPath(); ctx.moveTo(-54, 65); ctx.lineTo(-42, -22); ctx.lineTo(-22, 7); ctx.lineTo(0, -48); ctx.lineTo(22, 7); ctx.lineTo(43, -22); ctx.lineTo(56, 65); ctx.closePath(); ctx.fill(); ctx.fillStyle = palette.cream; ctx.beginPath(); ctx.arc(0, -4, 34, 0, Math.PI * 2); ctx.fill(); ctx.fillStyle = palette.ink; ctx.beginPath(); ctx.arc(-12, -9, 4, 0, Math.PI * 2); ctx.arc(12, -9, 4, 0, Math.PI * 2); ctx.fill(); ctx.strokeStyle = palette.red; ctx.lineWidth = 4; ctx.beginPath(); ctx.arc(0, 3, 17, .18, Math.PI - .18); ctx.stroke(); ctx.fillStyle = palette.gold; ctx.beginPath(); ctx.moveTo(-27, -36); ctx.lineTo(-17, -65); ctx.lineTo(0, -42); ctx.lineTo(17, -65); ctx.lineTo(27, -36); ctx.closePath(); ctx.fill(); ctx.fillStyle = palette.pink; ctx.beginPath(); ctx.arc(0, 38, 16, 0, Math.PI * 2); ctx.fill(); ctx.fillStyle = palette.cream; ctx.font = "bold 25px Georgia"; ctx.textAlign = "center"; ctx.fillText("♥", 0, 47); ctx.restore(); },
    drawParticles() { for (const particle of this.particles) { ctx.save(); ctx.globalAlpha = clamp(particle.life * 2, 0, 1); ctx.fillStyle = particle.color; ctx.fillRect(particle.x - particle.size / 2, particle.y - particle.size / 2, particle.size, particle.size); ctx.restore(); } }
  };

  function showToast(message) { UI.toast.textContent = message; UI.toast.classList.add("show"); clearTimeout(toastTimer); toastTimer = setTimeout(() => UI.toast.classList.remove("show"), 2400); }
  function syncSettings() {
    UI.sound.checked = save.settings.sound; UI.shake.checked = save.settings.shake; UI.motion.checked = save.settings.motion; UI.contrast.checked = save.settings.contrast;
    document.body.classList.toggle("reduced-motion", save.settings.motion); document.body.classList.toggle("high-contrast", save.settings.contrast);
  }
  function closePanel(id) { const panel = document.getElementById(id); if (panel) panel.hidden = true; }

  window.addEventListener("keydown", (event) => {
    const blocked = ["ArrowLeft", "ArrowRight", "ArrowUp", "ArrowDown", "Space"];
    if (blocked.includes(event.code)) event.preventDefault();
    if (event.code === "KeyP" || event.code === "Escape") { if (!event.repeat && !["title", "result"].includes(game.state)) game.togglePause(); return; }
    if ((event.code === "ShiftLeft" || event.code === "ShiftRight" || event.code === "KeyK") && !event.repeat) { game.activatePower(); return; }
    keys.add(event.code); audio.wake();
  });
  window.addEventListener("keyup", (event) => keys.delete(event.code));
  window.addEventListener("blur", () => { keys.clear(); touchKeys.clear(); if (game.state === "playing") game.togglePause(); });

  $("#startButton").addEventListener("click", () => game.reset(selectedMode));
  $("#againButton").addEventListener("click", () => game.reset(selectedMode));
  $("#resultTitleButton").addEventListener("click", () => game.showTitle());
  $("#resumeButton").addEventListener("click", () => game.togglePause());
  $("#quitButton").addEventListener("click", () => game.showTitle());
  $("#helpButton").addEventListener("click", () => { if (game.state !== "playing") UI.help.hidden = false; else { game.togglePause(); UI.help.hidden = false; } });
  $("#settingsButton").addEventListener("click", () => { if (game.state === "playing") game.togglePause(); UI.settings.hidden = false; });
  $$("[data-close]").forEach((button) => button.addEventListener("click", () => closePanel(button.dataset.close)));
  $("#activateButton").addEventListener("click", () => game.activatePower());
  $$(".mode-card").forEach((button) => button.addEventListener("click", () => { selectedMode = button.dataset.mode; $$(".mode-card").forEach((card) => card.classList.toggle("selected", card === button)); }));
  [[UI.sound, "sound"], [UI.shake, "shake"], [UI.motion, "motion"], [UI.contrast, "contrast"]].forEach(([control, name]) => control.addEventListener("change", () => { save.settings[name] = control.checked; persist(); syncSettings(); showToast(`${control.closest("label").querySelector("strong").textContent} を${control.checked ? "オン" : "オフ"}にしました`); }));
  $("#fullscreenButton").addEventListener("click", async () => { try { if (!document.fullscreenElement) await document.documentElement.requestFullscreen(); else await document.exitFullscreen(); } catch { showToast("このブラウザでは全画面表示を開始できませんでした"); } });
  $$("[data-control]").forEach((button) => {
    const control = button.dataset.control;
    const set = (pressed) => { if (control === "power" && pressed) { game.activatePower(); return; } if (control === "pause" && pressed) { game.togglePause(); return; } if (pressed) touchKeys.add(control); else touchKeys.delete(control); audio.wake(); };
    button.addEventListener("pointerdown", (event) => { event.preventDefault(); button.setPointerCapture?.(event.pointerId); set(true); });
    ["pointerup", "pointercancel", "pointerleave"].forEach((name) => button.addEventListener(name, () => set(false)));
  });

  // Gamepad support is polled instead of relying on browser-specific events.
  setInterval(() => {
    const pad = navigator.getGamepads?.()[0]; if (!pad || game.state !== "playing") return;
    const [x, y] = pad.axes; [["left", x < -.35], ["right", x > .35], ["up", y < -.35], ["down", y > .35], ["fire", pad.buttons[0]?.pressed]].forEach(([name, pressed]) => { if (pressed) touchKeys.add(name); else touchKeys.delete(name); });
    if (pad.buttons[1]?.pressed && !touchKeys.has("gpPower")) { game.activatePower(); touchKeys.add("gpPower"); }
    if (!pad.buttons[1]?.pressed) touchKeys.delete("gpPower");
    if (pad.buttons[9]?.pressed && !touchKeys.has("gpPause")) { game.togglePause(); touchKeys.add("gpPause"); } if (!pad.buttons[9]?.pressed) touchKeys.delete("gpPause");
  }, 50);

  syncSettings(); game.updateAchievements(); game.init();
})();
