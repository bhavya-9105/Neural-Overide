using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Central manager for the dark teal-blue vector UI system.
/// Handles Eye Filter Mode, Audio Quick Toggle, System Settings Modal, and floating Toast Notifications.
/// </summary>
public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    [Header("Main Panels")]
    public GameObject startMenuPanel;
    public GameObject hudPanel;
    public GameObject settingsModalPanel;
    public GameObject pausePanel;
    public GameObject gameOverPanel;
    public GameObject victoryPanel;

    [Header("Eye Comfort Filter")]
    public Image eyeFilterOverlay;
    public Button eyeFilterButton;
    public TextMeshProUGUI eyeFilterButtonText;
    public bool isEyeFilterActive = false;

    [Header("Audio Quick Toggle")]
    public Button audioQuickToggleBtn;
    public TextMeshProUGUI audioQuickToggleText;
    public bool isAudioMuted = false;

    [Header("Floating Toast System")]
    public GameObject toastBanner;
    public CanvasGroup toastCanvasGroup;
    public TextMeshProUGUI toastText;
    private Coroutine toastCoroutine;

    [Header("Settings Modal Elements")]
    public Slider masterVolumeSlider;
    public TextMeshProUGUI masterVolumeValueText;
    public Button ambientParticlesBtn;
    public TextMeshProUGUI ambientParticlesBtnText;
    public Button highContrastBtn;
    public TextMeshProUGUI highContrastBtnText;
    public Button applyCloseBtn;
    public Button modalCloseXBtn;

    [Header("Settings State")]
    public float masterVolume = 100f; // 0 to 100
    public bool ambientParticlesEnabled = true;
    public bool highContrastEnabled = false;

    private GameObject lastActivePanelBeforeModal;

    private static readonly Color ColorTealPrimary = new Color(0f, 240f / 255f, 1f, 1f); // #00f0ff Electric Cyan
    private static readonly Color ColorTealAccent = new Color(56f / 255f, 189f / 255f, 248f / 255f, 1f); // #38bdf8 Glow Highlight
    private static readonly Color ColorTranslucentNavy = new Color(15f / 255f, 20f / 255f, 29f / 255f, 0.90f); // #0f141d with 90% opacity
    private static readonly Color ColorWarmSepiaOverlay = new Color(1.0f, 0.84f, 0.60f, 0.16f); // Warm soothing tint

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else if (Instance != this)
        {
            Destroy(this);
            return;
        }

        LoadPreferences();
        EnsureUIReferences();
    }

    private void Start()
    {
        ApplyPreferences();
        BindUIEvents();
    }

    private void Update()
    {
        if (settingsModalPanel != null && settingsModalPanel.activeSelf)
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                CloseSettingsModal(false);
            }
        }
    }

    public void EnsureUIReferences()
    {
        if (startMenuPanel == null)
        {
            startMenuPanel = GameObject.Find("StartMenuPanel") ?? GameObject.Find("StartPanel");
        }
        if (hudPanel == null)
        {
            hudPanel = GameObject.Find("HUDPanel");
        }
        if (settingsModalPanel == null)
        {
            settingsModalPanel = GameObject.Find("SettingsModalPanel") ?? GameObject.Find("SettingsPanel");
        }
        if (pausePanel == null)
        {
            pausePanel = GameObject.Find("PausePanel");
        }
        if (gameOverPanel == null)
        {
            gameOverPanel = GameObject.Find("GameOverPanel");
        }
        if (victoryPanel == null)
        {
            victoryPanel = GameObject.Find("VictoryPanel");
        }

        if (eyeFilterOverlay == null)
        {
            GameObject overlayObj = GameObject.Find("EyeFilterOverlay");
            if (overlayObj != null) eyeFilterOverlay = overlayObj.GetComponent<Image>();
        }

        if (toastBanner == null)
        {
            toastBanner = GameObject.Find("Toast_Banner");
            if (toastBanner != null)
            {
                toastCanvasGroup = toastBanner.GetComponent<CanvasGroup>();
                toastText = toastBanner.GetComponentInChildren<TextMeshProUGUI>();
            }
        }

        if (audioQuickToggleBtn == null)
        {
            GameObject aBtnObj = GameObject.Find("Btn_AudioQuickToggle");
            if (aBtnObj != null)
            {
                audioQuickToggleBtn = aBtnObj.GetComponent<Button>();
                audioQuickToggleText = aBtnObj.GetComponentInChildren<TextMeshProUGUI>();
            }
        }

        if (eyeFilterButton == null)
        {
            GameObject efBtnObj = GameObject.Find("Btn_EyeFilter");
            if (efBtnObj != null)
            {
                eyeFilterButton = efBtnObj.GetComponent<Button>();
                eyeFilterButtonText = efBtnObj.GetComponentInChildren<TextMeshProUGUI>();
            }
        }
    }

    public void BindUIEvents()
    {
        if (audioQuickToggleBtn != null)
        {
            audioQuickToggleBtn.onClick.RemoveAllListeners();
            audioQuickToggleBtn.onClick.AddListener(ToggleAudio);
        }

        if (eyeFilterButton != null)
        {
            eyeFilterButton.onClick.RemoveAllListeners();
            eyeFilterButton.onClick.AddListener(ToggleEyeFilter);
        }

        if (masterVolumeSlider != null)
        {
            masterVolumeSlider.onValueChanged.RemoveAllListeners();
            masterVolumeSlider.onValueChanged.AddListener(SetMasterVolume);
        }

        if (ambientParticlesBtn != null)
        {
            ambientParticlesBtn.onClick.RemoveAllListeners();
            ambientParticlesBtn.onClick.AddListener(ToggleAmbientParticles);
        }

        if (highContrastBtn != null)
        {
            highContrastBtn.onClick.RemoveAllListeners();
            highContrastBtn.onClick.AddListener(ToggleHighContrast);
        }

        if (applyCloseBtn != null)
        {
            applyCloseBtn.onClick.RemoveAllListeners();
            applyCloseBtn.onClick.AddListener(() => CloseSettingsModal(true));
        }

        if (modalCloseXBtn != null)
        {
            modalCloseXBtn.onClick.RemoveAllListeners();
            modalCloseXBtn.onClick.AddListener(() => CloseSettingsModal(false));
        }

        // Action Buttons on Start Panel
        GameObject startBtnObj = GameObject.Find("Btn_Start");
        if (startBtnObj != null)
        {
            Button sBtn = startBtnObj.GetComponent<Button>();
            if (sBtn != null)
            {
                sBtn.onClick.RemoveAllListeners();
                sBtn.onClick.AddListener(OnStartButtonClicked);
            }
        }

        GameObject settingsBtnObj = GameObject.Find("Btn_Settings");
        if (settingsBtnObj != null)
        {
            Button setBtn = settingsBtnObj.GetComponent<Button>();
            if (setBtn != null)
            {
                setBtn.onClick.RemoveAllListeners();
                setBtn.onClick.AddListener(OpenSettingsModal);
            }
        }

        GameObject exitBtnObj = GameObject.Find("Btn_Exit");
        if (exitBtnObj != null)
        {
            Button exBtn = exitBtnObj.GetComponent<Button>();
            if (exBtn != null)
            {
                exBtn.onClick.RemoveAllListeners();
                exBtn.onClick.AddListener(OnExitButtonClicked);
            }
        }
    }

    // ==================== EYE COMFORT FILTER ====================

    public void ToggleEyeFilter()
    {
        SetEyeFilter(!isEyeFilterActive);
        ShowToast(isEyeFilterActive ? "EYE COMFORT FILTER ACTIVATED" : "EYE COMFORT FILTER DEACTIVATED");
    }

    public void SetEyeFilter(bool enabled)
    {
        isEyeFilterActive = enabled;
        PlayerPrefs.SetInt("Settings_EyeFilter", isEyeFilterActive ? 1 : 0);
        PlayerPrefs.Save();

        if (eyeFilterOverlay != null)
        {
            eyeFilterOverlay.color = isEyeFilterActive ? ColorWarmSepiaOverlay : Color.clear;
            eyeFilterOverlay.gameObject.SetActive(isEyeFilterActive);
        }

        // Camera background warm tint matrix
        Camera cam = Camera.main;
        if (cam != null)
        {
            Color baseColor;
            if (isEyeFilterActive)
            {
                // Subtle warm sepia dark tone
                ColorUtility.TryParseHtmlString("#121415", out baseColor);
            }
            else
            {
                // Standard Deep Dark Navy #071322
                ColorUtility.TryParseHtmlString("#071322", out baseColor);
            }
            cam.backgroundColor = baseColor;
        }

        UpdateEyeFilterButtonUI();
    }

    private void UpdateEyeFilterButtonUI()
    {
        if (eyeFilterButtonText != null)
        {
            eyeFilterButtonText.text = isEyeFilterActive ? "EYE FILTER: ACTIVE" : "APPLY EYE FILTER MODE";
            eyeFilterButtonText.color = isEyeFilterActive ? new Color(0.99f, 0.94f, 0.54f, 1f) : ColorTealAccent; // #FEF08A or #5EEAD4
        }
    }

    // ==================== AUDIO QUICK TOGGLE ====================

    public void ToggleAudio()
    {
        isAudioMuted = !isAudioMuted;
        AudioListener.volume = isAudioMuted ? 0f : (masterVolume / 100f);
        PlayerPrefs.SetInt("Settings_AudioMuted", isAudioMuted ? 1 : 0);
        PlayerPrefs.Save();

        UpdateAudioQuickToggleUI();
        ShowToast(isAudioMuted ? "AUDIO: MUTED" : "AUDIO: ENABLED");

        if (SettingsManager.Instance != null)
        {
            SettingsManager.Instance.isMuted = isAudioMuted;
            SettingsManager.Instance.volume = masterVolume / 100f;
            SettingsManager.Instance.ApplyAudio();
        }
    }

    public void SetAudio(bool enabled)
    {
        isAudioMuted = !enabled;
        AudioListener.volume = isAudioMuted ? 0f : (masterVolume / 100f);
        PlayerPrefs.SetInt("Settings_AudioMuted", isAudioMuted ? 1 : 0);
        PlayerPrefs.Save();
        UpdateAudioQuickToggleUI();
    }

    private void UpdateAudioQuickToggleUI()
    {
        if (audioQuickToggleText != null)
        {
            audioQuickToggleText.text = isAudioMuted ? "AUDIO: MUTED" : "AUDIO: ON";
            audioQuickToggleText.color = isAudioMuted ? new Color(1f, 0.45f, 0.45f, 1f) : new Color(56f / 255f, 189f / 255f, 248f / 255f, 1f); // Red or Cyan #38BDF8
        }
    }

    // ==================== SETTINGS MODAL ====================

    public void OpenSettingsModal()
    {
        EnsureUIReferences();

        // Detect caller panel
        if (startMenuPanel != null && startMenuPanel.activeSelf)
        {
            lastActivePanelBeforeModal = startMenuPanel;
        }
        else if (pausePanel != null && pausePanel.activeSelf)
        {
            lastActivePanelBeforeModal = pausePanel;
        }
        else if (victoryPanel != null && victoryPanel.activeSelf)
        {
            lastActivePanelBeforeModal = victoryPanel;
        }
        else
        {
            lastActivePanelBeforeModal = null;
        }

        if (settingsModalPanel != null)
        {
            settingsModalPanel.SetActive(true);
            UpdateModalControlsUI();
        }
    }

    public void CloseSettingsModal(bool apply = true)
    {
        if (apply)
        {
            SavePreferences();
            ApplyPreferences();
            ShowToast("SETTINGS PREFERENCES APPLIED");
        }

        if (settingsModalPanel != null)
        {
            settingsModalPanel.SetActive(false);
        }

        if (lastActivePanelBeforeModal != null)
        {
            lastActivePanelBeforeModal.SetActive(true);
        }
    }

    public void SetMasterVolume(float val)
    {
        masterVolume = Mathf.Clamp(val, 0f, 100f);
        if (!isAudioMuted)
        {
            AudioListener.volume = masterVolume / 100f;
        }

        if (masterVolumeValueText != null)
        {
            masterVolumeValueText.text = $"{Mathf.RoundToInt(masterVolume)}%";
        }

        if (SettingsManager.Instance != null)
        {
            SettingsManager.Instance.volume = masterVolume / 100f;
            SettingsManager.Instance.ApplyAudio();
        }
    }

    public void ToggleAmbientParticles()
    {
        ambientParticlesEnabled = !ambientParticlesEnabled;
        ApplyAmbientParticles();
        UpdateModalControlsUI();
    }

    public void ToggleHighContrast()
    {
        highContrastEnabled = !highContrastEnabled;
        ApplyHighContrast();
        UpdateModalControlsUI();
    }

    private void ApplyAmbientParticles()
    {
        ParticleSystem[] particleSystems = Object.FindObjectsByType<ParticleSystem>(FindObjectsInactive.Include);
        foreach (ParticleSystem ps in particleSystems)
        {
            if (ps == null) continue;
            string n = ps.gameObject.name.ToLowerInvariant();
            if (n.Contains("ambient") || n.Contains("dust") || n.Contains("bg"))
            {
                if (ambientParticlesEnabled)
                {
                    if (!ps.isPlaying) ps.Play();
                }
                else
                {
                    if (ps.isPlaying) ps.Stop();
                }
            }
        }
    }

    private void ApplyHighContrast()
    {
        TextMeshProUGUI[] tmps = Object.FindObjectsByType<TextMeshProUGUI>(FindObjectsInactive.Include);
        foreach (TextMeshProUGUI tmp in tmps)
        {
            if (tmp == null) continue;
            if (highContrastEnabled)
            {
                if (tmp.color.a > 0.1f && tmp.color.r < 0.6f && tmp.color.g < 0.6f && tmp.color.b < 0.6f)
                {
                    tmp.color = Color.white;
                }
            }
        }
    }

    private void UpdateModalControlsUI()
    {
        if (masterVolumeSlider != null) masterVolumeSlider.value = masterVolume;
        if (masterVolumeValueText != null) masterVolumeValueText.text = $"{Mathf.RoundToInt(masterVolume)}%";

        if (ambientParticlesBtnText != null)
        {
            ambientParticlesBtnText.text = ambientParticlesEnabled ? "ENABLED" : "DISABLED";
            ambientParticlesBtnText.color = ambientParticlesEnabled ? ColorTealAccent : new Color(0.6f, 0.65f, 0.75f, 1f);
        }

        if (highContrastBtnText != null)
        {
            highContrastBtnText.text = highContrastEnabled ? "ON" : "OFF";
            highContrastBtnText.color = highContrastEnabled ? new Color(0.99f, 0.94f, 0.54f, 1f) : new Color(0.6f, 0.65f, 0.75f, 1f);
        }
    }

    // ==================== FLOATING TOAST SYSTEM ====================

    public void ShowToast(string message, float duration = 2.0f)
    {
        if (toastBanner == null || toastText == null)
        {
            EnsureUIReferences();
        }

        if (toastBanner == null || toastText == null)
        {
            Debug.Log($"[Toast] {message}");
            return;
        }

        if (toastCoroutine != null)
        {
            StopCoroutine(toastCoroutine);
        }

        toastCoroutine = StartCoroutine(ToastRoutine(message, duration));
    }

    private IEnumerator ToastRoutine(string message, float duration)
    {
        toastText.text = message;
        toastBanner.SetActive(true);

        if (toastCanvasGroup == null)
        {
            toastCanvasGroup = toastBanner.GetComponent<CanvasGroup>() ?? toastBanner.AddComponent<CanvasGroup>();
        }

        // Fade in
        float fadeInTime = 0.15f;
        float elapsed = 0f;
        while (elapsed < fadeInTime)
        {
            elapsed += Time.unscaledDeltaTime;
            toastCanvasGroup.alpha = Mathf.Clamp01(elapsed / fadeInTime);
            yield return null;
        }
        toastCanvasGroup.alpha = 1f;

        // Hold
        yield return new WaitForSecondsRealtime(duration);

        // Fade out
        float fadeOutTime = 0.25f;
        elapsed = 0f;
        while (elapsed < fadeOutTime)
        {
            elapsed += Time.unscaledDeltaTime;
            toastCanvasGroup.alpha = Mathf.Clamp01(1f - (elapsed / fadeOutTime));
            yield return null;
        }
        toastCanvasGroup.alpha = 0f;

        toastBanner.SetActive(false);
        toastCoroutine = null;
    }

    // ==================== PREFERENCES STORAGE ====================

    public void SavePreferences()
    {
        PlayerPrefs.SetFloat("Settings_MasterVolume", masterVolume);
        PlayerPrefs.SetInt("Settings_AudioMuted", isAudioMuted ? 1 : 0);
        PlayerPrefs.SetInt("Settings_EyeFilter", isEyeFilterActive ? 1 : 0);
        PlayerPrefs.SetInt("Settings_AmbientParticles", ambientParticlesEnabled ? 1 : 0);
        PlayerPrefs.SetInt("Settings_HighContrast", highContrastEnabled ? 1 : 0);
        PlayerPrefs.Save();
    }

    public void LoadPreferences()
    {
        masterVolume = PlayerPrefs.GetFloat("Settings_MasterVolume", 100f);
        isAudioMuted = PlayerPrefs.GetInt("Settings_AudioMuted", 0) == 1;
        isEyeFilterActive = PlayerPrefs.GetInt("Settings_EyeFilter", 0) == 1;
        ambientParticlesEnabled = PlayerPrefs.GetInt("Settings_AmbientParticles", 1) == 1;
        highContrastEnabled = PlayerPrefs.GetInt("Settings_HighContrast", 0) == 1;
    }

    public void ApplyPreferences()
    {
        AudioListener.volume = isAudioMuted ? 0f : (masterVolume / 100f);
        SetEyeFilter(isEyeFilterActive);
        ApplyAmbientParticles();
        ApplyHighContrast();
        UpdateAudioQuickToggleUI();
        UpdateEyeFilterButtonUI();
        UpdateModalControlsUI();
    }

    // ==================== START / EXIT ACTIONS ====================

    public void OnStartButtonClicked()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.ResetGame();
        }
    }

    public void OnExitButtonClicked()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.QuitGame();
        }
        else
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
