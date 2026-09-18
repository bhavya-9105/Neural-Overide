using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public enum State
{
    MainMenu,
    Starting = MainMenu,
    Playing,
    Paused,
    GameOver,
    Victory
}

[RequireComponent(typeof(AudioSource))]
public class GameManager : MonoBehaviour
{
    public enum Difficulty
    {
        Easy,
        Normal,
        Expert
    }

    public static GameManager Instance { get; private set; }
    public static int? PendingDifficultyIndex = null;

    [Header("Game State")]
    public State currentState = State.Starting;

    [Header("Difficulty Mode")]
    private int currentLevelIndex = 0;
    public int CurrentLevelIndex { get => currentLevelIndex; set => currentLevelIndex = value; }
    public float enemySpawnRate = 3.0f;
    public float EnemySpawnRate { get => enemySpawnRate; set => enemySpawnRate = value; }
    public int currentDifficulty = 0; // 0 = Easy (20s), 1 = Normal (50s), 2 = Expert/Hard (90s)
    public float survivalDuration = 20f;
    public float baseEnemySpeedMultiplier = 0.75f;
    public Difficulty CurrentDifficultyEnum => (Difficulty)Mathf.Clamp(currentDifficulty, 0, 2);

    [Header("Survival Timer")]
    public float survivalTimeRemaining = 20f;
    public float survivalTimer
    {
        get => survivalTimeRemaining;
        set
        {
            survivalTimeRemaining = value;
            survivalDuration = value;
            totalSurvivalTime = value;
        }
    }

    [Header("Score & Combo")]
    public int currentScore = 0;
    public int comboCount = 0;
    public float comboTimer = 0f;
    public float comboWindow = 2.0f;
    public int currentMultiplier = 1;

    [Header("Canvas UI References")]
    [SerializeField] private GameObject startPanel;
    [SerializeField] private GameObject gameOverPanel;
    public GameObject victoryPanel;
    [SerializeField] private TextMeshProUGUI timerText;
    [SerializeField] private TextMeshProUGUI scoreText;
    [SerializeField] private TextMeshProUGUI comboText;
    [SerializeField] private TextMeshProUGUI gameOverScoreText;
    [SerializeField] private TextMeshProUGUI victoryScoreText;

    [Header("HUD & Pause References")]
    [SerializeField] private GameObject hudPanel;
    public GameObject pausePanel;
    [SerializeField] private TextMeshProUGUI gameOverStatsText;
    [SerializeField] private TextMeshProUGUI victoryStatsText;

    [Header("State Tracking")]
    public bool isPaused = false;
    public bool isGameActive = false;
    public int currentLevel = 1;

    [Header("Telemetry Tracking")]
    public int enemiesEliminated = 0;
    public float totalSurvivalTime = 20f;
    public float timeSurvived = 0f;

    [Header("Start Panel UI References")]
    [SerializeField] private Image startBtnImage;
    [SerializeField] private TextMeshProUGUI startBtnLabel;
    [SerializeField] private HealthBarUI healthBarUI;

    [Header("Difficulty Mode UI References")]
    public Button easyButton;
    public Button normalButton;
    public Button expertButton;
    public Image easyBtnImage;
    public Image normalBtnImage;
    public Image expertBtnImage;

    [Header("Victory Navigation UI References")]
    public GameObject nextLevelBtn; // Reference to the button on VictoryPanel
    public Button nextLevelButton;
    public TextMeshProUGUI nextLevelButtonText;
    public Button replayLevelButton;
    public TextMeshProUGUI victoryTitleText;

    [Header("Audio Settings")]
    public AudioSource audioSource;
    public AudioSource shootAudioSource;
    public AudioSource hitAudioSource;
    public AudioClip shotSFX;
    public AudioClip enemyHitSFX;
    public AudioClip playerHurtSFX;
    public AudioClip victorySFX;
    public AudioClip defeatSFX;

    // Public properties/accessors for editor configuration and external scripts
    public float enemySpeedMultiplier
    {
        get
        {
            switch (currentDifficulty)
            {
                case 0: return 0.75f;
                case 1: return 1.0f;
                case 2: return 1.35f;
                default: return baseEnemySpeedMultiplier;
            }
        }
    }
    public float EnemySpeedMultiplier => enemySpeedMultiplier;

    public float SurvivalTimeRemaining { get => survivalTimeRemaining; set => survivalTimeRemaining = value; }
    public int CurrentScore { get => currentScore; set => currentScore = value; }
    public State CurrentState { get => currentState; set => currentState = value; }

    public GameObject NextLevelBtn { get => nextLevelBtn; set => nextLevelBtn = value; }
    public Button NextLevelButton { get => nextLevelButton; set => nextLevelButton = value; }
    public TextMeshProUGUI NextLevelButtonText { get => nextLevelButtonText; set => nextLevelButtonText = value; }
    public Button ReplayLevelButton { get => replayLevelButton; set => replayLevelButton = value; }
    public TextMeshProUGUI VictoryTitleText { get => victoryTitleText; set => victoryTitleText = value; }

