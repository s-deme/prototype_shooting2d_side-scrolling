using System;
using System.Collections.Generic;
using UnityEngine;

namespace WonderlandFlight
{
    /// <summary>Creates the complete playable scene at runtime, so the project has no prefab or external-art dependency.</summary>
    public static class ClockworkSkyBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateGame()
        {
            if (UnityEngine.Object.FindObjectOfType<ClockworkSkyGame>() != null) return;
            var root = new GameObject("Clockwork Sky Runtime");
            root.AddComponent<ClockworkSkyGame>();
        }
    }

    public sealed class ClockworkSkyGame : MonoBehaviour
    {
        private enum State { Title, Playing, Paused, Results }
        private enum EnemyKind { Card, Hare, Teapot, Cat, Queen }
        private enum ShotKind { Tea, Double, Laser, Missile, Enemy, Heart }

        private sealed class Actor
        {
            public GameObject Node;
            public SpriteRenderer Sprite;
            public EnemyKind Kind;
            public Vector2 Position;
            public float Radius;
            public float Health;
            public float MaxHealth;
            public float Speed;
            public float Phase;
            public float Cooldown;
            public float BaseY;
            public int Score;
            public bool Boss;
            public bool Entered;
            public int Pattern;
        }

        private sealed class Shot
        {
            public GameObject Node;
            public SpriteRenderer Sprite;
            public Vector2 Position;
            public Vector2 Velocity;
            public float Radius;
            public float Damage;
            public float Lifetime;
            public int Pierce;
            public ShotKind Kind;
        }

        private sealed class Particle
        {
            public GameObject Node;
            public SpriteRenderer Sprite;
            public Vector2 Velocity;
            public float Lifetime;
            public float Scale;
        }

        private static class Ink
        {
            // Semantic color roles; gameplay visuals consume these roles rather than scattered swatches.
            public static readonly Color Surface = Hex("14142B");
            public static readonly Color SurfaceRaised = Hex("201D3F");
            public static readonly Color OnSurface = Hex("F7F3FF");
            public static readonly Color OnSurfaceVariant = Hex("C9C3D7");
            public static readonly Color Primary = Hex("F7CA6F");
            public static readonly Color OnPrimary = Hex("261712");
            public static readonly Color PrimaryContainer = Hex("684936");
            public static readonly Color Secondary = Hex("E4B3D3");
            public static readonly Color Danger = Hex("DD567B");
            public static readonly Color Success = Hex("8CE8C4");
            public static readonly Color Info = Hex("8CC7FF");
            public static readonly Color Cream = Hex("FFF4D8");
            public static Color Hex(string hex)
            {
                Color color;
                ColorUtility.TryParseHtmlString("#" + hex, out color);
                return color;
            }
        }

        private const string ScoreKey = "clockwork.best-score";
        private const string SoundKey = "clockwork.sound";
        private const string ShakeKey = "clockwork.shake";
        private const string MotionKey = "clockwork.motion";
        private const string ContrastKey = "clockwork.contrast";
        private const string MementoKey = "clockwork.mementos";

        private readonly List<Actor> enemies = new List<Actor>();
        private readonly List<Shot> friendlyShots = new List<Shot>();
        private readonly List<Shot> enemyShots = new List<Shot>();
        private readonly List<Shot> capsules = new List<Shot>();
        private readonly List<Particle> particles = new List<Particle>();
        private readonly List<GameObject> stars = new List<GameObject>();
        private readonly List<GameObject> options = new List<GameObject>();

        private Camera gameCamera;
        private Sprite squareSprite;
        private AudioSource audioSource;
        private State state;
        private bool gardenMode;
        private bool showHelp;
        private bool showSettings;
        private bool soundEnabled;
        private bool shakeEnabled;
        private bool reducedMotion;
        private bool highContrast;
        private bool bossStarted;
        private bool bossDefeated;
        private Vector2 playerPosition;
        private GameObject player;
        private float playerInvulnerability;
        private float shieldTimer;
        private float playerFireTimer;
        private float missileTimer;
        private float stageTime;
        private float spawnTimer;
        private float clearTimer;
        private float shakeTimer;
        private float worldTime;
        private int score;
        private int bestScore;
        private int lives;
        private int playerHealth;
        private int kills;
        private int capsuleCount;
        private int powerCursor;
        private int speedLevel;
        private int missileLevel;
        private int doubleLevel;
        private int laserLevel;
        private int optionLevel;
        private string status = "LOOKING FOR A RABBIT HOLE…";
        private string toast = "";
        private float toastTimer;

        private void Awake()
        {
            Application.targetFrameRate = 120;
            QualitySettings.vSyncCount = 0;
            CreateRuntimeResources();
            LoadSettings();
            CreateCamera();
            CreateStars();
            state = State.Title;
        }

        private void CreateRuntimeResources()
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            texture.SetPixel(0, 0, Color.white);
            texture.Apply();
            squareSprite = Sprite.Create(texture, new Rect(0, 0, 1, 1), new Vector2(.5f, .5f), 1f);
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0f;
        }

        private void CreateCamera()
        {
            gameCamera = Camera.main;
            if (gameCamera == null)
            {
                var cameraNode = new GameObject("Clockwork Camera");
                gameCamera = cameraNode.AddComponent<Camera>();
                cameraNode.tag = "MainCamera";
            }
            gameCamera.orthographic = true;
            gameCamera.orthographicSize = 5f;
            gameCamera.clearFlags = CameraClearFlags.SolidColor;
            gameCamera.backgroundColor = Ink.Surface;
            gameCamera.transform.position = new Vector3(0, 0, -10);
        }

        private void CreateStars()
        {
            for (var index = 0; index < 70; index++)
            {
                var star = MakePiece("Star", new Vector2(UnityEngine.Random.Range(-9f, 9f), UnityEngine.Random.Range(-5f, 5f)), new Vector2(UnityEngine.Random.Range(.015f, .045f), UnityEngine.Random.Range(.015f, .045f)), index % 3 == 0 ? Ink.Info : Ink.OnSurfaceVariant, -4);
                star.GetComponent<SpriteRenderer>().color = new Color(star.GetComponent<SpriteRenderer>().color.r, star.GetComponent<SpriteRenderer>().color.g, star.GetComponent<SpriteRenderer>().color.b, UnityEngine.Random.Range(.25f, .85f));
                stars.Add(star);
            }
        }

        private void LoadSettings()
        {
            bestScore = PlayerPrefs.GetInt(ScoreKey, 0);
            soundEnabled = PlayerPrefs.GetInt(SoundKey, 1) == 1;
            shakeEnabled = PlayerPrefs.GetInt(ShakeKey, 1) == 1;
            reducedMotion = PlayerPrefs.GetInt(MotionKey, 0) == 1;
            highContrast = PlayerPrefs.GetInt(ContrastKey, 0) == 1;
        }

        private void SaveSettings()
        {
            PlayerPrefs.SetInt(SoundKey, soundEnabled ? 1 : 0);
            PlayerPrefs.SetInt(ShakeKey, shakeEnabled ? 1 : 0);
            PlayerPrefs.SetInt(MotionKey, reducedMotion ? 1 : 0);
            PlayerPrefs.SetInt(ContrastKey, highContrast ? 1 : 0);
            PlayerPrefs.Save();
        }

        private void Update()
        {
            var dt = Mathf.Min(Time.unscaledDeltaTime, .034f);
            worldTime += dt * (state == State.Playing ? (reducedMotion ? .28f : 1f) : .25f);
            AnimateStars(dt);
            toastTimer -= dt;
            if (toastTimer <= 0f) toast = "";
            if (state == State.Playing)
            {
                TickPlay(dt);
                if (Input.GetKeyDown(KeyCode.P) || Input.GetKeyDown(KeyCode.Escape)) Pause();
            }
            else if (state == State.Paused && !showHelp && !showSettings && (Input.GetKeyDown(KeyCode.P) || Input.GetKeyDown(KeyCode.Escape))) Resume();
            gameCamera.backgroundColor = highContrast ? Color.black : Ink.Surface;
            if (shakeTimer > 0f)
            {
                shakeTimer -= dt;
                if (shakeEnabled && !reducedMotion)
                {
                    var offset = UnityEngine.Random.insideUnitCircle * .09f;
                    gameCamera.transform.localPosition = new Vector3(offset.x, offset.y, -10f);
                }
            }
            else gameCamera.transform.localPosition = new Vector3(0f, 0f, -10f);
        }

        private void AnimateStars(float dt)
        {
            var speed = state == State.Playing ? 1.8f : .22f;
            for (var index = 0; index < stars.Count; index++)
            {
                var star = stars[index];
                if (star == null) continue;
                star.transform.position += Vector3.left * dt * speed * (.35f + index % 5 * .17f);
                if (star.transform.position.x < -9.3f) star.transform.position = new Vector3(9.3f, UnityEngine.Random.Range(-5f, 5f), 4f);
            }
        }

        private void TickPlay(float dt)
        {
            stageTime += dt;
            playerFireTimer -= dt;
            missileTimer -= dt;
            playerInvulnerability = Mathf.Max(0f, playerInvulnerability - dt);
            shieldTimer = Mathf.Max(0f, shieldTimer - dt);
            var horizontal = (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) ? 1f : 0f) - (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) ? 1f : 0f);
            var vertical = (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow) ? 1f : 0f) - (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow) ? 1f : 0f);
            ReadTouchInput(ref horizontal, ref vertical);
            var input = new Vector2(horizontal, vertical).normalized;
            playerPosition += input * (5.4f + speedLevel * .75f) * dt;
            var halfWidth = gameCamera.orthographicSize * gameCamera.aspect;
            playerPosition.x = Mathf.Clamp(playerPosition.x, -halfWidth + .35f, halfWidth - .7f);
            playerPosition.y = Mathf.Clamp(playerPosition.y, -4.45f, 4.45f);
            if (player != null) player.transform.position = playerPosition;
            UpdateOptions();
            if (Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.J) || Input.GetMouseButton(0)) Fire();
            if (Input.GetKeyDown(KeyCode.LeftShift) || Input.GetKeyDown(KeyCode.RightShift) || Input.GetKeyDown(KeyCode.K)) ActivatePower();
            if (stageTime > 48f && !bossStarted) StartBoss();
            if (!bossStarted)
            {
                spawnTimer -= dt;
                if (spawnTimer <= 0f)
                {
                    SpawnWave();
                    spawnTimer = Mathf.Max(.34f, .98f - stageTime * .01f - (gardenMode ? .13f : 0f));
                }
            }
            UpdateEnemies(dt);
            UpdateShots(friendlyShots, dt, true);
            UpdateShots(enemyShots, dt, false);
            UpdateCapsules(dt);
            UpdateParticles(dt);
            CheckCollisions();
            if (bossDefeated)
            {
                clearTimer -= dt;
                if (clearTimer <= 0f) Finish(true);
            }
        }

        private void ReadTouchInput(ref float horizontal, ref float vertical)
        {
            if (!Application.isMobilePlatform) return;
            foreach (var touch in Input.touches)
            {
                if (touch.position.x < Screen.width * .45f)
                {
                    var center = new Vector2(Screen.width * .19f, Screen.height * .2f);
                    var delta = (touch.position - center).normalized;
                    horizontal = delta.x;
                    vertical = delta.y;
                }
                else if (touch.phase != TouchPhase.Ended && touch.phase != TouchPhase.Canceled) Fire();
            }
        }

        private void StartRun(bool garden)
        {
            ClearRunObjects();
            gardenMode = garden;
            state = State.Playing;
            score = 0;
            kills = 0;
            capsuleCount = 0;
            stageTime = 0f;
            spawnTimer = .75f;
            clearTimer = 0f;
            bossStarted = false;
            bossDefeated = false;
            lives = garden ? 5 : 3;
            playerHealth = 3;
            playerPosition = new Vector2(-5.7f, 0f);
            playerInvulnerability = 1.7f;
            shieldTimer = 0f;
            speedLevel = missileLevel = doubleLevel = laserLevel = optionLevel = 0;
            powerCursor = -1;
            player = CreatePlayer();
            status = "TEA GARDENへの降下を開始。時計うさぎを追え。";
            Beep(560f, .1f, .05f);
        }

        private void ClearRunObjects()
        {
            foreach (var actor in enemies) SafeDestroy(actor.Node);
            foreach (var shot in friendlyShots) SafeDestroy(shot.Node);
            foreach (var shot in enemyShots) SafeDestroy(shot.Node);
            foreach (var shot in capsules) SafeDestroy(shot.Node);
            foreach (var particle in particles) SafeDestroy(particle.Node);
            foreach (var option in options) SafeDestroy(option);
            enemies.Clear(); friendlyShots.Clear(); enemyShots.Clear(); capsules.Clear(); particles.Clear(); options.Clear();
            SafeDestroy(player);
            player = null;
        }

        private GameObject CreatePlayer()
        {
            var root = new GameObject("Alice Craft");
            root.transform.position = playerPosition;
            MakePiece("Hull", Vector2.zero, new Vector2(.75f, .38f), Ink.OnSurface, 1, root.transform);
            MakePiece("Heart Cockpit", new Vector2(.12f, 0f), new Vector2(.22f, .22f), Ink.Primary, 2, root.transform);
            MakePiece("Rabbit Ear", new Vector2(-.18f, .38f), new Vector2(.13f, .52f), Ink.Secondary, 1, root.transform);
            MakePiece("Rabbit Ear", new Vector2(-.02f, .38f), new Vector2(.13f, .52f), Ink.Secondary, 1, root.transform);
            MakePiece("Engine", new Vector2(-.48f, 0f), new Vector2(.28f, .16f), Ink.Success, 0, root.transform);
            return root;
        }

        private void UpdateOptions()
        {
            while (options.Count < optionLevel)
            {
                var option = new GameObject("Cheshire Option");
                MakePiece("Grin", Vector2.zero, new Vector2(.28f, .28f), Ink.Info, 2, option.transform);
                MakePiece("Eye", new Vector2(-.06f, .04f), new Vector2(.035f, .035f), Ink.Surface, 3, option.transform);
                MakePiece("Eye", new Vector2(.06f, .04f), new Vector2(.035f, .035f), Ink.Surface, 3, option.transform);
                options.Add(option);
            }
            while (options.Count > optionLevel) { SafeDestroy(options[options.Count - 1]); options.RemoveAt(options.Count - 1); }
            for (var index = 0; index < options.Count; index++)
            {
                var angle = worldTime * 2.1f + index * Mathf.PI * 2f / options.Count;
                options[index].transform.position = playerPosition + new Vector2(Mathf.Cos(angle) * (.65f + index * .1f), Mathf.Sin(angle) * (.48f + index * .07f));
            }
        }

        private void SpawnWave()
        {
            var roll = UnityEngine.Random.value;
            if (roll < .4f)
            {
                var y = UnityEngine.Random.Range(-3.9f, 3.9f);
                SpawnEnemy(EnemyKind.Card, new Vector2(8.8f, y));
                if (UnityEngine.Random.value < .45f) SpawnEnemy(EnemyKind.Card, new Vector2(10f, Mathf.Clamp(y + .8f, -4f, 4f)));
            }
            else if (roll < .72f) SpawnEnemy(EnemyKind.Hare, new Vector2(8.7f, UnityEngine.Random.Range(-3.6f, 3.6f)));
            else if (roll < .91f) SpawnEnemy(EnemyKind.Teapot, new Vector2(8.8f, UnityEngine.Random.Range(-3.5f, 3.5f)));
            else SpawnEnemy(EnemyKind.Cat, new Vector2(8.9f, UnityEngine.Random.Range(-3.3f, 3.3f)));
        }

        private void StartBoss()
        {
            bossStarted = true;
            SpawnEnemy(EnemyKind.Queen, new Vector2(10f, 0f));
            status = "警報：赤の女王がティーガーデンを封鎖した。";
            ShowToast("BOSS APPROACHING — RED QUEEN");
            Beep(180f, .16f, .08f);
        }

        private void SpawnEnemy(EnemyKind kind, Vector2 position)
        {
            var actor = new Actor { Kind = kind, Position = position, BaseY = position.y, Phase = UnityEngine.Random.Range(0f, 7f), Cooldown = UnityEngine.Random.Range(.65f, 1.35f) };
            switch (kind)
            {
                case EnemyKind.Card: actor.Radius = .26f; actor.Health = actor.MaxHealth = 2.8f; actor.Speed = 2.1f; actor.Score = 90; break;
                case EnemyKind.Hare: actor.Radius = .36f; actor.Health = actor.MaxHealth = 4.8f; actor.Speed = 1.6f; actor.Score = 175; break;
                case EnemyKind.Teapot: actor.Radius = .48f; actor.Health = actor.MaxHealth = 8.5f; actor.Speed = .92f; actor.Score = 300; break;
                case EnemyKind.Cat: actor.Radius = .58f; actor.Health = actor.MaxHealth = 14f; actor.Speed = 1.05f; actor.Score = 520; break;
                case EnemyKind.Queen: actor.Radius = 1.25f; actor.Health = actor.MaxHealth = 290f; actor.Speed = 1.4f; actor.Score = 8000; actor.Boss = true; actor.Cooldown = 1.6f; break;
            }
            actor.Node = CreateEnemyVisual(actor);
            actor.Sprite = actor.Node.GetComponentInChildren<SpriteRenderer>();
            enemies.Add(actor);
        }

        private GameObject CreateEnemyVisual(Actor actor)
        {
            var root = new GameObject(actor.Kind + " Enemy");
            root.transform.position = actor.Position;
            if (actor.Kind == EnemyKind.Card)
            {
                MakePiece("Card", Vector2.zero, new Vector2(.42f, .68f), Ink.Cream, 1, root.transform);
                MakePiece("Heart", Vector2.zero, new Vector2(.16f, .16f), Ink.Danger, 2, root.transform);
            }
            else if (actor.Kind == EnemyKind.Hare)
            {
                MakePiece("Hare", Vector2.zero, new Vector2(.62f, .48f), Ink.OnSurface, 1, root.transform);
                MakePiece("Ear", new Vector2(-.13f, .42f), new Vector2(.12f, .44f), Ink.Secondary, 2, root.transform);
                MakePiece("Ear", new Vector2(.13f, .42f), new Vector2(.12f, .44f), Ink.Secondary, 2, root.transform);
                MakePiece("Watch", new Vector2(.36f, -.13f), new Vector2(.19f, .19f), Ink.Primary, 2, root.transform);
            }
            else if (actor.Kind == EnemyKind.Teapot)
            {
                MakePiece("Pot", Vector2.zero, new Vector2(.78f, .56f), Ink.Secondary, 1, root.transform);
                MakePiece("Lid", new Vector2(0f, .34f), new Vector2(.35f, .14f), Ink.Cream, 2, root.transform);
                MakePiece("Spout", new Vector2(.56f, 0f), new Vector2(.36f, .18f), Ink.Danger, 1, root.transform);
            }
            else if (actor.Kind == EnemyKind.Cat)
            {
                MakePiece("Cat", Vector2.zero, new Vector2(.9f, .52f), Ink.Success, 1, root.transform);
                MakePiece("Ear", new Vector2(-.27f, .34f), new Vector2(.18f, .28f), Ink.Success, 2, root.transform);
                MakePiece("Ear", new Vector2(.27f, .34f), new Vector2(.18f, .28f), Ink.Success, 2, root.transform);
                MakePiece("Grin", new Vector2(0f, -.08f), new Vector2(.38f, .055f), Ink.Surface, 3, root.transform);
            }
            else
            {
                MakePiece("Queen Dress", new Vector2(0f, -.2f), new Vector2(1.75f, 1.65f), Ink.Danger, 1, root.transform);
                MakePiece("Queen Face", new Vector2(0f, .72f), new Vector2(.92f, .92f), Ink.Cream, 3, root.transform);
                MakePiece("Crown", new Vector2(0f, 1.35f), new Vector2(1.12f, .36f), Ink.Primary, 4, root.transform);
                MakePiece("Heart", new Vector2(0f, -.18f), new Vector2(.36f, .36f), Ink.Cream, 4, root.transform);
            }
            return root;
        }

        private void UpdateEnemies(float dt)
        {
            for (var index = enemies.Count - 1; index >= 0; index--)
            {
                var enemy = enemies[index];
                enemy.Phase += dt;
                if (enemy.Kind == EnemyKind.Card) { enemy.Position += Vector2.left * enemy.Speed * dt; enemy.Position.y += Mathf.Sin(enemy.Phase * 3f) * .45f * dt; }
                else if (enemy.Kind == EnemyKind.Hare) { enemy.Position.x -= enemy.Speed * dt; enemy.Position.y = enemy.BaseY + Mathf.Sin(enemy.Phase * 2.5f) * .9f; }
                else if (enemy.Kind == EnemyKind.Teapot) { enemy.Position.x -= enemy.Speed * dt; enemy.Position.y += Mathf.Sin(enemy.Phase * 2f) * .16f * dt; }
                else if (enemy.Kind == EnemyKind.Cat) { enemy.Position.x -= enemy.Speed * dt; enemy.Position.y = enemy.BaseY + Mathf.Sin(enemy.Phase * 1.7f) * 1.1f; }
                else if (enemy.Kind == EnemyKind.Queen)
                {
                    if (!enemy.Entered) { enemy.Position.x -= enemy.Speed * dt; if (enemy.Position.x < 5.8f) enemy.Entered = true; }
                    else { enemy.Position.x = 5.8f + Mathf.Sin(enemy.Phase * .65f) * .32f; enemy.Position.y = Mathf.Sin(enemy.Phase * 1.25f) * 2.0f; }
                }
                enemy.Node.transform.position = enemy.Position;
                enemy.Cooldown -= dt;
                if (enemy.Cooldown <= 0f && (enemy.Position.x < 8.2f || enemy.Boss))
                {
                    EnemyFire(enemy);
                    enemy.Cooldown = enemy.Boss ? Mathf.Lerp(.88f, .5f, 1f - enemy.Health / enemy.MaxHealth) : (enemy.Kind == EnemyKind.Teapot ? 1.65f : 1.24f);
                }
                if (enemy.Position.x < -9.5f || enemy.Health <= 0f) RemoveEnemy(index, false);
            }
        }

        private void EnemyFire(Actor enemy)
        {
            var direction = (playerPosition - enemy.Position).normalized;
            if (enemy.Kind == EnemyKind.Card) return;
            if (enemy.Kind == EnemyKind.Hare) { SpawnShot(false, enemy.Position, direction * 4.5f, .12f, 1f, ShotKind.Enemy, Ink.Primary); return; }
            if (enemy.Kind == EnemyKind.Teapot)
            {
                for (var index = -1; index <= 1; index++) SpawnShot(false, enemy.Position, Rotate(direction, index * 16f) * 3.7f, .16f, 1f, ShotKind.Enemy, Ink.Secondary);
                return;
            }
            if (enemy.Kind == EnemyKind.Cat)
            {
                for (var index = -2; index <= 2; index++) SpawnShot(false, enemy.Position, Rotate(direction, index * 12f) * 3.3f, .13f, 1f, ShotKind.Enemy, Ink.Success);
                return;
            }
            enemy.Pattern++;
            if (enemy.Pattern % 3 == 0)
            {
                for (var index = 0; index < 12; index++)
                {
                    var angle = index * 30f + enemy.Phase * 55f;
                    SpawnShot(false, enemy.Position, new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad)) * 3.2f + Vector2.left, .17f, 1f, ShotKind.Heart, index % 2 == 0 ? Ink.Danger : Ink.Secondary);
                }
            }
            else for (var index = -2; index <= 2; index++) SpawnShot(false, enemy.Position + Vector2.left * .5f, Rotate(direction, index * 12f) * 4.8f, .18f, 1f, ShotKind.Heart, Ink.Danger);
        }

        private void Fire()
        {
            if (playerFireTimer > 0f) return;
            playerFireTimer = Mathf.Max(.08f, .19f - speedLevel * .02f);
            var origin = playerPosition + Vector2.right * .45f;
            SpawnShot(true, origin, Vector2.right * 11f, laserLevel > 0 ? .11f : .08f, laserLevel > 0 ? 2.6f : 1.2f, laserLevel > 0 ? ShotKind.Laser : ShotKind.Tea, laserLevel > 0 ? Ink.Success : Ink.Primary, laserLevel > 0 ? 2 : 0);
            if (doubleLevel > 0) { SpawnShot(true, origin, new Vector2(10.5f, 1.8f), .07f, 1f, ShotKind.Double, Ink.Secondary); SpawnShot(true, origin, new Vector2(10.5f, -1.8f), .07f, 1f, ShotKind.Double, Ink.Secondary); }
            for (var index = 0; index < options.Count; index++) SpawnShot(true, options[index].transform.position + Vector3.right * .18f, Vector2.right * 10f, .065f, .85f, ShotKind.Tea, Ink.Info);
            if (missileLevel > 0 && missileTimer <= 0f) { missileTimer = Mathf.Max(.3f, .75f - missileLevel * .17f); SpawnShot(true, origin + Vector2.down * .18f, new Vector2(6.5f, -1.2f), .13f, 2.5f, ShotKind.Missile, Ink.Danger); }
            Beep(420f, .035f, .025f);
        }

        private void SpawnShot(bool friendly, Vector2 position, Vector2 velocity, float radius, float damage, ShotKind kind, Color color, int pierce = 0)
        {
            var node = MakePiece(kind.ToString(), position, Vector2.one * radius * 2f, color, friendly ? 3 : 2);
            var shot = new Shot { Node = node, Sprite = node.GetComponent<SpriteRenderer>(), Position = position, Velocity = velocity, Radius = radius, Damage = damage, Lifetime = 4f, Kind = kind, Pierce = pierce };
            if (friendly) friendlyShots.Add(shot); else enemyShots.Add(shot);
        }

        private void UpdateShots(List<Shot> shots, float dt, bool friendly)
        {
            for (var index = shots.Count - 1; index >= 0; index--)
            {
                var shot = shots[index];
                shot.Lifetime -= dt;
                if (shot.Kind == ShotKind.Missile)
                {
                    Actor target = null;
                    var nearest = float.MaxValue;
                    foreach (var enemy in enemies)
                    {
                        if (enemy.Position.x < shot.Position.x - .3f) continue;
                        var d = Vector2.Distance(shot.Position, enemy.Position);
                        if (d < nearest) { nearest = d; target = enemy; }
                    }
                    if (target != null) shot.Velocity += (target.Position - shot.Position).normalized * 7f * dt;
                }
                shot.Position += shot.Velocity * dt;
                shot.Node.transform.position = shot.Position;
                if (shot.Lifetime <= 0f || Mathf.Abs(shot.Position.x) > 10f || Mathf.Abs(shot.Position.y) > 6f) RemoveShot(shots, index);
            }
        }

        private void UpdateCapsules(float dt)
        {
            for (var index = capsules.Count - 1; index >= 0; index--)
            {
                var capsule = capsules[index];
                capsule.Position += new Vector2(-1.65f, Mathf.Sin(worldTime * 4f + index) * .26f) * dt;
                capsule.Node.transform.position = capsule.Position;
                capsule.Node.transform.Rotate(0f, 0f, 120f * dt);
                if (capsule.Position.x < -9f) RemoveShot(capsules, index);
            }
        }

        private void CheckCollisions()
        {
            for (var shotIndex = friendlyShots.Count - 1; shotIndex >= 0; shotIndex--)
            {
                var shot = friendlyShots[shotIndex];
                for (var enemyIndex = enemies.Count - 1; enemyIndex >= 0; enemyIndex--)
                {
                    var enemy = enemies[enemyIndex];
                    if (Vector2.Distance(shot.Position, enemy.Position) > shot.Radius + enemy.Radius) continue;
                    enemy.Health -= shot.Damage;
                    SpawnParticles(shot.Position, 3, shot.Sprite.color, .8f);
                    if (shot.Pierce > 0) shot.Pierce--; else { RemoveShot(friendlyShots, shotIndex); break; }
                    if (enemy.Health <= 0f) RemoveEnemy(enemyIndex, true);
                }
            }
            for (var shotIndex = enemyShots.Count - 1; shotIndex >= 0; shotIndex--)
            {
                var shot = enemyShots[shotIndex];
                if (Vector2.Distance(shot.Position, playerPosition) <= shot.Radius + .31f) { RemoveShot(enemyShots, shotIndex); DamagePlayer(); }
            }
            for (var index = enemies.Count - 1; index >= 0; index--) if (!enemies[index].Boss && Vector2.Distance(enemies[index].Position, playerPosition) < enemies[index].Radius + .3f) { RemoveEnemy(index, true); DamagePlayer(); }
            for (var index = capsules.Count - 1; index >= 0; index--) if (Vector2.Distance(capsules[index].Position, playerPosition) < .52f) { RemoveShot(capsules, index); CollectCapsule(); }
        }

        private void RemoveEnemy(int index, bool destroyed)
        {
            if (index < 0 || index >= enemies.Count) return;
            var enemy = enemies[index];
            enemies.RemoveAt(index);
            SafeDestroy(enemy.Node);
            if (!destroyed) return;
            kills++;
            score += enemy.Score;
            SpawnParticles(enemy.Position, enemy.Boss ? 54 : 18, enemy.Boss ? Ink.Secondary : Ink.Primary, enemy.Boss ? 2.5f : 1.5f);
            Beep(80f, .14f, .06f);
            shakeTimer = enemy.Boss ? .55f : .15f;
            if (!enemy.Boss && UnityEngine.Random.value < (enemy.Kind == EnemyKind.Teapot ? .52f : .2f))
            {
                var capsule = new Shot { Position = enemy.Position, Radius = .21f, Kind = ShotKind.Tea, Node = MakePiece("Tea Capsule", enemy.Position, new Vector2(.29f, .29f), Ink.Primary, 3) };
                capsules.Add(capsule);
            }
            if (enemy.Boss)
            {
                bossDefeated = true;
                clearTimer = 2.4f;
                status = "女王の冠が砕けた。出口が見える！";
                UnlockMemento("CROWN BREAKER");
                Beep(900f, .25f, .08f);
            }
        }

        private void DamagePlayer()
        {
            if (playerInvulnerability > 0f) return;
            if (shieldTimer > 0f)
            {
                shieldTimer = Mathf.Max(0f, shieldTimer - 2.8f);
                playerInvulnerability = .5f;
                status = "ティーカップの盾が攻撃を弾いた！";
                SpawnParticles(playerPosition, 12, Ink.Info, 1.4f);
                return;
            }
            playerHealth--;
            playerInvulnerability = 2.1f;
            shakeTimer = .35f;
            SpawnParticles(playerPosition, 22, Ink.Secondary, 1.8f);
            Beep(75f, .18f, .07f);
            if (playerHealth > 0) return;
            lives--;
            if (lives <= 0) { Finish(false); return; }
            playerHealth = 3;
            playerPosition = new Vector2(-5.7f, 0f);
            status = "アリス・クラフトを雲から回収。飛行再開！";
        }

        private void CollectCapsule()
        {
            capsuleCount++;
            score += 250;
            powerCursor = Mathf.Min(5, powerCursor + 1);
            status = "TEA CAPSULE を獲得。SHIFTで強化を選択。";
            ShowToast("POWER READY — " + PowerNames()[powerCursor]);
            SpawnParticles(playerPosition, 14, Ink.Primary, 1.4f);
            Beep(720f, .08f, .045f);
            if (capsuleCount >= 7) UnlockMemento("CURIOUS COLLECTOR");
        }

        private void ActivatePower()
        {
            if (state != State.Playing) return;
            if (powerCursor < 0) { ShowToast("先に TEA CAPSULE を集めよう"); return; }
            switch (powerCursor)
            {
                case 0: speedLevel = Mathf.Min(3, speedLevel + 1); break;
                case 1: missileLevel = Mathf.Min(2, missileLevel + 1); break;
                case 2: doubleLevel = 1; break;
                case 3: laserLevel = 1; break;
                case 4: optionLevel = Mathf.Min(3, optionLevel + 1); break;
                case 5: shieldTimer = 12f; break;
            }
            status = PowerNames()[powerCursor] + " を発動。もっと深く落ちていこう。";
            ShowToast(PowerNames()[powerCursor] + " ACTIVATED");
            powerCursor = -1;
            SpawnParticles(playerPosition, 20, Ink.Success, 1.9f);
            Beep(520f, .16f, .065f);
            if (optionLevel >= 3) UnlockMemento("THREE GRINS");
            if (laserLevel > 0 && missileLevel > 0 && optionLevel >= 2) UnlockMemento("FULL TEA SET");
        }

        private void Finish(bool won)
        {
            state = State.Results;
            bestScore = Mathf.Max(bestScore, score);
            PlayerPrefs.SetInt(ScoreKey, bestScore);
            PlayerPrefs.Save();
            status = won ? "THE LOOKING GLASS OPENS" : "THE QUEEN'S DECREE";
        }

        private void Pause() { state = State.Paused; status = "THE CLOCK HAS PAUSED"; }
        private void Resume() { state = State.Playing; status = "飛行を再開。ティーカップを傾けろ。"; }
        private void ShowToast(string message) { toast = message; toastTimer = 2.3f; }
        private void UnlockMemento(string memento)
        {
            var current = PlayerPrefs.GetString(MementoKey, "");
            if (current.Contains(memento)) return;
            PlayerPrefs.SetString(MementoKey, current + memento + "|");
            PlayerPrefs.Save();
            ShowToast("MEMENTO: " + memento);
        }

        private void SpawnParticles(Vector2 position, int count, Color color, float speed)
        {
            if (reducedMotion) count = Mathf.Max(3, count / 4);
            for (var index = 0; index < count; index++)
            {
                var node = MakePiece("Spark", position, Vector2.one * UnityEngine.Random.Range(.035f, .09f), color, 5);
                particles.Add(new Particle { Node = node, Sprite = node.GetComponent<SpriteRenderer>(), Velocity = UnityEngine.Random.insideUnitCircle * speed, Lifetime = UnityEngine.Random.Range(.25f, .65f), Scale = node.transform.localScale.x });
            }
        }

        private void UpdateParticles(float dt)
        {
            for (var index = particles.Count - 1; index >= 0; index--)
            {
                var particle = particles[index];
                particle.Lifetime -= dt;
                particle.Node.transform.position += (Vector3)(particle.Velocity * dt);
                particle.Node.transform.localScale = Vector3.one * particle.Scale * Mathf.Clamp01(particle.Lifetime * 2f);
                if (particle.Lifetime <= 0f) { SafeDestroy(particle.Node); particles.RemoveAt(index); }
            }
        }

        private void Beep(float frequency, float seconds, float volume)
        {
            if (!soundEnabled || audioSource == null) return;
            var sampleRate = 22050;
            var length = Mathf.CeilToInt(sampleRate * seconds);
            var samples = new float[length];
            for (var index = 0; index < length; index++)
            {
                var envelope = 1f - (float)index / length;
                samples[index] = Mathf.Sin(index * Mathf.PI * 2f * frequency / sampleRate) * envelope * volume;
            }
            var clip = AudioClip.Create("Clockwork Beep", length, 1, sampleRate, false);
            clip.SetData(samples, 0);
            audioSource.PlayOneShot(clip);
            Destroy(clip, seconds + .1f);
        }

        private static Vector2 Rotate(Vector2 vector, float degrees)
        {
            var radians = degrees * Mathf.Deg2Rad;
            return new Vector2(vector.x * Mathf.Cos(radians) - vector.y * Mathf.Sin(radians), vector.x * Mathf.Sin(radians) + vector.y * Mathf.Cos(radians));
        }

        private GameObject MakePiece(string name, Vector2 position, Vector2 scale, Color color, int order, Transform parent = null)
        {
            var node = new GameObject(name);
            if (parent != null) node.transform.SetParent(parent, false);
            node.transform.localPosition = parent == null ? new Vector3(position.x, position.y, 0f) : new Vector3(position.x, position.y, 0f);
            node.transform.localScale = new Vector3(scale.x, scale.y, 1f);
            var renderer = node.AddComponent<SpriteRenderer>();
            renderer.sprite = squareSprite;
            renderer.color = color;
            renderer.sortingOrder = order;
            return node;
        }

        private static void SafeDestroy(GameObject node) { if (node != null) UnityEngine.Object.Destroy(node); }
        private static void RemoveShot(List<Shot> shots, int index) { if (index < 0 || index >= shots.Count) return; SafeDestroy(shots[index].Node); shots.RemoveAt(index); }
        private string[] PowerNames() { return new[] { "SPEED", "MISSILE", "DOUBLE", "LASER", "OPTION", "SHIELD" }; }

        private void OnGUI()
        {
            var scale = Mathf.Clamp(Screen.width / 1280f, .65f, 1.35f);
            var previous = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(Vector3.one * scale);
            var width = Screen.width / scale;
            var height = Screen.height / scale;
            if (state == State.Playing || state == State.Paused || state == State.Results) DrawHud(width, height);
            if (state == State.Title) DrawTitle(width, height);
            if (state == State.Paused) DrawPause(width, height);
            if (state == State.Results) DrawResults(width, height);
            if (showHelp) DrawHelp(width, height);
            if (showSettings) DrawSettings(width, height);
            if (!string.IsNullOrEmpty(toast)) DrawToast(width, height, toast);
            GUI.matrix = previous;
        }

        private void DrawTitle(float width, float height)
        {
            DrawRect(new Rect(0f, 0f, width, height), new Color(0f, 0f, .08f, .38f));
            var panel = CenteredPanel(width, height, 575f, 420f);
            DrawPanel(panel);
            Label(new Rect(panel.x, panel.y + 28, panel.width, 24), "STAGE 01 / TEA GARDEN", 13, Ink.OnSurfaceVariant, TextAnchor.MiddleCenter, FontStyle.Bold);
            Label(new Rect(panel.x, panel.y + 59, panel.width, 95), "時計うさぎを\n追いかけて。", 43, Ink.OnSurface, TextAnchor.MiddleCenter, FontStyle.Normal);
            Label(new Rect(panel.x + 50, panel.y + 163, panel.width - 100, 45), "空飛ぶハートのチェス駒で、赤の女王が支配する\nティーパーティーから出口を探そう。", 15, Ink.OnSurfaceVariant, TextAnchor.MiddleCenter, FontStyle.Normal);
            if (Button(new Rect(panel.x + 47, panel.y + 226, 228, 55), "STORY FLIGHT\n標準 · 残機3", !gardenMode)) gardenMode = false;
            if (Button(new Rect(panel.x + 300, panel.y + 226, 228, 55), "GARDEN FLIGHT\nゆったり · 残機5", gardenMode)) gardenMode = true;
            if (ActionButton(new Rect(panel.x + 47, panel.y + 302, panel.width - 94, 52), "▶  FLY INTO WONDERLAND")) StartRun(gardenMode);
            Label(new Rect(panel.x, panel.y + 370, panel.width, 24), "移動 WASD / 矢印　 撃つ SPACE　 強化 SHIFT", 12, Ink.OnSurfaceVariant, TextAnchor.MiddleCenter, FontStyle.Bold);
            TopButtons(width, height);
        }

        private void DrawHud(float width, float height)
        {
            DrawRect(new Rect(15, 15, width - 30, 58), new Color(Ink.SurfaceRaised.r, Ink.SurfaceRaised.g, Ink.SurfaceRaised.b, .92f));
            Label(new Rect(30, 22, 170, 17), "SCORE", 11, Ink.OnSurfaceVariant, TextAnchor.UpperLeft, FontStyle.Bold);
            Label(new Rect(30, 39, 170, 24), score.ToString("000000"), 20, Ink.Primary, TextAnchor.UpperLeft, FontStyle.Bold);
            Label(new Rect(215, 22, 170, 17), "BEST", 11, Ink.OnSurfaceVariant, TextAnchor.UpperLeft, FontStyle.Bold);
            Label(new Rect(215, 39, 170, 24), bestScore.ToString("000000"), 20, Ink.Primary, TextAnchor.UpperLeft, FontStyle.Bold);
            Label(new Rect(width * .43f, 25, 240, 20), bossStarted ? "STAGE 01 · RED QUEEN" : "STAGE 01 · TEA GARDEN", 14, Ink.OnSurface, TextAnchor.MiddleCenter, FontStyle.Bold);
            Label(new Rect(width - 210, 22, 170, 17), "HEARTS", 11, Ink.OnSurfaceVariant, TextAnchor.UpperRight, FontStyle.Bold);
            Label(new Rect(width - 210, 39, 170, 24), new string('♥', Mathf.Max(lives, 0)), 20, Ink.Secondary, TextAnchor.UpperRight, FontStyle.Bold);
            if (bossStarted && !bossDefeated)
            {
                var queen = enemies.Find(actor => actor.Boss);
                if (queen != null)
                {
                    DrawRect(new Rect(width * .28f, 82, width * .44f, 8), new Color(.25f, .08f, .19f, .96f));
                    DrawRect(new Rect(width * .28f, 82, width * .44f * Mathf.Clamp01(queen.Health / queen.MaxHealth), 8), Ink.Danger);
                    Label(new Rect(width * .28f, 62, width * .44f, 18), "RED QUEEN // HEARTS DECREE", 11, Ink.Secondary, TextAnchor.MiddleCenter, FontStyle.Bold);
                }
            }
            DrawPower(width, height);
            Label(new Rect(22, height - 46, width - 44, 22), status, 13, Ink.Primary, TextAnchor.MiddleCenter, FontStyle.Bold);
            if (Button(new Rect(width - 150, height - 85, 60, 28), "?", false)) OpenHelp();
            if (Button(new Rect(width - 82, height - 85, 60, 28), "⚙", false)) OpenSettings();
        }

        private void DrawPower(float width, float height)
        {
            var names = PowerNames();
            var panel = new Rect(width * .17f, height - 99, width * .66f, 39);
            DrawRect(panel, new Color(Ink.Surface.r, Ink.Surface.g, Ink.Surface.b, .94f));
            var cellWidth = panel.width / names.Length;
            for (var index = 0; index < names.Length; index++)
            {
                var ready = index == powerCursor;
                var owned = (index == 0 && speedLevel > 0) || (index == 1 && missileLevel > 0) || (index == 2 && doubleLevel > 0) || (index == 3 && laserLevel > 0) || (index == 4 && optionLevel > 0) || (index == 5 && shieldTimer > 0f);
                var color = ready ? Ink.Primary : owned ? Ink.PrimaryContainer : Ink.SurfaceRaised;
                DrawRect(new Rect(panel.x + index * cellWidth + 2, panel.y + 3, cellWidth - 4, panel.height - 6), color);
                Label(new Rect(panel.x + index * cellWidth, panel.y + 11, cellWidth, 17), names[index], 10, ready ? Ink.OnPrimary : Ink.OnSurface, TextAnchor.MiddleCenter, FontStyle.Bold);
            }
            if (ActionButton(new Rect(panel.x + panel.width + 8, panel.y, 117, 39), "SHIFT / K\nPOWER")) ActivatePower();
        }

        private void DrawPause(float width, float height)
        {
            DrawRect(new Rect(0f, 0f, width, height), new Color(0f, 0f, 0f, .56f));
            var panel = CenteredPanel(width, height, 390, 245);
            DrawPanel(panel);
            Label(new Rect(panel.x, panel.y + 37, panel.width, 24), "THE CLOCK HAS PAUSED", 13, Ink.OnSurfaceVariant, TextAnchor.MiddleCenter, FontStyle.Bold);
            Label(new Rect(panel.x, panel.y + 76, panel.width, 52), "ひとやすみ。", 36, Ink.OnSurface, TextAnchor.MiddleCenter, FontStyle.Normal);
            if (ActionButton(new Rect(panel.x + 36, panel.y + 145, panel.width - 72, 43), "▶ RESUME FLIGHT")) Resume();
            if (Button(new Rect(panel.x + 36, panel.y + 195, panel.width - 72, 32), "TITLE SCREEN", false)) { ClearRunObjects(); state = State.Title; }
        }

        private void DrawResults(float width, float height)
        {
            DrawRect(new Rect(0f, 0f, width, height), new Color(0f, 0f, .08f, .54f));
            var panel = CenteredPanel(width, height, 490, 350);
            DrawPanel(panel);
            var victory = bossDefeated;
            Label(new Rect(panel.x, panel.y + 30, panel.width, 22), victory ? "THE LOOKING GLASS OPENS" : "THE QUEEN'S DECREE", 13, Ink.OnSurfaceVariant, TextAnchor.MiddleCenter, FontStyle.Bold);
            Label(new Rect(panel.x, panel.y + 62, panel.width, 55), victory ? "夢は、まだ続く。" : "首を…はねないで。", 35, Ink.OnSurface, TextAnchor.MiddleCenter, FontStyle.Normal);
            Label(new Rect(panel.x + 44, panel.y + 120, panel.width - 88, 40), victory ? "女王の命令は砕け、ティーガーデンに風が戻った。" : "強化の順番を考えて、もう一度お茶会へ。", 14, Ink.OnSurfaceVariant, TextAnchor.MiddleCenter, FontStyle.Normal);
            Label(new Rect(panel.x + 40, panel.y + 179, panel.width - 80, 25), "SCORE " + score.ToString("000000") + "    CARDS " + kills + "    CAPSULES " + capsuleCount, 14, Ink.Primary, TextAnchor.MiddleCenter, FontStyle.Bold);
            if (ActionButton(new Rect(panel.x + 43, panel.y + 237, panel.width - 86, 43), "▶ FLY AGAIN")) StartRun(gardenMode);
            if (Button(new Rect(panel.x + 43, panel.y + 288, panel.width - 86, 31), "TITLE SCREEN", false)) { ClearRunObjects(); state = State.Title; }
        }

        private void DrawHelp(float width, float height)
        {
            DrawRect(new Rect(0f, 0f, width, height), new Color(0f, 0f, 0f, .6f));
            var panel = CenteredPanel(width, height, 650, 380); DrawPanel(panel);
            Label(new Rect(panel.x + 22, panel.y + 24, panel.width - 44, 30), "飛行マニュアル", 28, Ink.OnSurface, TextAnchor.MiddleLeft, FontStyle.Normal);
            Label(new Rect(panel.x + 30, panel.y + 80, panel.width - 60, 175), "✦ 撃つ　SPACE / J。押し続けると連射。\n\n◈ 強化　カプセルでメーターを進め、SHIFT / Kで発動。\n\n♘ オプション　チェシャ猫はあなたと一緒にショットを撃つ。\n\n⌁ 停止　P / ESC。設定から動きと画面揺れも調整できる。", 16, Ink.OnSurfaceVariant, TextAnchor.UpperLeft, FontStyle.Normal);
            if (Button(new Rect(panel.x + panel.width - 115, panel.y + 310, 82, 35), "CLOSE", false)) showHelp = false;
        }

        private void DrawSettings(float width, float height)
        {
            DrawRect(new Rect(0f, 0f, width, height), new Color(0f, 0f, 0f, .6f));
            var panel = CenteredPanel(width, height, 500, 410); DrawPanel(panel);
            Label(new Rect(panel.x + 26, panel.y + 25, panel.width - 52, 32), "設定", 28, Ink.OnSurface, TextAnchor.MiddleLeft, FontStyle.Normal);
            var row = panel.y + 81;
            soundEnabled = Toggle(new Rect(panel.x + 31, row, panel.width - 62, 43), soundEnabled, "効果音", "ショット・被弾などの音を鳴らす");
            shakeEnabled = Toggle(new Rect(panel.x + 31, row + 53, panel.width - 62, 43), shakeEnabled, "画面の揺れ", "爆発時のカメラシェイク");
            reducedMotion = Toggle(new Rect(panel.x + 31, row + 106, panel.width - 62, 43), reducedMotion, "動きを抑える", "背景と粒子の動きを穏やかにする");
            highContrast = Toggle(new Rect(panel.x + 31, row + 159, panel.width - 62, 43), highContrast, "高コントラスト", "HUDと照準をくっきり表示する");
            if (Button(new Rect(panel.x + panel.width - 115, panel.y + 357, 82, 32), "CLOSE", false)) { SaveSettings(); showSettings = false; }
        }

        private void TopButtons(float width, float height)
        {
            if (Button(new Rect(width - 170, 25, 65, 30), "? HELP", false)) OpenHelp();
            if (Button(new Rect(width - 96, 25, 70, 30), "⚙ SET", false)) OpenSettings();
        }

        private void DrawToast(float width, float height, string value)
        {
            var rect = new Rect(width * .5f - 180, height * .18f, 360, 35);
            DrawRect(rect, new Color(Ink.SurfaceRaised.r, Ink.SurfaceRaised.g, Ink.SurfaceRaised.b, .96f));
            Label(rect, value, 13, Ink.Primary, TextAnchor.MiddleCenter, FontStyle.Bold);
        }

        private void OpenHelp()
        {
            if (state == State.Playing) Pause();
            showHelp = true;
        }

        private void OpenSettings()
        {
            if (state == State.Playing) Pause();
            showSettings = true;
        }

        private void DrawPanel(Rect rect)
        {
            DrawRect(rect, new Color(Ink.SurfaceRaised.r, Ink.SurfaceRaised.g, Ink.SurfaceRaised.b, .97f));
            DrawRect(new Rect(rect.x, rect.y, rect.width, 2), Ink.Primary);
        }

        private static Rect CenteredPanel(float width, float height, float panelWidth, float panelHeight) { return new Rect((width - panelWidth) * .5f, (height - panelHeight) * .5f, panelWidth, panelHeight); }
        private static void DrawRect(Rect rect, Color color) { var previous = GUI.color; GUI.color = color; GUI.DrawTexture(rect, Texture2D.whiteTexture); GUI.color = previous; }
        private static void Label(Rect rect, string text, int fontSize, Color color, TextAnchor alignment, FontStyle style)
        {
            var guiStyle = new GUIStyle(GUI.skin.label) { fontSize = fontSize, alignment = alignment, fontStyle = style, wordWrap = true, normal = { textColor = color } };
            GUI.Label(rect, text, guiStyle);
        }
        private static bool Button(Rect rect, string text, bool selected)
        {
            var style = new GUIStyle(GUI.skin.button) { fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = true, normal = { textColor = Ink.OnSurface, background = Texture2D.whiteTexture }, hover = { textColor = Ink.OnSurface, background = Texture2D.whiteTexture }, active = { textColor = Ink.OnPrimary, background = Texture2D.whiteTexture } };
            var previous = GUI.color; GUI.color = selected ? Ink.PrimaryContainer : Ink.SurfaceRaised; var result = GUI.Button(rect, text, style); GUI.color = previous; return result;
        }
        private static bool ActionButton(Rect rect, string text)
        {
            var style = new GUIStyle(GUI.skin.button) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Ink.OnPrimary, background = Texture2D.whiteTexture }, hover = { textColor = Ink.OnPrimary, background = Texture2D.whiteTexture } };
            var previous = GUI.color; GUI.color = Ink.Primary; var result = GUI.Button(rect, text, style); GUI.color = previous; return result;
        }
        private static bool Toggle(Rect rect, bool value, string title, string detail)
        {
            DrawRect(rect, Ink.Surface);
            var toggled = GUI.Toggle(new Rect(rect.x + 12, rect.y + 9, 22, 22), value, "");
            Label(new Rect(rect.x + 45, rect.y + 5, rect.width - 55, 18), title, 14, Ink.OnSurface, TextAnchor.UpperLeft, FontStyle.Bold);
            Label(new Rect(rect.x + 45, rect.y + 23, rect.width - 55, 17), detail, 11, Ink.OnSurfaceVariant, TextAnchor.UpperLeft, FontStyle.Normal);
            return toggled;
        }
    }
}
