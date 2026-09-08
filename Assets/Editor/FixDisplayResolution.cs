#if UNITY_EDITOR
#pragma warning disable 0618
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GameJam.Editor
{
    public static class FixDisplayResolution
    {
        [MenuItem("GameJam/Enforce Ultra Clean HD Resolution")]
        public static void EnforceUltraCleanHDResolution()
        {
            Debug.Log("[GameJam Resolution] Starting Enforce Ultra Clean HD Resolution...");

            // 1. Force Universal Render Pipeline (URP) to Native 1080p (Stop Screen Downsampling)
            FixUniversalRenderPipelineAssets();

            // 2. Destroy any PixelPerfectCamera component across all scenes/cameras
            RemovePixelPerfectCameras();

            // 3. Regenerate / Fix TextMeshPro Font Quality
            FixTextMeshProFontQuality();

            // 4. Fix Canvas & Camera Orthographic Settings
            FixCameraAndCanvasSettings();

            // 5. Rebuild Clean High-Res Start/Pause/HUD Labels
            FixCanvasTextInstances();

            // Mark active scene dirty and save assets
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();

            Debug.Log("<color=green>[GameJam Resolution] Ultra Clean HD Resolution enforced successfully! All blurry/pixelated downsampling eliminated.</color>");
        }

        public static void FixUniversalRenderPipelineAssets()
        {
            QualitySettings.resolutionScalingFixedDPIFactor = 1.0f;
            QualitySettings.pixelLightCount = 4;

            string[] guids = AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                UniversalRenderPipelineAsset asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
                if (asset != null)
                {
                    asset.renderScale = 1.0f;
                    asset.msaaSampleCount = 4;
                    asset.supportsCameraDepthTexture = true;

                    SerializedObject so = new SerializedObject(asset);
                    SerializedProperty renderScaleProp = so.FindProperty("m_RenderScale");
                    if (renderScaleProp != null) renderScaleProp.floatValue = 1.0f;

                    SerializedProperty msaaProp = so.FindProperty("m_MSAA");
                    if (msaaProp != null) msaaProp.intValue = 4;

                    SerializedProperty depthProp = so.FindProperty("m_RequireDepthTexture");
                    if (depthProp != null) depthProp.boolValue = true;

                    so.ApplyModifiedProperties();
                    EditorUtility.SetDirty(asset);
                    Debug.Log($"[GameJam Resolution] UniversalRenderPipelineAsset at '{path}' updated: renderScale = 1.0f, MSAA = 4, depthTexture = true.");
                }
            }

            // Also check currently assigned RenderPipelineAsset in GraphicsSettings
            if (GraphicsSettings.defaultRenderPipeline is UniversalRenderPipelineAsset currentPipeline)
            {
                currentPipeline.renderScale = 1.0f;
                currentPipeline.msaaSampleCount = 4;
                currentPipeline.supportsCameraDepthTexture = true;
                EditorUtility.SetDirty(currentPipeline);
            }
        }

        public static void RemovePixelPerfectCameras()
        {
            Component[] allComponents = Object.FindObjectsByType<Component>(FindObjectsInactive.Include);
            foreach (Component comp in allComponents)
            {
                if (comp != null && comp.GetType().Name.Contains("PixelPerfectCamera"))
                {
                    Debug.Log($"[GameJam Resolution] Destroying PixelPerfectCamera component on '{comp.gameObject.name}'.");
                    Object.DestroyImmediate(comp);
                }
            }
        }

        public static void FixTextMeshProFontQuality()
        {
            TMP_FontAsset fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
            if (fontAsset == null)
            {
                fontAsset = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            }
            if (fontAsset == null && TMP_Settings.defaultFontAsset != null)
            {
                fontAsset = TMP_Settings.defaultFontAsset;
            }

            if (fontAsset != null)
            {
                // 1. Texture Inspector settings for the font atlas
                if (fontAsset.atlasTexture != null)
                {
                    fontAsset.atlasTexture.filterMode = FilterMode.Bilinear;
                    string atlasPath = AssetDatabase.GetAssetPath(fontAsset.atlasTexture);
                    if (!string.IsNullOrEmpty(atlasPath))
                    {
                        TextureImporter importer = AssetImporter.GetAtPath(atlasPath) as TextureImporter;
                        if (importer != null)
                        {
                            bool modified = false;
                            if (importer.maxTextureSize < 2048)
                            {
                                importer.maxTextureSize = 2048;
                                modified = true;
                            }
                            if (importer.filterMode != FilterMode.Bilinear)
                            {
                                importer.filterMode = FilterMode.Bilinear;
                                modified = true;
                            }
                            if (importer.textureCompression != TextureImporterCompression.Uncompressed)
                            {
                                importer.textureCompression = TextureImporterCompression.Uncompressed;
                                modified = true;
                            }
                            if (modified)
                            {
                                importer.SaveAndReimport();
                                Debug.Log($"[GameJam Resolution] Font atlas texture at '{atlasPath}' reimported (Max Size: 2048, Bilinear, Uncompressed).");
                            }
                        }
                    }
                }

                // 2. Material properties for LiberationSans SDF Material
                Material mat = fontAsset.material;
                if (mat != null)
                {
                    Shader dfShader = Shader.Find("TextMeshPro/Distance Field");
                    if (dfShader != null && mat.shader != dfShader)
                    {
                        mat.shader = dfShader;
                    }

                    if (mat.mainTexture != null)
                    {
                        mat.mainTexture.filterMode = FilterMode.Bilinear;
                    }

                    if (mat.HasProperty("_FaceDilate")) mat.SetFloat("_FaceDilate", 0f);
                    if (mat.HasProperty("_OutlineSoftness")) mat.SetFloat("_OutlineSoftness", 0f);
                    if (mat.HasProperty("_OutlineWidth")) mat.SetFloat("_OutlineWidth", 0f);
                    if (mat.HasProperty("_UnderlayDilate")) mat.SetFloat("_UnderlayDilate", 0f);
                    if (mat.HasProperty("_UnderlaySoftness")) mat.SetFloat("_UnderlaySoftness", 0f);

                    mat.DisableKeyword("UNDERLAY_ON");
                    mat.DisableKeyword("OUTLINE_ON");

                    EditorUtility.SetDirty(mat);
                }

                EditorUtility.SetDirty(fontAsset);
                Debug.Log("[GameJam Resolution] TextMeshPro font asset and material configured for vector SDF sharpness.");
            }
        }

        public static void FixCameraAndCanvasSettings()
        {
            // Main Camera: orthographicSize = 8f, clearFlags = SolidColor
            Camera cam = Camera.main;
            if (cam == null)
            {
                cam = Object.FindAnyObjectByType<Camera>();
            }
            if (cam != null)
            {
                cam.orthographic = true;
                cam.orthographicSize = 8f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                ColorUtility.TryParseHtmlString("#161922", out Color slateNavy);
                cam.backgroundColor = slateNavy;
                EditorUtility.SetDirty(cam);
                Debug.Log("[GameJam Resolution] Main Camera set to orthographicSize = 8f, clearFlags = SolidColor.");
            }

            // UI_Canvas: ScreenSpaceOverlay, CanvasScaler with dynamicPixelsPerUnit = 10.0f
            GameObject canvasObj = GameObject.Find("UI_Canvas") ?? GameObject.Find("Canvas");
            if (canvasObj != null)
            {
                Canvas canvas = canvasObj.GetComponent<Canvas>();
                if (canvas != null)
                {
                    canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                    canvas.sortingOrder = 50;
                    canvas.pixelPerfect = false;
                    EditorUtility.SetDirty(canvas);
                }

                CanvasScaler scaler = canvasObj.GetComponent<CanvasScaler>();
                if (scaler != null)
                {
                    scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                    scaler.referenceResolution = new Vector2(1920f, 1080f);
                    scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                    scaler.matchWidthOrHeight = 0.5f;
                    scaler.referencePixelsPerUnit = 100f;
                    scaler.dynamicPixelsPerUnit = 10.0f; // Critical: forces dynamic text to rasterize at 10x pixel density
                    EditorUtility.SetDirty(scaler);
                    Debug.Log("[GameJam Resolution] UI_Canvas CanvasScaler updated: referenceResolution = (1920, 1080), match = 0.5, dynamicPixelsPerUnit = 10.0f.");
                }
            }
        }

        public static void FixCanvasTextInstances()
        {
            GameObject canvasObj = GameObject.Find("UI_Canvas") ?? GameObject.Find("Canvas");
            if (canvasObj == null) return;

            TMP_FontAsset fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
            if (fontAsset == null) fontAsset = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            if (fontAsset == null) fontAsset = TMP_Settings.defaultFontAsset;

            TextMeshProUGUI[] tmps = canvasObj.GetComponentsInChildren<TextMeshProUGUI>(true);
            foreach (TextMeshProUGUI tmp in tmps)
            {
                if (tmp == null) continue;

                tmp.rectTransform.localScale = Vector3.one;
                if (tmp.transform.parent is RectTransform parentRect)
                {
                    parentRect.localScale = Vector3.one;
                }

                if (fontAsset != null)
                {
                    tmp.font = fontAsset;
                }

                tmp.enableAutoSizing = false;
                tmp.enableWordWrapping = false;
                tmp.overflowMode = TextOverflowModes.Overflow;

                string name = tmp.gameObject.name;
                Transform parent = tmp.transform.parent;
                bool isButton = (parent != null && parent.GetComponent<Button>() != null) || name.StartsWith("Btn_");
                bool isStartBtn = (parent != null && parent.name == "Btn_Start") || name == "Btn_Start";
                bool isTitle = name.Contains("Title") || name.Contains("Header");
                bool isTimerOrScore = name == "Timer_Text" || name == "Score_Text" || name == "GameOverScore_Text" || name == "VictoryScore_Text";

                if (name == "Btn_NextLevel" || (parent != null && parent.name == "Btn_NextLevel"))
                {
                    tmp.fontSize = 20f;
                    tmp.alignment = TextAlignmentOptions.Center;
                }
                else if (name == "Btn_Replay" || (parent != null && parent.name == "Btn_Replay") || name == "Btn_VictoryMenu" || (parent != null && parent.name == "Btn_VictoryMenu"))
                {
                    tmp.fontSize = 18f;
                    tmp.alignment = TextAlignmentOptions.Center;
                }
                else if (parent != null && parent.name == "PausePanel" && isTitle)
                {
                    tmp.fontSize = 46f;
                    tmp.alignment = TextAlignmentOptions.Center;
                }
                else if (parent != null && parent.name == "VictoryPanel" && isTitle)
                {
                    tmp.fontSize = 48f;
                    tmp.alignment = TextAlignmentOptions.Center;
                }
                else if (parent != null && parent.parent != null && parent.parent.name == "PausePanel" && isButton)
                {
                    tmp.fontSize = 20f;
                    tmp.alignment = TextAlignmentOptions.Center;
                }
                else if (isTitle)
                {
                    tmp.fontSize = 56f;
                    tmp.alignment = TextAlignmentOptions.Center;
                }
                else if (isStartBtn)
                {
                    tmp.fontSize = 26f;
                    tmp.alignment = TextAlignmentOptions.Center;
                }
                else if (isButton)
                {
                    tmp.fontSize = 22f;
                    tmp.alignment = TextAlignmentOptions.Center;
                }
                else if (isTimerOrScore)
                {
                    tmp.fontSize = 32f;
                    if (name == "Score_Text")
                    {
                        tmp.alignment = TextAlignmentOptions.Right;
                    }
                    else
                    {
                        tmp.alignment = TextAlignmentOptions.Center;
                    }
                }
                else if (name == "Multiplier_Text")
                {
                    tmp.fontSize = 22f;
                    tmp.alignment = TextAlignmentOptions.Right;
                }
                else if (name == "Hijack_Text")
                {
                    tmp.fontSize = 22f;
                    tmp.alignment = TextAlignmentOptions.Left;
                }
                else if (name == "HP_Text")
                {
                    tmp.fontSize = 20f;
                    tmp.alignment = TextAlignmentOptions.Center;
                }
                else if (name == "Subtitle_Text")
                {
                    tmp.fontSize = (parent != null && parent.name == "VictoryPanel") ? 22f : 20f;
                    tmp.alignment = TextAlignmentOptions.Center;
                }
                else
                {
                    tmp.fontSize = 22f;
                    tmp.alignment = TextAlignmentOptions.Center;
                }

                tmp.fontStyle = FontStyles.Bold;
                tmp.raycastTarget = false;
                EditorUtility.SetDirty(tmp);
            }

            // Ensure all RectTransforms on Canvas are strictly Vector3.one
            RectTransform[] allRects = canvasObj.GetComponentsInChildren<RectTransform>(true);
            foreach (RectTransform rt in allRects)
            {
                rt.localScale = Vector3.one;
            }

            Debug.Log($"[GameJam Resolution] Configured {tmps.Length} TextMeshProUGUI elements with native crisp font sizes (Auto-size disabled, overflow = Overflow, scale = (1,1,1)).");
        }
    }
}
#endif