    public GameObject StartPanel { get => startPanel; set => startPanel = value; }
    public GameObject GameOverPanel { get => gameOverPanel; set => gameOverPanel = value; }
    public GameObject VictoryPanel { get => victoryPanel; set => victoryPanel = value; }
    public TextMeshProUGUI TimerText { get => timerText; set => timerText = value; }
    public TextMeshProUGUI ScoreText { get => scoreText; set => scoreText = value; }
    public TextMeshProUGUI ComboText { get => comboText; set => comboText = value; }
    public TextMeshProUGUI GameOverScoreText { get => gameOverScoreText; set => gameOverScoreText = value; }
    public TextMeshProUGUI VictoryScoreText { get => victoryScoreText; set => victoryScoreText = value; }
    public GameObject HudPanel { get => hudPanel; set => hudPanel = value; }
    public GameObject PausePanel { get => pausePanel; set => pausePanel = value; }
    public TextMeshProUGUI GameOverStatsText { get => gameOverStatsText; set => gameOverStatsText = value; }
    public TextMeshProUGUI VictoryStatsText { get => victoryStatsText; set => victoryStatsText = value; }

    public Image StartBtnImage { get => startBtnImage; set => startBtnImage = value; }
    public TextMeshProUGUI StartBtnLabel { get => startBtnLabel; set => startBtnLabel = value; }
    public HealthBarUI HealthBarUIComponent { get => healthBarUI; set => healthBarUI = value; }

    [Header("Life Extend UI")]
    [SerializeField] private TextMeshProUGUI lifeExtendBannerText;
    private Coroutine lifeExtendCoroutine;

    public TextMeshProUGUI LifeExtendBannerText { get => lifeExtendBannerText; set => lifeExtendBannerText = value; }

    public void NotifyLifeExtend(string message, int currentHp, int maxHp)
    {
        PlayVictorySound();
        CameraFollow2D.TriggerShake(0.12f, 0.22f);

        if (lifeExtendBannerText != null)
        {
            if (lifeExtendCoroutine != null)
            {
                StopCoroutine(lifeExtendCoroutine);
            }
            lifeExtendCoroutine = StartCoroutine(LifeExtendBannerRoutine(message));
        }

        Debug.Log($"[GameManager] {message} -> Current HP: {currentHp}/{maxHp}");
    }

    private System.Collections.IEnumerator LifeExtendBannerRoutine(string message)
    {
        lifeExtendBannerText.text = message;
        lifeExtendBannerText.gameObject.SetActive(true);

        float duration = 1.6f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            float alpha = Mathf.Clamp01((duration - elapsed) / 0.5f);
            lifeExtendBannerText.color = new Color(0f, 1f, 0.45f, alpha);
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        lifeExtendBannerText.gameObject.SetActive(false);
        lifeExtendCoroutine = null;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
        }

        if (audioSource != null)
        {
            audioSource.playOnAwake = false;
        }

        // Dedicated audio channels for smooth, crisp arcade sound mixing
        if (shootAudioSource == null)
        {
            shootAudioSource = gameObject.AddComponent<AudioSource>();
            shootAudioSource.playOnAwake = false;
        }

        if (hitAudioSource == null)
        {
            hitAudioSource = gameObject.AddComponent<AudioSource>();
            hitAudioSource.playOnAwake = false;
        }

        EnsureUIReferences();

        // Set default difficulty to Easy if not pending from previous level
        int initialDifficulty = PendingDifficultyIndex.HasValue ? PendingDifficultyIndex.Value : (int)currentDifficulty;
        SetDifficulty(initialDifficulty);
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public void ClearAllCombatEntities()
    {
        EnemyChaser[] chasers = Object.FindObjectsByType<EnemyChaser>(FindObjectsInactive.Include);
        foreach (EnemyChaser c in chasers) if (c != null) Destroy(c.gameObject);

        SniperNode[] snipers = Object.FindObjectsByType<SniperNode>(FindObjectsInactive.Include);
        foreach (SniperNode s in snipers) if (s != null) Destroy(s.gameObject);

        EnemyProjectile[] projs = Object.FindObjectsByType<EnemyProjectile>(FindObjectsInactive.Include);
        foreach (EnemyProjectile p in projs) if (p != null) Destroy(p.gameObject);

        WaveSpawner.activeEnemies.Clear();
        if (MiniMapController.Instance != null)
        {
            MiniMapController.Instance.HideAllBlips();
        }
    }

    private void Start()
    {
        EnsureUIReferences();

        currentScore = 0;
        comboCount = 0;
        comboTimer = 0f;
        currentMultiplier = 1;

        if (comboText != null)
        {
            comboText.text = "";
        }

        BindButtons();

        if (gameOverPanel != null)
        {
            gameOverPanel.SetActive(false);
        }

        if (victoryPanel != null)
        {
            victoryPanel.SetActive(false);
        }

        // If advancing from a previous level victory, launch directly into action!
        if (PendingDifficultyIndex.HasValue)
        {
            int targetDiff = PendingDifficultyIndex.Value;
            PendingDifficultyIndex = null;
            SetDifficulty(targetDiff);

            if (startPanel != null) startPanel.SetActive(false);
            if (pausePanel != null) pausePanel.SetActive(false);
            if (hudPanel != null) hudPanel.SetActive(true);

            StartGame();
            return;
        }

        // Normal fresh startup: strictly reset to LevelIndex 0 (20s)
        if (!PendingDifficultyIndex.HasValue)
        {
            currentLevelIndex = 0;
            ApplyLevel(0);
        }

        if (startPanel != null)
        {
            currentState = State.MainMenu;
            isGameActive = false;
            isPaused = false;
            Time.timeScale = 0f;
            startPanel.SetActive(true);
            if (hudPanel != null) hudPanel.SetActive(false);

            // Pause WaveSpawner until game start
            WaveSpawner spawner = Object.FindAnyObjectByType<WaveSpawner>();
            if (spawner != null)
            {
                spawner.enabled = false;
            }

            SetMainMenuVisibility(true);
        }
        else
        {
            currentState = State.Playing;
            isGameActive = true;
            isPaused = false;
            Time.timeScale = 1f;
            if (hudPanel != null) hudPanel.SetActive(true);
        }

        ApplyDifficultyUITheme(currentDifficulty);
    }

