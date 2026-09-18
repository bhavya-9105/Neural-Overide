using System;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

public class SettingsManager : MonoBehaviour
{
    public static SettingsManager Instance { get; private set; }

    public enum MovementMode
    {
        WASDOnly = 0,
        ArrowKeysOnly = 1,
        Both = 2
    }

    public enum GraphicsQuality
    {
        Low = 0,
        Medium = 1,
        High = 2
    }

    [Header("Audio Settings")]
    [Range(0f, 1f)] public float volume = 1.0f;
    public bool isMuted = false;

    [Header("Display Settings")]
    [Range(0f, 2f)] public float brightness = 1.0f; // 0% - 200%, default 100%
    [Range(0.5f, 1.5f)] public float contrast = 1.0f; // 50% - 150%, default 100%
    public GraphicsQuality graphicsQuality = GraphicsQuality.High;

    [Header("Controls Settings")]
    public MovementMode currentMovementMode = MovementMode.Both;
    public int primaryMouseButton = 0;   // 0 = Left Click
    public int secondaryMouseButton = 1; // 1 = Right Click
    public int specialMouseButton = 2;   // 2 = Middle Click

    [Header("UI Overlay & Panels")]
    public GameObject settingsPanel;
    public GameObject previousPanel;
    public Image screenEffectOverlay;

    [Header("Tab References")]
    public GameObject tabDisplayAudioObj;
    public GameObject tabControlsObj;
    public Image tabDisplayAudioImg;
    public Image tabControlsImg;
    public TextMeshProUGUI tabDisplayAudioText;
    public TextMeshProUGUI tabControlsText;
    public Button tabDisplayAudioBtn;
    public Button tabControlsBtn;

    [Header("Display & Audio UI References")]
    public Slider volumeSlider;
    public TextMeshProUGUI volumeValueText;
    public Button muteButton;
    public TextMeshProUGUI muteButtonText;

    public Slider brightnessSlider;
    public TextMeshProUGUI brightnessValueText;

    public Slider contrastSlider;
    public TextMeshProUGUI contrastValueText;

    public Button qualityLowBtn;
    public Button qualityMedBtn;
    public Button qualityHighBtn;
    public Image qualityLowImg;
    public Image qualityMedImg;
    public Image qualityHighImg;

    [Header("Controls UI References")]
    public Button moveWasdBtn;
    public Button moveArrowsBtn;
    public Button moveBothBtn;
    public Image moveWasdImg;
    public Image moveArrowsImg;
    public Image moveBothImg;

    public Button mousePrimaryBtn;
    public TextMeshProUGUI mousePrimaryText;
    public Button mouseSecondaryBtn;
    public TextMeshProUGUI mouseSecondaryText;
    public Button mouseSpecialBtn;
    public TextMeshProUGUI mouseSpecialText;

    public TextMeshProUGUI keybindPreviewText;

    [Header("Action Buttons")]
    public Button backButton;
    public Button resetDefaultsButton;

    // Tactical Zoom Tracking
    private bool isTacticalZoomed = false;
    private float defaultCameraOrthoSize = 8f;
    private const float ZOOMED_CAMERA_ORTHO_SIZE = 11.5f;

    public bool IsSettingsOpen => (settingsPanel != null && settingsPanel.activeSelf) ||
                                  (UIManager.Instance != null && UIManager.Instance.settingsModalPanel != null && UIManager.Instance.settingsModalPanel.activeSelf);
    public MovementMode CurrentMovementMode => currentMovementMode;
    public int PrimaryMouseButton => primaryMouseButton;
    public int SecondaryMouseButton => secondaryMouseButton;
    public int SpecialMouseButton => specialMouseButton;
    public bool IsTacticalZoomed => isTacticalZoomed;

    private static readonly Color ColorActiveCyan = new Color(0f, 240f / 255f, 1f, 1f); // #00f0ff
    private static readonly Color ColorInactiveCharcoal = new Color(15f / 255f, 20f / 255f, 29f / 255f, 0.90f); // #0f141d
    private static readonly Color ColorButtonHover = new Color(56f / 255f, 189f / 255f, 248f / 255f, 1f); // #38bdf8

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

        if (Camera.main != null)
        {
            defaultCameraOrthoSize = Camera.main.orthographicSize;
        }