    public void ApplyLevel(int index)
    {
        currentLevelIndex = Mathf.Clamp(index, 0, 2);
        currentDifficulty = currentLevelIndex;
        currentLevel = currentLevelIndex + 1;

        switch (currentLevelIndex)
        {
            case 0: // Easy
                survivalTimer = 20f;
                enemySpawnRate = 3.0f;
                baseEnemySpeedMultiplier = 0.75f;
                break;
            case 1: // Normal
                survivalTimer = 50f;
                enemySpawnRate = 2.0f;
                baseEnemySpeedMultiplier = 1.0f;
                break;
            case 2: // Expert
            default:
                survivalTimer = 90f;
                enemySpawnRate = 1.2f;
                baseEnemySpeedMultiplier = 1.35f;
                break;
        }

        WaveSpawner spawner = Object.FindAnyObjectByType<WaveSpawner>(FindObjectsInactive.Include);
        if (spawner != null)
        {
            spawner.ApplyDifficulty(currentLevelIndex);
            spawner.InitialSpawnInterval = enemySpawnRate;
        }

        ApplyDifficultyUITheme(currentLevelIndex);
        UpdateTimerDisplay();
    }

    public void SetDifficulty(int diff)
    {
        currentLevelIndex = Mathf.Clamp(diff, 0, 2);
        ApplyLevel(currentLevelIndex);
        Debug.Log($"[GameManager] Difficulty selected: {currentDifficulty}, Level: {currentLevel} (Timer: {survivalTimeRemaining}s, SpawnRate: {enemySpawnRate}s)");
    }

    public void ApplyDifficultyUITheme(Difficulty diff)
    {
        ApplyDifficultyUITheme((int)diff);
    }

    public void ApplyDifficultyUITheme(int diff = -1)
    {
        if (healthBarUI == null)
        {
            healthBarUI = Object.FindAnyObjectByType<HealthBarUI>(FindObjectsInactive.Include);
        }

        UpdateTimerDisplay();
        UpdateScoreDisplay();
        UpdateDifficultyButtonsUI();
    }

    public void UpdateDifficultyButtonsUI()
    {
        ColorUtility.TryParseHtmlString("#06B6D4", out Color activeCol); // #06B6D4 Electric Cyan
        ColorUtility.TryParseHtmlString("#1E293B", out Color inactiveCol); // #1E293B Clean Charcoal

        if (easyBtnImage != null)
        {
            easyBtnImage.color = (currentDifficulty == 0) ? activeCol : inactiveCol;
        }
        if (normalBtnImage != null)
        {
            normalBtnImage.color = (currentDifficulty == 1) ? activeCol : inactiveCol;
        }
        if (expertBtnImage != null)
        {
            expertBtnImage.color = (currentDifficulty == 2) ? activeCol : inactiveCol;
        }
    }

    private void Update()
    {
        if (SettingsManager.Instance != null && SettingsManager.Instance.IsSettingsOpen)
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                SettingsManager.Instance.CloseSettings();
                return;
            }
            return;
        }

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (isGameActive)
            {
                TogglePause();
                return;
            }
            else if (currentState == State.MainMenu || currentState == State.GameOver || currentState == State.Victory)
            {
                QuitGame();
                return;
            }
        }

        // 1. PLAYING STATE
        if (currentState == State.Playing)
        {
            survivalTimeRemaining -= Time.deltaTime;
            timeSurvived += Time.deltaTime;

            if (survivalTimeRemaining <= 0f)
            {
                survivalTimeRemaining = 0f;
                TriggerVictory();
                return;
            }

            UpdateTimerDisplay();

            if (comboTimer > 0f)
            {
                comboTimer -= Time.deltaTime;
            }
            else
            {
                comboCount = 0;
                currentMultiplier = 1;
                if (comboText != null) comboText.text = "";
            }

            if (Input.GetKeyDown(KeyCode.P))
            {
                TogglePause();
            }
            return;
        }

        // 2. PAUSED STATE
        if (currentState == State.Paused)
        {
            if (Input.GetKeyDown(KeyCode.P))
            {
                TogglePause();
                return;
            }
            if (Input.GetKeyDown(KeyCode.R))
            {
                ResumeGame();
                RestartGame();
                return;
            }
            if (Input.GetKeyDown(KeyCode.M))
            {
                ExitToMenu();
                return;
            }
            return;
        }

        // 3. MAIN MENU STATE
        if (currentState == State.MainMenu)
        {
            if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return))
            {
                StartGame();
                return;
            }
            return;
        }

        // 4. GAME OVER STATE
        if (currentState == State.GameOver)
        {
            if (Input.GetKeyDown(KeyCode.R) || Input.GetKeyDown(KeyCode.Space))
            {
                RestartGame();
                return;
            }
            if (Input.GetKeyDown(KeyCode.M))
            {
                ExitToMenu();
                return;
            }
            return;
        }

        // 5. VICTORY STATE
        if (currentState == State.Victory)
        {
            if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return))
            {
                AdvanceToNextDifficulty();
                return;
            }
            if (Input.GetKeyDown(KeyCode.R))
            {
                ReplayCurrentDifficulty();
                return;
            }
            if (Input.GetKeyDown(KeyCode.M))
            {
                ExitToMenu();
                return;
            }
            return;
        }
    }

    public void StartGame()
    {
        currentState = State.Playing;
        Time.timeScale = 1f;
        isPaused = false;
        isGameActive = true;

        if (startPanel != null) startPanel.SetActive(false);
        if (pausePanel != null) pausePanel.SetActive(false);
        if (gameOverPanel != null) gameOverPanel.SetActive(false);
        if (victoryPanel != null) victoryPanel.SetActive(false);
        if (hudPanel != null) hudPanel.SetActive(true);

        currentScore = 0;
        enemiesEliminated = 0;
        timeSurvived = 0f;
        comboCount = 0;
        comboTimer = 0f;
        currentMultiplier = 1;
        ApplyLevel(currentLevelIndex);

        SetMainMenuVisibility(false);

        PlayerController player = Object.FindAnyObjectByType<PlayerController>(FindObjectsInactive.Include);
        if (player != null)
        {
            player.ResetPlayerState();
        }

        WaveSpawner spawner = Object.FindAnyObjectByType<WaveSpawner>(FindObjectsInactive.Include);
        if (spawner != null)
        {
            spawner.ResetSpawner();
        }

        UpdateScoreDisplay();
        UpdateTimerDisplay();

        Debug.Log($"[GameManager] Game started on difficulty {currentDifficulty}! Time resumed, WaveSpawner activated.");
    }

    public void IncrementEnemiesEliminated()
    {
        enemiesEliminated++;
    }

    public void TogglePause()
    {
        isPaused = !isPaused;
        Time.timeScale = isPaused ? 0f : 1f;
        currentState = isPaused ? State.Paused : State.Playing;
        if (pausePanel != null)
        {
            pausePanel.SetActive(isPaused);
        }
        Debug.Log($"[GameManager] TogglePause: isPaused={isPaused}");
    }

    public void PauseGame()
    {
        if (!isPaused)
        {
            TogglePause();
        }
    }

    public void ResumeGame()
    {
        isPaused = false;
        currentState = State.Playing;
        Time.timeScale = 1f;

        if (pausePanel != null)
        {
            pausePanel.SetActive(false);
        }

        Debug.Log("[GameManager] Game Resumed.");
    }

    public void ExitToMenu()
    {
        Time.timeScale = 1f;
        isPaused = false;
        isGameActive = false;
        currentState = State.MainMenu;

        WaveSpawner spawner = Object.FindAnyObjectByType<WaveSpawner>(FindObjectsInactive.Include);
        if (spawner != null)
        {
            spawner.StopSpawning();
            spawner.enabled = false;
        }

        ClearAllCombatEntities();

        if (pausePanel != null) pausePanel.SetActive(false);
        if (gameOverPanel != null) gameOverPanel.SetActive(false);
        if (victoryPanel != null) victoryPanel.SetActive(false);
        if (hudPanel != null) hudPanel.SetActive(false);

        if (startPanel != null)
        {
            startPanel.SetActive(true);
        }

        SetMainMenuVisibility(true);

        currentScore = 0;
        enemiesEliminated = 0;
        timeSurvived = 0f;
        comboCount = 0;
        comboTimer = 0f;
        currentMultiplier = 1;

        currentLevel = 1;
        SetDifficulty(0);

        Debug.Log("[GameManager] Exited to Main Menu.");
    }

    public void ReturnToMainMenu()
    {
        ExitToMenu();
    }

    public void OpenSettingsFromStart()
    {
        if (UIManager.Instance != null)
        {
            UIManager.Instance.OpenSettingsModal();
            return;
        }
        if (SettingsManager.Instance != null)
        {
            SettingsManager.Instance.OpenSettings(startPanel);
        }
    }

    public void OpenSettingsFromPause()
    {
        if (UIManager.Instance != null)
        {
            UIManager.Instance.OpenSettingsModal();
            return;
        }
        if (SettingsManager.Instance != null)
        {
            SettingsManager.Instance.OpenSettings(pausePanel);
        }
    }

    public void OpenSettingsFromVictory()
    {
        if (UIManager.Instance != null)
        {
            UIManager.Instance.OpenSettingsModal();
            return;
        }
        if (SettingsManager.Instance != null)
        {
            SettingsManager.Instance.OpenSettings(victoryPanel);
        }
    }

    public void CloseSettings()
    {
        if (UIManager.Instance != null)
        {
            UIManager.Instance.CloseSettingsModal(true);
            return;
        }
        if (SettingsManager.Instance != null)
        {
            SettingsManager.Instance.CloseSettings();
        }
    }

    public void ToggleEyeFilter()
    {
        if (UIManager.Instance != null)
        {
            UIManager.Instance.ToggleEyeFilter();
        }
    }

    public void ToggleAudio()
    {
        if (UIManager.Instance != null)
        {
            UIManager.Instance.ToggleAudio();
        }
        else
        {
            AudioListener.volume = (AudioListener.volume > 0f) ? 0f : 1.0f;
        }
    }

    public void ShowToast(string message, float duration = 2.0f)
    {
        if (UIManager.Instance != null)
        {
            UIManager.Instance.ShowToast(message, duration);
        }
        else
        {
            Debug.Log($"[Toast] {message}");
        }
    }

    /// <summary>
    /// When in MainMenu/StartPanel, hide Player and enemy sprites and place them off-screen
    /// so they never bleed through the UI buttons.
    /// </summary>
    public void SetMainMenuVisibility(bool inMainMenu)
    {
        PlayerController player = Object.FindAnyObjectByType<PlayerController>(FindObjectsInactive.Include);
        if (player != null)
        {
            player.SetVisualsEnabled(!inMainMenu);
            player.transform.position = inMainMenu ? new Vector3(0f, -1000f, 0f) : Vector3.zero;
        }

        if (inMainMenu)
        {
            ClearAllCombatEntities();
        }
    }

    public void QuitGame()
    {
        Debug.Log("[GameManager] Exiting application...");
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#elif UNITY_WEBGL
        ReturnToMainMenu();
#else
        Application.Quit();
#endif
    }

    // ==================== PLEASANT & SATISFYING AUDIO SYSTEM ====================

    /// <summary>
    /// Play rapid laser weapon sound with subtle pitch randomness so firing feels dynamic and never causes ear fatigue.
    /// </summary>
    public void PlayShootSound()
    {
        if (shotSFX == null) return;
        AudioSource src = shootAudioSource != null ? shootAudioSource : audioSource;
        if (src == null) return;

        src.pitch = Random.Range(0.95f, 1.05f);
        src.PlayOneShot(shotSFX, 0.55f);
    }

    /// <summary>
    /// Play crisp pop/shatter hit sound with ascending pitch based on combo multiplier streak (1X -> 2X -> 4X).
    /// </summary>
    public void PlayEnemyHitSound()
    {
        if (enemyHitSFX == null) return;
        AudioSource src = hitAudioSource != null ? hitAudioSource : audioSource;
        if (src == null) return;

        float comboPitch = (currentMultiplier >= 4) ? 1.30f : ((currentMultiplier >= 2) ? 1.15f : 1.0f);
        comboPitch += Random.Range(-0.03f, 0.03f);

        src.pitch = Mathf.Clamp(comboPitch, 0.8f, 2.0f);
        src.PlayOneShot(enemyHitSFX, 0.75f);
    }

    /// <summary>
    /// Play player damage sound with soft, muffled low-pitch thud so taking hits is clear without being shrill.
    /// </summary>
    public void PlayPlayerHurtSound()
    {
        if (playerHurtSFX == null || audioSource == null) return;
        audioSource.pitch = 0.88f;
        audioSource.PlayOneShot(playerHurtSFX, 0.7f);
    }

    /// <summary>
    /// Play triumphant, harmonious chord chime for victory and EMP.
    /// </summary>
    public void PlayVictorySound()
    {
        if (victorySFX == null || audioSource == null) return;
        audioSource.pitch = 1.0f;
        audioSource.PlayOneShot(victorySFX, 0.85f);
    }

    /// <summary>
    /// Play gentle, respectful defeat tone without loud buzzers.
    /// </summary>
    public void PlayDefeatSound()
    {
        if (defeatSFX == null || audioSource == null) return;
        audioSource.pitch = 0.85f;
        audioSource.PlayOneShot(defeatSFX, 0.7f);
    }

    /// <summary>
    /// General SFX playback fallback.
    /// </summary>
    public void PlaySound(AudioClip clip)
    {
        PlaySound(clip, 1.0f, 0.75f);
    }

    /// <summary>
    /// Pitch-shifted SFX playback for chimes and dynamic feedback.
    /// </summary>
    public void PlaySound(AudioClip clip, float pitch, float volume = 0.75f)
    {
        if (clip != null && audioSource != null)
        {
            audioSource.pitch = pitch;
            audioSource.PlayOneShot(clip, volume);
        }
    }

    // ==================== GAME OVER & VICTORY LOGIC ====================

    public void AddScore()
    {
        AddScore(100);
    }

    public void AddScore(int points)
    {
        if (currentState != State.Playing)
        {
            return;
        }

        comboCount++;
        comboTimer = comboWindow;

        if (comboCount >= 6)
        {
            currentMultiplier = 4;
        }
        else if (comboCount >= 3)
        {
            currentMultiplier = 2;
        }
        else
        {
            currentMultiplier = 1;
        }

        int finalPoints = points * Mathf.Max(1, currentMultiplier);
        currentScore += finalPoints;

        if (scoreText != null)
        {
            scoreText.text = "NODES PURGED: " + currentScore;
        }

        if (comboText != null)
        {
            if (currentMultiplier > 1)
            {
                switch (currentDifficulty)
                {
                    case 0: // Easy
                        comboText.text = currentMultiplier >= 4
                            ? $">> UNSTOPPABLE: {currentMultiplier}X OVERDRIVE <<"
                            : $"> COMBAT COMBO: {currentMultiplier}X <";
                        comboText.color = new Color(0.369f, 0.918f, 0.831f, 1f); // #5EEAD4
                        break;
                    case 1: // Normal
                        comboText.text = currentMultiplier >= 4
                            ? $"[ >> {currentMultiplier}X HYPERDRIVE ENGAGED << ]"
                            : $"[ > {currentMultiplier}X COMBAT MULTIPLIER < ]";
                        comboText.color = new Color(0.369f, 0.918f, 0.831f, 1f); // #5EEAD4
                        break;
                    case 2: // Expert
                        comboText.text = currentMultiplier >= 4
                            ? $">> MAXIMUM OVERDRIVE: {currentMultiplier}X <<"
                            : $"> CRITICAL MULTIPLIER: {currentMultiplier}X <";
                        comboText.color = new Color(0.984f, 0.443f, 0.522f, 1f); // #FB7185
                        break;
                }
            }
            else
            {
                comboText.text = "";
            }
        }
    }

    public void TriggerGameOver()
    {
        if (currentState != State.Playing)
        {
            return;
        }

        currentState = State.GameOver;
        isGameActive = false;
        isPaused = false;
        Time.timeScale = 0f;

        // Play gentle defeat sound
        PlayDefeatSound();

        // Deactivate WaveSpawner
        WaveSpawner spawner = Object.FindAnyObjectByType<WaveSpawner>(FindObjectsInactive.Include);
        if (spawner != null)
        {
            spawner.StopSpawning();
            spawner.enabled = false;
        }

        ClearAllCombatEntities();

        // Enable gameOverPanel with final score text
        if (gameOverPanel != null)
        {
            gameOverPanel.SetActive(true);
        }

        if (gameOverScoreText != null)
        {
            gameOverScoreText.text = $"NODES PURGED: {currentScore}";
        }

        if (gameOverStatsText != null)
        {
            gameOverStatsText.text = $"ENEMIES ELIMINATED: {enemiesEliminated}   |   SURVIVAL TIME: {timeSurvived:F1}s";
        }

        Debug.Log($"[GameManager] Game Over! Final Score: {currentScore}, Enemies: {enemiesEliminated}, Time: {timeSurvived:F1}s");
    }

    public void TriggerVictory()
    {
        if (currentState != State.Playing && isGameActive == false)
        {
            return;
        }

        currentState = State.Victory;
        isGameActive = false;
        isPaused = false;
        Time.timeScale = 0f;

        // Play pleasant victory chime
        PlayVictorySound();

        // Deactivate WaveSpawner
        WaveSpawner spawner = Object.FindAnyObjectByType<WaveSpawner>(FindObjectsInactive.Include);
        if (spawner != null)
        {
            spawner.StopSpawning();
            spawner.enabled = false;
        }

        ClearAllCombatEntities();

        NeuralTether tether = Object.FindAnyObjectByType<NeuralTether>(FindObjectsInactive.Include);
        if (tether != null)
        {
            tether.ReleaseMinion();
        }

        // Enable victoryPanel with final score and buttons
        if (victoryPanel != null)
        {
            victoryPanel.SetActive(true);
        }
        if (hudPanel != null)
        {
            hudPanel.SetActive(false);
        }

        if (victoryScoreText != null)
        {
            victoryScoreText.text = $"NODES PURGED: {currentScore}";
        }

        if (victoryStatsText != null)
        {
            victoryStatsText.text = "SECTOR SECURED";
        }

        UpdateVictoryPanelUI();

        Debug.Log($"[GameManager] Victory! Level: {currentLevel}, Difficulty: {currentDifficulty}, Final Score: {currentScore}");
    }

    public void UpdateVictoryPanelUI()
    {
        if (victoryTitleText != null)
        {
            victoryTitleText.text = "NEURAL PURGE COMPLETE";
            ColorUtility.TryParseHtmlString("#22C55E", out Color victoryGreen);
            victoryTitleText.color = victoryGreen;
        }

        EnsureNextLevelButtonReferences();

        // Update Next Level Button text/state on victory:
        // Update Next Level Button text/state on victory:
        if (currentLevel == 1)
        {
            if (nextLevelBtn != null) nextLevelBtn.SetActive(true);
            if (nextLevelButtonText != null)
            {
                nextLevelButtonText.text = "ADVANCE TO NORMAL PROTOCOL (50s)";
            }
        }
        else if (currentLevel == 2)
        {
            if (nextLevelBtn != null) nextLevelBtn.SetActive(true);
            if (nextLevelButtonText != null)
            {
                nextLevelButtonText.text = "ADVANCE TO EXPERT PROTOCOL (90s)";
            }
        }
        else // currentLevel >= 3
        {
            if (nextLevelBtn != null)
            {
                nextLevelBtn.SetActive(false);
            }
            if (nextLevelButtonText != null)
            {
                nextLevelButtonText.text = "SYSTEM MASTERED - PLAY AGAIN";
            }
        }
    }

    public void EnsureNextLevelButtonReferences()
    {
        if (nextLevelBtn == null && victoryPanel != null)
        {
            Transform t = victoryPanel.transform.Find("Btn_NextLevel");
            if (t != null) nextLevelBtn = t.gameObject;
        }
        if (nextLevelBtn == null)
        {
            GameObject go = GameObject.Find("Btn_NextLevel");
            if (go != null) nextLevelBtn = go;
        }
        if (nextLevelButton == null && nextLevelBtn != null)
        {
            nextLevelButton = nextLevelBtn.GetComponent<Button>();
        }
        if (nextLevelButtonText == null && nextLevelBtn != null)
        {
            nextLevelButtonText = nextLevelBtn.GetComponentInChildren<TextMeshProUGUI>();
        }
    }

    public void NextLevel()
    {
        Time.timeScale = 1f;
        if (victoryPanel != null)
        {
            victoryPanel.SetActive(false);
        }

        // Advance level linearly: Level 0 -> Level 1 -> Level 2
        currentLevelIndex++;
        if (currentLevelIndex > 2)
        {
            currentLevelIndex = 2;
        }

        ApplyLevel(currentLevelIndex);

        // Reset player position and health (5/5).
        PlayerController player = Object.FindAnyObjectByType<PlayerController>(FindObjectsInactive.Include);
        if (player != null)
        {
            player.ResetPlayerState();
            player.currentHealth = 5;
        }
        if (healthBarUI != null)
        {
            healthBarUI.SetHealth(5, 5);
        }

        // Clear all active enemies, projectiles, and tether references.
        ClearAllCombatEntities();

        NeuralTether tether = Object.FindAnyObjectByType<NeuralTether>(FindObjectsInactive.Include);
        if (tether != null)
        {
            tether.ReleaseMinion();
        }

        // Restart wave loops
        WaveSpawner spawner = Object.FindAnyObjectByType<WaveSpawner>(FindObjectsInactive.Include);
        if (spawner != null)
        {
            spawner.ResetSpawner();
            spawner.InitialSpawnInterval = enemySpawnRate;
        }

        StartGame();

        Debug.Log($"[GameManager] NextLevel: Advanced linearly to LevelIndex {currentLevelIndex}, Level {currentLevel} (Timer: {survivalTimeRemaining}s, SpawnRate: {enemySpawnRate}s).");
    }

    /// <summary>
    /// Advance to next difficulty level automatically and launch directly into gameplay.
    /// </summary>
    public void AdvanceToNextDifficulty()
    {
        NextLevel();
    }

    /// <summary>
    /// Replay the current difficulty level immediately.
    /// </summary>
    public void ReplayCurrentDifficulty()
    {
        RestartGame();
    }

    public void ResetGame()
    {
        currentLevelIndex = 0;
        ApplyLevel(0);

        ClearAllCombatEntities();
        NeuralTether tether = Object.FindAnyObjectByType<NeuralTether>(FindObjectsInactive.Include);
        if (tether != null)
        {
            tether.ReleaseMinion();
        }

        PlayerController player = Object.FindAnyObjectByType<PlayerController>(FindObjectsInactive.Include);
        if (player != null)
        {
            player.ResetPlayerState();
            player.currentHealth = 5;
        }
        if (healthBarUI != null)
        {
            healthBarUI.SetHealth(5, 5);
        }

        WaveSpawner spawner = Object.FindAnyObjectByType<WaveSpawner>(FindObjectsInactive.Include);
        if (spawner != null)
        {
            spawner.ResetSpawner();
            spawner.InitialSpawnInterval = enemySpawnRate;
        }

        StartGame();

        Debug.Log($"[GameManager] ResetGame: Reset to LevelIndex 0, Level 1 (Timer: {survivalTimeRemaining}s).");
    }

    public void StartNewGame()
    {
        ResetGame();
    }

    public void RestartGame()
    {
        ApplyLevel(currentLevelIndex);

        ClearAllCombatEntities();
        NeuralTether tether = Object.FindAnyObjectByType<NeuralTether>(FindObjectsInactive.Include);
        if (tether != null)
        {
            tether.ReleaseMinion();
        }

        PlayerController player = Object.FindAnyObjectByType<PlayerController>(FindObjectsInactive.Include);
        if (player != null)
        {
            player.ResetPlayerState();
            player.currentHealth = 5;
        }
        if (healthBarUI != null)
        {
            healthBarUI.SetHealth(5, 5);
        }

        WaveSpawner spawner = Object.FindAnyObjectByType<WaveSpawner>(FindObjectsInactive.Include);
        if (spawner != null)
        {
            spawner.ResetSpawner();
            spawner.InitialSpawnInterval = enemySpawnRate;
        }

        StartGame();
    }

    private void UpdateTimerDisplay()
    {
        if (timerText == null) return;
        int seconds = Mathf.CeilToInt(survivalTimeRemaining);
        timerText.text = $"SURVIVE: {seconds}s";
    }

    private void UpdateScoreDisplay()
    {
        if (scoreText == null) return;
        scoreText.text = $"NODES PURGED: {currentScore}";
    }

    public void BindButtons()
    {
        GameObject startBtnObj = GameObject.Find("Btn_Start");
        if (startBtnObj != null)
        {
            Button sBtn = startBtnObj.GetComponent<Button>();
            if (sBtn != null)
            {
                sBtn.onClick.RemoveAllListeners();
                sBtn.onClick.AddListener(ResetGame);
            }
        }

        GameObject restartBtnObj = GameObject.Find("Btn_Restart");
        if (restartBtnObj != null)
        {
            Button rBtn = restartBtnObj.GetComponent<Button>();
            if (rBtn != null)
            {
                rBtn.onClick.RemoveAllListeners();
                rBtn.onClick.AddListener(ResetGame);
            }
        }

        if (easyButton == null)
        {
            GameObject b = GameObject.Find("Btn_Easy");
            if (b != null) easyButton = b.GetComponent<Button>();
        }
        if (normalButton == null)
        {
            GameObject b = GameObject.Find("Btn_Normal");
            if (b != null) normalButton = b.GetComponent<Button>();
        }
        if (expertButton == null)
        {
            GameObject b = GameObject.Find("Btn_Expert");
            if (b != null) expertButton = b.GetComponent<Button>();
        }

        if (easyButton != null)
        {
            easyButton.onClick.RemoveAllListeners();
            easyButton.onClick.AddListener(() => SetDifficulty(0));
        }

        if (normalButton != null)
        {
            normalButton.onClick.RemoveAllListeners();
            normalButton.onClick.AddListener(() => SetDifficulty(1));
        }

        if (expertButton != null)
        {
            expertButton.onClick.RemoveAllListeners();
            expertButton.onClick.AddListener(() => SetDifficulty(2));
        }

        if (replayLevelButton != null)
        {
            replayLevelButton.onClick.RemoveAllListeners();
            replayLevelButton.onClick.AddListener(RestartGame);
        }

        // Victory Panel Buttons
        EnsureNextLevelButtonReferences();
        if (nextLevelButton != null)
        {
            nextLevelButton.onClick.RemoveAllListeners();
            nextLevelButton.onClick.AddListener(NextLevel);
        }

        if (replayLevelButton == null && victoryPanel != null)
        {
            Transform t = victoryPanel.transform.Find("Btn_Replay");
            if (t != null) replayLevelButton = t.GetComponent<Button>();
        }
        if (replayLevelButton != null)
        {
            replayLevelButton.onClick.RemoveAllListeners();
            replayLevelButton.onClick.AddListener(RestartGame);
        }

        GameObject vicMenuBtnObj = null;
        if (victoryPanel != null)
        {
            Transform t = victoryPanel.transform.Find("Btn_VictoryMenu");
            if (t != null) vicMenuBtnObj = t.gameObject;
        }
        if (vicMenuBtnObj == null)
        {
            vicMenuBtnObj = GameObject.Find("Btn_VictoryMenu");
        }
        if (vicMenuBtnObj != null)
        {
            Button btn = vicMenuBtnObj.GetComponent<Button>();
            if (btn != null)
            {
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(ExitToMenu);
            }
        }

        // Pause Panel Buttons
        GameObject resumeBtnObj = null;
        if (pausePanel != null)
        {
            Transform t = pausePanel.transform.Find("Btn_Resume");
            if (t != null) resumeBtnObj = t.gameObject;
        }
        if (resumeBtnObj == null)
        {
            resumeBtnObj = GameObject.Find("Btn_Resume");
        }
        if (resumeBtnObj != null)
        {
            Button btn = resumeBtnObj.GetComponent<Button>();
            if (btn != null)
            {
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(ResumeGame);
            }
        }

        GameObject pauseMenuBtnObj = null;
        if (pausePanel != null)
        {
            Transform t = pausePanel.transform.Find("Btn_PauseMenu");
            if (t != null) pauseMenuBtnObj = t.gameObject;
        }
        if (pauseMenuBtnObj == null)
        {
            pauseMenuBtnObj = GameObject.Find("Btn_PauseMenu");
        }
        if (pauseMenuBtnObj != null)
        {
            Button btn = pauseMenuBtnObj.GetComponent<Button>();
            if (btn != null)
            {
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(ExitToMenu);
            }
        }

        GameObject quitBtnObj = null;
        if (pausePanel != null)
        {
            Transform t = pausePanel.transform.Find("Btn_Quit");
            if (t != null) quitBtnObj = t.gameObject;
        }
        if (quitBtnObj == null)
        {
            quitBtnObj = GameObject.Find("Btn_Quit");
        }
        if (quitBtnObj != null)
        {
            Button btn = quitBtnObj.GetComponent<Button>();
            if (btn != null)
            {
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(QuitGame);
            }
        }
    }

    public void EnsureUIReferences()
    {
        if (hudPanel == null)
        {
            GameObject go = GameObject.Find("HUDPanel");
            if (go != null) hudPanel = go;
        }
        if (startPanel == null)
        {
            GameObject go = GameObject.Find("StartMenuPanel") ?? GameObject.Find("StartPanel");
            if (go != null) startPanel = go;
        }
        if (gameOverPanel == null)
        {
            GameObject go = GameObject.Find("GameOverPanel");
            if (go != null) gameOverPanel = go;
        }
        if (victoryPanel == null)
        {
            GameObject go = GameObject.Find("VictoryPanel");
            if (go != null) victoryPanel = go;
        }
        if (pausePanel == null)
        {
            GameObject go = GameObject.Find("PausePanel");
            if (go != null) pausePanel = go;
        }
        if (timerText == null)
        {
            GameObject go = GameObject.Find("Timer_Text");
            if (go != null) timerText = go.GetComponent<TextMeshProUGUI>();
        }
        if (scoreText == null)
        {
            GameObject go = GameObject.Find("Score_Text");
            if (go != null) scoreText = go.GetComponent<TextMeshProUGUI>();
        }
        if (comboText == null)
        {
            GameObject go = GameObject.Find("Multiplier_Text");
            if (go != null) comboText = go.GetComponent<TextMeshProUGUI>();
        }
        if (healthBarUI == null)
        {
            healthBarUI = Object.FindAnyObjectByType<HealthBarUI>(FindObjectsInactive.Include);
        }
        EnsureNextLevelButtonReferences();
    }
}