        LoadSettings();
    }

    private void Start()
    {
        ApplyAllSettings();
        BindUIEvents();
        UpdateUIFromSettings();
    }

    private void Update()
    {
        if (IsSettingsOpen && Input.GetKeyDown(KeyCode.Escape))
        {
            CloseSettings();
        }
    }

    public void OpenSettings(GameObject caller)
    {
        previousPanel = caller;

        if (UIManager.Instance != null && UIManager.Instance.settingsModalPanel != null)
        {
            UIManager.Instance.OpenSettingsModal();
            return;
        }

        if (previousPanel != null)
        {
            previousPanel.SetActive(false);
        }

        if (settingsPanel != null)
        {
            settingsPanel.SetActive(true);
            SwitchTab(0);
            UpdateUIFromSettings();
        }

        Debug.Log($"[SettingsManager] Opened Settings Overlay. Caller: {(caller != null ? caller.name : "null")}");
    }

    public void CloseSettings()
    {
        SaveSettings();

        if (UIManager.Instance != null && UIManager.Instance.settingsModalPanel != null)
        {
            UIManager.Instance.CloseSettingsModal(true);
            return;
        }

        if (settingsPanel != null)
        {
            settingsPanel.SetActive(false);
        }

        if (previousPanel != null)
        {
            previousPanel.SetActive(true);
        }

        Debug.Log($"[SettingsManager] Closed Settings Overlay. Returned to: {(previousPanel != null ? previousPanel.name : "null")}");
    }

    public void SwitchTab(int tabIndex)
    {
        bool isDisplayAudio = tabIndex == 0;

        if (tabDisplayAudioObj != null) tabDisplayAudioObj.SetActive(isDisplayAudio);
        if (tabControlsObj != null) tabControlsObj.SetActive(!isDisplayAudio);

        if (tabDisplayAudioImg != null)
            tabDisplayAudioImg.color = isDisplayAudio ? ColorActiveCyan : ColorInactiveCharcoal;
        if (tabControlsImg != null)
            tabControlsImg.color = !isDisplayAudio ? ColorActiveCyan : ColorInactiveCharcoal;

        if (tabDisplayAudioText != null)
            tabDisplayAudioText.color = isDisplayAudio ? Color.white : new Color(148f / 255f, 163f / 255f, 184f / 255f, 1f);
        if (tabControlsText != null)
            tabControlsText.color = !isDisplayAudio ? Color.white : new Color(148f / 255f, 163f / 255f, 184f / 255f, 1f);
    }

    public void SetVolume(float val)
    {
        volume = Mathf.Clamp01(val);
        ApplyAudio();
        if (volumeValueText != null)
        {
            volumeValueText.text = $"{Mathf.RoundToInt(volume * 100f)}%";
        }
    }

    public void ToggleMute()
    {
        isMuted = !isMuted;
        ApplyAudio();
        UpdateMuteButtonUI();
    }

    public void SetBrightness(float val)
    {
        brightness = Mathf.Clamp(val, 0f, 2f);
        ApplyDisplay();
        if (brightnessValueText != null)
        {
            brightnessValueText.text = $"{Mathf.RoundToInt(brightness * 100f)}%";
        }
    }

    public void SetContrast(float val)
    {
        contrast = Mathf.Clamp(val, 0.5f, 1.5f);
        ApplyDisplay();
        if (contrastValueText != null)
        {
            contrastValueText.text = $"{Mathf.RoundToInt(contrast * 100f)}%";
        }
    }

    public void SetGraphicsQuality(int qualityIndex)
    {
        graphicsQuality = (GraphicsQuality)Mathf.Clamp(qualityIndex, 0, 2);
        ApplyGraphicsQuality();
        UpdateQualityButtonsUI();
    }

    public void SetMovementMode(int modeIndex)
    {
        currentMovementMode = (MovementMode)Mathf.Clamp(modeIndex, 0, 2);
        UpdateMovementButtonsUI();
        UpdateKeybindPreviewTable();
    }

    public void CyclePrimaryMouseButton()
    {
        primaryMouseButton = (primaryMouseButton + 1) % 3;
        UpdateMouseButtonsUI();
        UpdateKeybindPreviewTable();
    }

    public void CycleSecondaryMouseButton()
    {
        secondaryMouseButton = (secondaryMouseButton + 1) % 3;
        UpdateMouseButtonsUI();
        UpdateKeybindPreviewTable();
    }

    public void CycleSpecialMouseButton()
    {
        specialMouseButton = (specialMouseButton + 1) % 3;
        UpdateMouseButtonsUI();
        UpdateKeybindPreviewTable();
    }

    public void ToggleTacticalZoom()
    {
        isTacticalZoomed = !isTacticalZoomed;
        Camera cam = Camera.main;
        if (cam != null)
        {
            cam.orthographicSize = isTacticalZoomed ? ZOOMED_CAMERA_ORTHO_SIZE : defaultCameraOrthoSize;
        }
        Debug.Log($"[SettingsManager] Tactical Zoom: {(isTacticalZoomed ? "ACTIVATED" : "DEACTIVATED")}");
    }

    public void ApplyAllSettings()
    {
        ApplyAudio();
        ApplyDisplay();
        ApplyGraphicsQuality();
    }

    public void ApplyAudio()
    {
        AudioListener.volume = isMuted ? 0f : volume;
    }

    public void ApplyDisplay()
    {
        // 1. Screen effect overlay under UI Canvas for immediate visual brightness & contrast feedback
        if (screenEffectOverlay != null)
        {
            if (brightness < 1.0f)
            {
                // Darken screen towards black
                float blackAlpha = (1.0f - brightness) * 0.95f;
                screenEffectOverlay.color = new Color(0f, 0f, 0f, blackAlpha);
                screenEffectOverlay.gameObject.SetActive(blackAlpha > 0.01f);
            }
            else if (brightness > 1.0f)
            {
                // Brighten screen with soft white ambient overlay
                float whiteAlpha = (brightness - 1.0f) * 0.35f;
                screenEffectOverlay.color = new Color(1f, 1f, 1f, whiteAlpha);
                screenEffectOverlay.gameObject.SetActive(whiteAlpha > 0.01f);
            }
            else
            {
                screenEffectOverlay.color = Color.clear;
                screenEffectOverlay.gameObject.SetActive(false);
            }
        }

        // 2. WebGL CSS filter injection when running in browser
#if UNITY_WEBGL && !UNITY_EDITOR
        try
        {
            string js = $"if (document.getElementById('unity-canvas')) {{ document.getElementById('unity-canvas').style.filter = 'brightness({brightness * 100f}%) contrast({contrast * 100f}%)'; }}";
            Application.ExternalEval(js);
        }
        catch {}
#endif
    }

    public void ApplyGraphicsQuality()
    {
        int qualityLevel = (int)graphicsQuality;
        QualitySettings.SetQualityLevel(qualityLevel, true);

        // URP Render scale & MSAA adjustment
        UniversalRenderPipelineAsset urpAsset = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (urpAsset != null)
        {
            switch (graphicsQuality)
            {
                case GraphicsQuality.Low:
                    urpAsset.renderScale = 0.8f;
                    urpAsset.msaaSampleCount = 1;
                    break;
                case GraphicsQuality.Medium:
                    urpAsset.renderScale = 1.0f;
                    urpAsset.msaaSampleCount = 2;
                    break;
                case GraphicsQuality.High:
                default:
                    urpAsset.renderScale = 1.0f;
                    urpAsset.msaaSampleCount = 4;
                    break;
            }
        }
    }

    public void SaveSettings()
    {
        PlayerPrefs.SetFloat("Settings_Volume", volume);
        PlayerPrefs.SetInt("Settings_IsMuted", isMuted ? 1 : 0);
        PlayerPrefs.SetFloat("Settings_Brightness", brightness);
        PlayerPrefs.SetFloat("Settings_Contrast", contrast);
        PlayerPrefs.SetInt("Settings_GraphicsQuality", (int)graphicsQuality);
        PlayerPrefs.SetInt("Settings_MovementMode", (int)currentMovementMode);
        PlayerPrefs.SetInt("Settings_PrimaryMouse", primaryMouseButton);
        PlayerPrefs.SetInt("Settings_SecondaryMouse", secondaryMouseButton);
        PlayerPrefs.SetInt("Settings_SpecialMouse", specialMouseButton);
        PlayerPrefs.Save();
    }

    public void LoadSettings()
    {
        volume = PlayerPrefs.GetFloat("Settings_Volume", 1.0f);
        isMuted = PlayerPrefs.GetInt("Settings_IsMuted", 0) == 1;
        brightness = PlayerPrefs.GetFloat("Settings_Brightness", 1.0f);
        contrast = PlayerPrefs.GetFloat("Settings_Contrast", 1.0f);
        graphicsQuality = (GraphicsQuality)PlayerPrefs.GetInt("Settings_GraphicsQuality", (int)GraphicsQuality.High);
        currentMovementMode = (MovementMode)PlayerPrefs.GetInt("Settings_MovementMode", (int)MovementMode.Both);
        primaryMouseButton = PlayerPrefs.GetInt("Settings_PrimaryMouse", 0);
        secondaryMouseButton = PlayerPrefs.GetInt("Settings_SecondaryMouse", 1);
        specialMouseButton = PlayerPrefs.GetInt("Settings_SpecialMouse", 2);
    }

    public void ResetToDefaults()
    {
        volume = 1.0f;
        isMuted = false;
        brightness = 1.0f;
        contrast = 1.0f;
        graphicsQuality = GraphicsQuality.High;
        currentMovementMode = MovementMode.Both;
        primaryMouseButton = 0;
        secondaryMouseButton = 1;
        specialMouseButton = 2;

        SaveSettings();
        ApplyAllSettings();
        UpdateUIFromSettings();
        Debug.Log("[SettingsManager] Reset all settings to factory defaults.");
    }

    public void BindUIEvents()
    {
        if (volumeSlider != null)
        {
            volumeSlider.onValueChanged.RemoveAllListeners();
            volumeSlider.onValueChanged.AddListener(SetVolume);
        }
        if (muteButton != null)
        {
            muteButton.onClick.RemoveAllListeners();
            muteButton.onClick.AddListener(ToggleMute);
        }

        if (brightnessSlider != null)
        {
            brightnessSlider.onValueChanged.RemoveAllListeners();
            brightnessSlider.onValueChanged.AddListener(SetBrightness);
        }

        if (contrastSlider != null)
        {
            contrastSlider.onValueChanged.RemoveAllListeners();
            contrastSlider.onValueChanged.AddListener(SetContrast);
        }

        if (qualityLowBtn != null)
        {
            qualityLowBtn.onClick.RemoveAllListeners();
            qualityLowBtn.onClick.AddListener(() => SetGraphicsQuality(0));
        }
        if (qualityMedBtn != null)
        {
            qualityMedBtn.onClick.RemoveAllListeners();
            qualityMedBtn.onClick.AddListener(() => SetGraphicsQuality(1));
        }
        if (qualityHighBtn != null)
        {
            qualityHighBtn.onClick.RemoveAllListeners();
            qualityHighBtn.onClick.AddListener(() => SetGraphicsQuality(2));
        }

        if (moveWasdBtn != null)
        {
            moveWasdBtn.onClick.RemoveAllListeners();
            moveWasdBtn.onClick.AddListener(() => SetMovementMode(0));
        }
        if (moveArrowsBtn != null)
        {
            moveArrowsBtn.onClick.RemoveAllListeners();
            moveArrowsBtn.onClick.AddListener(() => SetMovementMode(1));
        }
        if (moveBothBtn != null)
        {
            moveBothBtn.onClick.RemoveAllListeners();
            moveBothBtn.onClick.AddListener(() => SetMovementMode(2));
        }

        if (mousePrimaryBtn != null)
        {
            mousePrimaryBtn.onClick.RemoveAllListeners();
            mousePrimaryBtn.onClick.AddListener(CyclePrimaryMouseButton);
        }
        if (mouseSecondaryBtn != null)
        {
            mouseSecondaryBtn.onClick.RemoveAllListeners();
            mouseSecondaryBtn.onClick.AddListener(CycleSecondaryMouseButton);
        }
        if (mouseSpecialBtn != null)
        {
            mouseSpecialBtn.onClick.RemoveAllListeners();
            mouseSpecialBtn.onClick.AddListener(CycleSpecialMouseButton);
        }

        if (tabDisplayAudioBtn != null)
        {
            tabDisplayAudioBtn.onClick.RemoveAllListeners();
            tabDisplayAudioBtn.onClick.AddListener(() => SwitchTab(0));
        }
        if (tabControlsBtn != null)
        {
            tabControlsBtn.onClick.RemoveAllListeners();
            tabControlsBtn.onClick.AddListener(() => SwitchTab(1));
        }

        if (backButton != null)
        {
            backButton.onClick.RemoveAllListeners();
            backButton.onClick.AddListener(CloseSettings);
        }
        if (resetDefaultsButton != null)
        {
            resetDefaultsButton.onClick.RemoveAllListeners();
            resetDefaultsButton.onClick.AddListener(ResetToDefaults);
        }
    }

    public void UpdateUIFromSettings()
    {
        if (volumeSlider != null) volumeSlider.value = volume;
        if (volumeValueText != null) volumeValueText.text = $"{Mathf.RoundToInt(volume * 100f)}%";
        UpdateMuteButtonUI();

        if (brightnessSlider != null) brightnessSlider.value = brightness;
        if (brightnessValueText != null) brightnessValueText.text = $"{Mathf.RoundToInt(brightness * 100f)}%";

        if (contrastSlider != null) contrastSlider.value = contrast;
        if (contrastValueText != null) contrastValueText.text = $"{Mathf.RoundToInt(contrast * 100f)}%";

        UpdateQualityButtonsUI();
        UpdateMovementButtonsUI();
        UpdateMouseButtonsUI();
        UpdateKeybindPreviewTable();
    }

    private void UpdateMuteButtonUI()
    {
        if (muteButtonText != null)
        {
            muteButtonText.text = isMuted ? "UNMUTE" : "MUTE";
            muteButtonText.color = isMuted ? new Color(1f, 0.4f, 0.4f, 1f) : Color.white;
        }
    }

    private void UpdateQualityButtonsUI()
    {
        if (qualityLowImg != null)
            qualityLowImg.color = graphicsQuality == GraphicsQuality.Low ? ColorActiveCyan : ColorInactiveCharcoal;
        if (qualityMedImg != null)
            qualityMedImg.color = graphicsQuality == GraphicsQuality.Medium ? ColorActiveCyan : ColorInactiveCharcoal;
        if (qualityHighImg != null)
            qualityHighImg.color = graphicsQuality == GraphicsQuality.High ? ColorActiveCyan : ColorInactiveCharcoal;
    }

    private void UpdateMovementButtonsUI()
    {
        if (moveWasdImg != null)
            moveWasdImg.color = currentMovementMode == MovementMode.WASDOnly ? ColorActiveCyan : ColorInactiveCharcoal;
        if (moveArrowsImg != null)
            moveArrowsImg.color = currentMovementMode == MovementMode.ArrowKeysOnly ? ColorActiveCyan : ColorInactiveCharcoal;
        if (moveBothImg != null)
            moveBothImg.color = currentMovementMode == MovementMode.Both ? ColorActiveCyan : ColorInactiveCharcoal;
    }

    private void UpdateMouseButtonsUI()
    {
        if (mousePrimaryText != null) mousePrimaryText.text = $"PRIMARY: {GetMouseButtonName(primaryMouseButton)}";
        if (mouseSecondaryText != null) mouseSecondaryText.text = $"SECONDARY: {GetMouseButtonName(secondaryMouseButton)}";
        if (mouseSpecialText != null) mouseSpecialText.text = $"SPECIAL: {GetMouseButtonName(specialMouseButton)}";
    }

    public void UpdateKeybindPreviewTable()
    {
        if (keybindPreviewText == null) return;

        string moveStr = currentMovementMode == MovementMode.WASDOnly ? "W / A / S / D" :
                         currentMovementMode == MovementMode.ArrowKeysOnly ? "Arrow Keys (↑ ↓ ← →)" :
                         "W / A / S / D + Arrow Keys";

        string primaryStr = GetMouseButtonName(primaryMouseButton);
        string secondaryStr = GetMouseButtonName(secondaryMouseButton);
        string specialStr = GetMouseButtonName(specialMouseButton);

        keybindPreviewText.text =
            $"<b>ACTIVE CONTROL MAPPINGS</b>\n" +
            $"• <b>Movement:</b> <color=#06B6D4>{moveStr}</color>\n" +
            $"• <b>Primary Action (Beam Pulse):</b> <color=#10B981>{primaryStr}</color>\n" +
            $"• <b>Secondary Action (Tether / EMP):</b> <color=#F59E0B>{secondaryStr}</color>\n" +
            $"• <b>Special Action (Tactical Zoom):</b> <color=#A855F7>{specialStr}</color>\n" +
            $"• <b>System Commands:</b> <color=#94A3B8>[ESC / P] Pause/Resume | [R] Reboot | [SPACE] Confirm</color>";
    }

    public static string GetMouseButtonName(int btnIndex)
    {
        switch (btnIndex)
        {
            case 0: return "Left Click";
            case 1: return "Right Click";
            case 2: return "Middle Click";
            default: return $"Mouse {btnIndex}";
        }
    }
}
