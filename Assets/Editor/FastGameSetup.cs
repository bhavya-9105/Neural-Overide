#if UNITY_EDITOR
#pragma warning disable 0618
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GameJam.Editor
{
    [InitializeOnLoad]
    public static class FastGameSetup
    {
        static FastGameSetup()
        {
            EditorApplication.delayCall += AutoRunOnCompile;
        }

        private static void AutoRunOnCompile()
        {
            if (!SessionState.GetBool("FastGameSetup_UIRebuild_Done_v20", false))
            {
                SessionState.SetBool("FastGameSetup_UIRebuild_Done_v20", true);
                SetupSceneAndSprites();
            }
        }

        public static void SetupScene() => SetupSceneAndSprites();

        [MenuItem("GameJam/Setup Scene & Sprites")]
        public static void SetupSceneAndSprites()
        {
            Debug.Log("[GameJam] Starting Minimalist Setup Scene & Sprites...");

            string scenePath = "Assets/Scenes/SampleScene.unity";
            Scene activeScene = EditorSceneManager.GetActiveScene();
            if (activeScene.path != scenePath)
            {
                if (File.Exists(scenePath))
                {
                    EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                }
            }

            // Enforce Ultra Clean HD Resolution (URP 1080p, TMPro SDF 2048 Uncompressed, no PixelPerfect, etc.)
            FixDisplayResolution.EnforceUltraCleanHDResolution();

            // Configure QualitySettings for crisp rendering
            QualitySettings.resolutionScalingFixedDPIFactor = 1.0f;
            QualitySettings.pixelLightCount = 4;

            // 1. Configure and reimport all textures in Kenney Tiny Dungeon folder
            string kenneyFolder = LocateKenneyFolder();
            ReimportKenneyTextures(kenneyFolder);

            // 2. Ensure "Player" and "Enemy" tags, and "Enemy" layer in TagManager
            int enemyLayerIndex = EnsureTagsAndLayers();

            // 3. Spawn or configure Player GameObject (Continuous Rigidbody2D, CircleCollider2D, LineRenderer 0.08f Sprites/Default)
            GameObject playerObj = SetupPlayer(kenneyFolder, enemyLayerIndex);

            // 4. Setup Camera with CameraFollow2D strictly targeting the Player in LateUpdate
            SetupCameraFollow(playerObj);

            // 5. Generates missing enemy prefabs in Assets/Prefabs/Enemies/
            SetupEnemyPrefabs(kenneyFolder, enemyLayerIndex, out GameObject chaserPrefab, out GameObject sniperPrefab, out GameObject projectilePrefab);

            // 6. Spawn or configure WaveSpawner GameObject and wire prefab references
            SetupWaveSpawner(playerObj, chaserPrefab, sniperPrefab);

            // 7. Generate arena boundary walls and visible yellow perimeter line (36x20 arena)
            SetupArenaBoundaries(36f, 20f);

            // 8. Build Canvas UI (StartPanel, HUD with Military Radar Mini-Map, PausePanel, VictoryPanel) & GameManager
            SetupGameUIAndManager(playerObj);

            // 9. Strict Scene Pruning: enforce ONLY the 7 required GameObjects
            PruneSceneHierarchy();

            // Mark active scene dirty and save scenes and assets so changes persist
            Scene currentScene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(currentScene);
            if (!string.IsNullOrEmpty(currentScene.path))
            {
                EditorSceneManager.SaveScene(currentScene);
            }
            else
            {
                EditorSceneManager.SaveScene(currentScene, "Assets/Scenes/SampleScene.unity");
            }
            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();

            Selection.activeGameObject = playerObj;
            Debug.Log("<color=green>[GameJam] Setup Scene & Sprites completed successfully! All prefabs, radar mini-map, and UI wired.</color>");
        }

        public static void WireVictoryAndNextLevelButton()
        {
            GameManager gm = Object.FindAnyObjectByType<GameManager>(FindObjectsInactive.Include);
            if (gm == null)
            {
                Debug.LogError("[GameJam] No GameManager found in scene!");
                return;
            }

            GameObject victoryPanelObj = gm.victoryPanel;
            if (victoryPanelObj == null)
            {
                Transform[] transforms = Resources.FindObjectsOfTypeAll<Transform>();
                foreach (Transform t in transforms)
                {
                    if (t.name == "VictoryPanel" && t.gameObject.scene.isLoaded)
                    {
                        victoryPanelObj = t.gameObject;
                        break;
                    }
                }
            }

            if (victoryPanelObj == null)
            {
                Debug.LogError("[GameJam] VictoryPanel not found in scene!");
                return;
            }

            Transform nextLvlTrans = victoryPanelObj.transform.Find("Btn_NextLevel");
            if (nextLvlTrans == null)
            {
                Debug.LogError("[GameJam] Btn_NextLevel not found under VictoryPanel!");
                return;
            }

            Button btn = nextLvlTrans.GetComponent<Button>();
            if (btn == null)
            {
                btn = nextLvlTrans.gameObject.AddComponent<Button>();
            }

            ContentSizeFitter csf = nextLvlTrans.GetComponent<ContentSizeFitter>();
            if (csf != null) Object.DestroyImmediate(csf);
            RectTransform nextLvlRt = nextLvlTrans.GetComponent<RectTransform>();
            if (nextLvlRt != null) nextLvlRt.sizeDelta = new Vector2(520f, 60f);

            btn.transition = Selectable.Transition.ColorTint;

            while (btn.onClick.GetPersistentEventCount() > 0)
            {
                UnityEventTools.RemovePersistentListener(btn.onClick, 0);
            }
            UnityEventTools.AddPersistentListener(btn.onClick, gm.NextLevel);

            gm.nextLevelBtn = nextLvlTrans.gameObject;
            gm.NextLevelBtn = nextLvlTrans.gameObject;
            gm.nextLevelButton = btn;
            gm.nextLevelButtonText = nextLvlTrans.GetComponentInChildren<TextMeshProUGUI>();

            SerializedObject gmSo = new SerializedObject(gm);
            SetSerializedProp(gmSo, "nextLevelBtn", nextLvlTrans.gameObject);
            SetSerializedProp(gmSo, "nextLevelButton", btn);
            if (gm.nextLevelButtonText != null)
            {
                SetSerializedProp(gmSo, "nextLevelButtonText", gm.nextLevelButtonText);
            }
            gmSo.ApplyModifiedProperties();

            EditorUtility.SetDirty(btn);
            EditorUtility.SetDirty(gm);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();

            Debug.Log("<color=green>[GameJam] Btn_NextLevel persistent listener and GameManager.nextLevelBtn wired successfully!</color>");
        }

        public static void BuildWindowsStandalone()
        {
            Debug.Log("[GameJam] Starting Windows Standalone Build...");

            // 1. Sets active scene in BuildSettings
            EditorSceneManager.SaveOpenScenes();
            string currentScenePath = EditorSceneManager.GetActiveScene().path;

            if (string.IsNullOrEmpty(currentScenePath))
            {
                currentScenePath = "Assets/Scenes/SampleScene.unity";
                if (!File.Exists(currentScenePath))
                {
                    string[] sceneGuids = AssetDatabase.FindAssets("t:Scene");
                    if (sceneGuids.Length > 0)
                    {
                        currentScenePath = AssetDatabase.GUIDToAssetPath(sceneGuids[0]);
                    }
                }
            }

            EditorBuildSettingsScene[] scenes = new[] { new EditorBuildSettingsScene(currentScenePath, true) };
            EditorBuildSettings.scenes = scenes;

            // 2. Prepare output directory
            string outputDir = "Builds/Windows";
            if (!Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            string outputPath = Path.Combine(outputDir, "NeuralOverride.exe").Replace('\\', '/');

            // 3. Configures target as BuildTarget.StandaloneWindows64
            BuildPlayerOptions buildPlayerOptions = new BuildPlayerOptions
            {
                scenes = new[] { currentScenePath },
                locationPathName = outputPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            };

            Debug.Log($"[GameJam] Building executable to: {outputPath} ...");
            UnityEditor.Build.Reporting.BuildReport report = BuildPipeline.BuildPlayer(buildPlayerOptions);
            UnityEditor.Build.Reporting.BuildSummary summary = report.summary;

            if (summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
            {
                Debug.Log($"<color=green>[GameJam] Windows Standalone build succeeded! File created: {outputPath} ({summary.totalSize} bytes)</color>");
            }
            else if (summary.result == UnityEditor.Build.Reporting.BuildResult.Failed)
            {
                Debug.LogError($"<color=red>[GameJam] Windows Standalone build failed with {summary.totalErrors} errors.</color>");
            }
        }

        private static string LocateKenneyFolder()
        {
            string defaultPath = "Assets/kenney_tiny-dungeon";
            if (AssetDatabase.IsValidFolder(defaultPath))
            {
                return defaultPath;
            }

            string[] guids = AssetDatabase.FindAssets("kenney_tiny-dungeon t:Folder");
            if (guids.Length > 0)
            {
                return AssetDatabase.GUIDToAssetPath(guids[0]);
            }

            guids = AssetDatabase.FindAssets("tiny-dungeon t:Folder");
            if (guids.Length > 0)
            {
                return AssetDatabase.GUIDToAssetPath(guids[0]);
            }

            return defaultPath;
        }

        private static void ReimportKenneyTextures(string folderPath)
        {
            if (!AssetDatabase.IsValidFolder(folderPath))
            {
                Debug.LogWarning($"[GameJam] Folder {folderPath} not found for texture reimport.");
                return;
            }

            string[] textureGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { folderPath });
            int reimportedCount = 0;

            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (string guid in textureGuids)
                {
                    string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                    TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
                    if (importer != null)
                    {
                        importer.textureType = TextureImporterType.Sprite;
                        importer.spritePixelsPerUnit = 16f;
                        importer.filterMode = FilterMode.Point;
                        importer.textureCompression = TextureImporterCompression.Uncompressed;
                        importer.SaveAndReimport();
                        reimportedCount++;
                    }
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.Refresh();
            }

            Debug.Log($"[GameJam] Reimported {reimportedCount} textures in {folderPath} as 16 PPU Point Uncompressed Sprites.");
        }

        private static int EnsureTagsAndLayers()
        {
            SerializedObject tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);

            EnsureTag(tagManager, "Player");
            EnsureTag(tagManager, "Enemy");

            int enemyLayerIndex = EnsureLayer(tagManager, "Enemies");
            EnsureLayer(tagManager, "MiniMap");
            EnsureLayer(tagManager, "Boundary");

            tagManager.ApplyModifiedProperties();
            tagManager.Update();
            AssetDatabase.SaveAssets();

            int enemiesLayer = LayerMask.NameToLayer("Enemies");
            if (enemiesLayer != -1)
            {
                enemyLayerIndex = enemiesLayer;
            }

            if (enemyLayerIndex == -1)
            {
                enemyLayerIndex = 11;
            }

            Debug.Log($"[GameJam] Tags & Layers verified. Layer 'Enemies' configured at index {enemyLayerIndex}.");
            return enemyLayerIndex;
        }

        private static int EnsureLayer(SerializedObject tagManager, string layerName)
        {
            SerializedProperty layersProp = tagManager.FindProperty("layers");
            for (int i = 8; i < 32; i++)
            {
                SerializedProperty layerProp = layersProp.GetArrayElementAtIndex(i);
                if (layerProp != null && layerProp.stringValue == layerName)
                {
                    return i;
                }
            }

            for (int i = 8; i < 32; i++)
            {
                SerializedProperty layerProp = layersProp.GetArrayElementAtIndex(i);
                if (layerProp != null && string.IsNullOrEmpty(layerProp.stringValue))
                {
                    layerProp.stringValue = layerName;
                    return i;
                }
            }

            return -1;
        }

        private static void EnsureTag(SerializedObject tagManager, string tagName)
        {
            foreach (string tag in UnityEditorInternal.InternalEditorUtility.tags)
            {
                if (tag == tagName)
                {
                    return;
                }
            }

            SerializedProperty tagsProp = tagManager.FindProperty("tags");
            for (int i = 0; i < tagsProp.arraySize; i++)
            {
                if (tagsProp.GetArrayElementAtIndex(i).stringValue == tagName)
                {
                    return;
                }
            }

            tagsProp.InsertArrayElementAtIndex(tagsProp.arraySize);
            tagsProp.GetArrayElementAtIndex(tagsProp.arraySize - 1).stringValue = tagName;
        }

        private static void SetupCameraFollow(GameObject playerObj)
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                cam = Object.FindAnyObjectByType<Camera>();
                if (cam == null)
                {
                    GameObject camObj = new GameObject("Main Camera");
                    Undo.RegisterCreatedObjectUndo(camObj, "Create Main Camera");
                    cam = camObj.AddComponent<Camera>();
                    camObj.tag = "MainCamera";
                    camObj.transform.position = new Vector3(0f, 0f, -10f);
                }
            }

            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            ColorUtility.TryParseHtmlString("#071322", out Color deepDarkNavy);
            cam.backgroundColor = deepDarkNavy;
            cam.orthographicSize = 8f;

            // Destroy any PixelPerfectCamera component
            Component[] ppcs = cam.GetComponents<Component>();
            foreach (Component c in ppcs)
            {
                if (c != null && c.GetType().Name.Contains("PixelPerfectCamera"))
                {
                    Object.DestroyImmediate(c);
                }
            }

            CameraFollow2D camFollow = cam.GetComponent<CameraFollow2D>();
            if (camFollow == null)
            {
                camFollow = cam.gameObject.AddComponent<CameraFollow2D>();
            }

            if (playerObj != null)
            {
                camFollow.Target = playerObj.transform;
            }
            camFollow.LookAheadDistance = 1.8f;
            camFollow.SmoothTime = 0.12f;
            camFollow.ClampToBounds = true;
            camFollow.MinBounds = new Vector2(-12f, -6f);
            camFollow.MaxBounds = new Vector2(12f, 6f);

            // Ensure no secondary MiniMapCamera or Render Texture exists (zero camera overhead)
            RemoveMiniMapCamera(cam);

            Debug.Log("[GameJam] Camera configured with CameraFollow2D tracking Player.");
        }

        private static void RemoveMiniMapCamera(Camera mainCam)
        {
            if (mainCam != null)
            {
                Transform child = mainCam.transform.Find("MiniMapCamera");
                if (child != null)
                {
                    Object.DestroyImmediate(child.gameObject);
                }
            }

            GameObject existingRoot = GameObject.Find("MiniMapCamera");
            if (existingRoot != null)
            {
                Object.DestroyImmediate(existingRoot);
            }

            string maskPath = "Assets/UI/RadarMask.png";
            if (File.Exists(maskPath))
            {
                AssetDatabase.DeleteAsset(maskPath);
            }
            string crossPath = "Assets/UI/RadarCrosshairs.png";
            if (File.Exists(crossPath))
            {
                AssetDatabase.DeleteAsset(crossPath);
            }
            Debug.Log("[GameJam] Secondary MiniMapCamera, RenderTexture, and Radar UI assets completely removed.");
        }


        private static void GetOrCreatePlayerDirectionalSprites(out Sprite front, out Sprite back, out Sprite side)
        {
            string playerFolder = "Assets/Sprites/Player";
            if (!AssetDatabase.IsValidFolder("Assets/Sprites"))
            {
                AssetDatabase.CreateFolder("Assets", "Sprites");
            }
            if (!AssetDatabase.IsValidFolder(playerFolder))
            {
                AssetDatabase.CreateFolder("Assets/Sprites", "Player");
            }

            string frontPath = $"{playerFolder}/Player_Front.png";
            string backPath = $"{playerFolder}/Player_Back.png";
            string sidePath = $"{playerFolder}/Player_Side.png";

            front = AssetDatabase.LoadAssetAtPath<Sprite>(frontPath);
            back = AssetDatabase.LoadAssetAtPath<Sprite>(backPath);
            side = AssetDatabase.LoadAssetAtPath<Sprite>(sidePath);

            if (front != null && back != null && side != null)
            {
                return;
            }

            // Create pixel-art textures (16x16)
            Color cClear = Color.clear;
            Color cDark = new Color(15f / 255f, 23f / 255f, 42f / 255f, 1f);       // #0F172A Boot soles
            Color cArmorDark = new Color(30f / 255f, 41f / 255f, 59f / 255f, 1f);   // #1E293B Dark Charcoal Suit
            Color cArmorMid = new Color(15f / 255f, 118f / 255f, 110f / 255f, 1f);  // #0F766E Deep Teal Armor
            Color cArmorBright = new Color(13f / 255f, 148f / 255f, 136f / 255f, 1f);// #0D9488 Vibrant Teal Plates
            Color cCyanGlow = new Color(34f / 255f, 211f / 255f, 238f / 255f, 1f);  // #22D3EE Cyan Visor & Energy
            Color cElectricCyan = new Color(6f / 255f, 182f / 255f, 212f / 255f, 1f);// #06B6D4 Electric Cyan
            Color cTrimSilver = new Color(148f / 255f, 163f / 255f, 184f / 255f, 1f);// #94A3B8 Tech Silver
            Color cGunMetal = new Color(51f / 255f, 65f / 255f, 85f / 255f, 1f);    // #334155 Weapon Steel
            Color cHighlightWhite = new Color(1f, 1f, 1f, 1f);                       // Visor reflection

            // --- 1. FRONT SPRITE (Facing Down) ---
            Texture2D texFront = new Texture2D(16, 16, TextureFormat.RGBA32, false);
            texFront.filterMode = FilterMode.Point;
            Color[] pFront = new Color[16 * 16];
            for (int i = 0; i < pFront.Length; i++) pFront[i] = cClear;

            void SetPixelFront(int x, int y, Color c) { if (x >= 0 && x < 16 && y >= 0 && y < 16) pFront[y * 16 + x] = c; }
            void FillRowFront(int y, int xStart, int xEnd, Color c) { for (int x = xStart; x <= xEnd; x++) SetPixelFront(x, y, c); }

            FillRowFront(15, 6, 9, cArmorBright);
            FillRowFront(14, 5, 10, cArmorMid);
            FillRowFront(13, 5, 10, cArmorMid);
            SetPixelFront(7, 13, cTrimSilver); SetPixelFront(8, 13, cTrimSilver);
            FillRowFront(12, 5, 10, cCyanGlow);
            SetPixelFront(6, 12, cHighlightWhite);
            FillRowFront(11, 6, 9, cArmorDark);
            FillRowFront(10, 4, 11, cArmorMid);
            SetPixelFront(7, 10, cArmorDark); SetPixelFront(8, 10, cArmorDark);
            FillRowFront(9, 3, 5, cArmorDark);
            FillRowFront(9, 6, 9, cArmorBright);
            FillRowFront(9, 10, 12, cArmorDark);
            SetPixelFront(7, 9, cCyanGlow); SetPixelFront(8, 9, cCyanGlow);
            FillRowFront(8, 4, 11, cArmorDark);
            FillRowFront(8, 7, 8, cGunMetal);
            SetPixelFront(7, 8, cElectricCyan); SetPixelFront(8, 8, cElectricCyan);
            FillRowFront(7, 5, 6, cArmorDark);
            FillRowFront(7, 9, 10, cArmorDark);
            SetPixelFront(7, 7, cTrimSilver); SetPixelFront(8, 7, cTrimSilver);
            FillRowFront(6, 5, 6, cArmorMid);
            FillRowFront(6, 9, 10, cArmorMid);
            FillRowFront(5, 5, 6, cArmorBright);
            FillRowFront(5, 9, 10, cArmorBright);
            FillRowFront(4, 5, 6, cArmorMid);
            FillRowFront(4, 9, 10, cArmorMid);
            FillRowFront(3, 4, 6, cArmorDark);
            FillRowFront(3, 9, 11, cArmorDark);
            FillRowFront(2, 4, 6, cDark);
            FillRowFront(2, 9, 11, cDark);

            texFront.SetPixels(pFront);
            texFront.Apply();
            SaveTextureAndImportSprite(texFront, frontPath);

            // --- 2. BACK SPRITE (Facing Up) ---
            Texture2D texBack = new Texture2D(16, 16, TextureFormat.RGBA32, false);
            texBack.filterMode = FilterMode.Point;
            Color[] pBack = new Color[16 * 16];
            for (int i = 0; i < pBack.Length; i++) pBack[i] = cClear;

            void SetPixelBack(int x, int y, Color c) { if (x >= 0 && x < 16 && y >= 0 && y < 16) pBack[y * 16 + x] = c; }
            void FillRowBack(int y, int xStart, int xEnd, Color c) { for (int x = xStart; x <= xEnd; x++) SetPixelBack(x, y, c); }

            FillRowBack(15, 6, 9, cArmorBright);
            FillRowBack(14, 5, 10, cArmorMid);
            FillRowBack(13, 5, 10, cArmorMid);
            SetPixelBack(7, 13, cArmorDark); SetPixelBack(8, 13, cArmorDark);
            FillRowBack(12, 5, 10, cArmorMid);
            FillRowBack(11, 6, 9, cArmorDark);
            FillRowBack(10, 4, 11, cArmorMid);
            SetPixelBack(6, 10, cTrimSilver); SetPixelBack(9, 10, cTrimSilver);
            FillRowBack(9, 4, 11, cArmorDark);
            SetPixelBack(6, 9, cCyanGlow); SetPixelBack(9, 9, cCyanGlow);
            SetPixelBack(7, 9, cElectricCyan); SetPixelBack(8, 9, cElectricCyan);
            FillRowBack(8, 5, 10, cGunMetal);
            SetPixelBack(6, 8, cHighlightWhite); SetPixelBack(9, 8, cHighlightWhite);
            FillRowBack(7, 5, 10, cArmorDark);
            SetPixelBack(6, 7, cArmorBright); SetPixelBack(9, 7, cArmorBright);
            FillRowBack(6, 5, 6, cArmorMid);
            FillRowBack(6, 9, 10, cArmorMid);
            FillRowBack(5, 5, 6, cArmorBright);
            FillRowBack(5, 9, 10, cArmorBright);
            FillRowBack(4, 5, 6, cArmorMid);
            FillRowBack(4, 9, 10, cArmorMid);
            FillRowBack(3, 4, 6, cArmorDark);
            FillRowBack(3, 9, 11, cArmorDark);
            FillRowBack(2, 4, 6, cDark);
            FillRowBack(2, 9, 11, cDark);

            texBack.SetPixels(pBack);
            texBack.Apply();
            SaveTextureAndImportSprite(texBack, backPath);

            // --- 3. SIDE SPRITE (Facing Right) ---
            Texture2D texSide = new Texture2D(16, 16, TextureFormat.RGBA32, false);
            texSide.filterMode = FilterMode.Point;
            Color[] pSide = new Color[16 * 16];
            for (int i = 0; i < pSide.Length; i++) pSide[i] = cClear;

            void SetPixelSide(int x, int y, Color c) { if (x >= 0 && x < 16 && y >= 0 && y < 16) pSide[y * 16 + x] = c; }
            void FillRowSide(int y, int xStart, int xEnd, Color c) { for (int x = xStart; x <= xEnd; x++) SetPixelSide(x, y, c); }

            FillRowSide(15, 5, 8, cArmorBright);
            FillRowSide(14, 4, 9, cArmorMid);
            FillRowSide(13, 4, 8, cArmorMid);
            FillRowSide(13, 9, 10, cCyanGlow);
            FillRowSide(12, 4, 7, cArmorMid);
            FillRowSide(12, 8, 11, cCyanGlow);
            SetPixelSide(10, 12, cHighlightWhite);
            FillRowSide(11, 5, 10, cArmorDark);
            FillRowSide(10, 4, 9, cArmorMid);
            SetPixelSide(5, 10, cTrimSilver);
            FillRowSide(9, 3, 9, cArmorDark);
            FillRowSide(9, 10, 13, cGunMetal);
            SetPixelSide(14, 9, cCyanGlow);
            FillRowSide(8, 4, 8, cArmorDark);
            FillRowSide(8, 9, 13, cGunMetal);
            SetPixelSide(14, 8, cElectricCyan);
            FillRowSide(7, 5, 9, cArmorDark);
            SetPixelSide(7, 7, cArmorBright);
            FillRowSide(6, 4, 6, cArmorMid);
            FillRowSide(6, 8, 10, cArmorMid);
            FillRowSide(5, 5, 6, cArmorBright);
            FillRowSide(5, 9, 10, cArmorBright);
            FillRowSide(4, 5, 6, cArmorMid);
            FillRowSide(4, 8, 9, cArmorMid);
            FillRowSide(3, 4, 7, cArmorDark);
            FillRowSide(3, 8, 11, cArmorDark);
            FillRowSide(2, 3, 7, cDark);
            FillRowSide(2, 8, 12, cDark);

            texSide.SetPixels(pSide);
            texSide.Apply();
            SaveTextureAndImportSprite(texSide, sidePath);

            AssetDatabase.Refresh();
            front = AssetDatabase.LoadAssetAtPath<Sprite>(frontPath);
            back = AssetDatabase.LoadAssetAtPath<Sprite>(backPath);
            side = AssetDatabase.LoadAssetAtPath<Sprite>(sidePath);
        }

        private static void SaveTextureAndImportSprite(Texture2D tex, string assetPath)
        {
            byte[] pngBytes = tex.EncodeToPNG();
            string fullPath = Path.Combine(Application.dataPath, assetPath.Substring("Assets/".Length));
            string dir = Path.GetDirectoryName(fullPath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllBytes(fullPath, pngBytes);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spritePixelsPerUnit = 16f;
                importer.filterMode = FilterMode.Point;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.SaveAndReimport();
            }
        }

        private static Sprite GetOrCreateWeaponBlasterSprite()
        {
            string playerFolder = "Assets/Sprites/Player";
            if (!AssetDatabase.IsValidFolder("Assets/Sprites"))
            {
                AssetDatabase.CreateFolder("Assets", "Sprites");
            }
            if (!AssetDatabase.IsValidFolder(playerFolder))
            {
                AssetDatabase.CreateFolder("Assets/Sprites", "Player");
            }

            string weaponPath = $"{playerFolder}/Weapon_Blaster.png";
            Sprite sp = AssetDatabase.LoadAssetAtPath<Sprite>(weaponPath);
            if (sp != null) return sp;

            Texture2D tex = new Texture2D(16, 16, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;
            Color[] pixels = new Color[16 * 16];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.clear;

            void SetPixel(int x, int y, Color c) { if (x >= 0 && x < 16 && y >= 0 && y < 16) pixels[y * 16 + x] = c; }
            void FillRow(int y, int xStart, int xEnd, Color c) { for (int x = xStart; x <= xEnd; x++) SetPixel(x, y, c); }

            Color cDarkMetal = new Color(30f / 255f, 41f / 255f, 59f / 255f, 1f);     // #1E293B Dark Slate
            Color cGunMetal = new Color(51f / 255f, 65f / 255f, 85f / 255f, 1f);      // #334155 Gunmetal
            Color cChrome = new Color(148f / 255f, 163f / 255f, 184f / 255f, 1f);     // #94A3B8 Tech Silver
            Color cCyanBright = new Color(34f / 255f, 211f / 255f, 238f / 255f, 1f);  // #22D3EE Cyan Glow
            Color cCyanHot = new Color(207f / 255f, 250f / 255f, 254f / 255f, 1f);   // #CFFAFE White-Cyan Core
            Color cGrip = new Color(15f / 255f, 23f / 255f, 42f / 255f, 1f);          // #0F172A Grip

            // Right-facing Sci-Fi Plasma Blaster:
            // Grip angled down at x: 2..5, y: 3..7
            FillRow(3, 3, 5, cGrip);
            FillRow(4, 3, 5, cDarkMetal);
            FillRow(5, 4, 6, cDarkMetal);
            FillRow(6, 4, 7, cGunMetal);
            FillRow(7, 3, 8, cChrome);

            // Receiver / Upper Body at y: 8..10, x: 3..12
            FillRow(8, 3, 12, cDarkMetal);
            FillRow(9, 3, 13, cGunMetal);
            FillRow(10, 4, 11, cChrome);

            // Plasma Barrel / Core at y: 8..9, x: 9..14
            FillRow(8, 10, 13, cCyanBright);
            FillRow(9, 10, 14, cCyanHot);
            SetPixel(14, 8, cCyanBright);
            SetPixel(15, 9, cCyanBright); // Muzzle flash tip

            // Top rail / Scope at y: 11, x: 5..9
            FillRow(11, 5, 9, cDarkMetal);
            SetPixel(6, 12, cCyanBright); // Scope dot

            tex.SetPixels(pixels);
            tex.Apply();

            SaveTextureAndImportSprite(tex, weaponPath);
            AssetDatabase.Refresh();
            return AssetDatabase.LoadAssetAtPath<Sprite>(weaponPath);
        }

        private static Sprite GetOrCreateTacticalReticleSprite()
        {
            string uiFolder = "Assets/UI";
            if (!AssetDatabase.IsValidFolder(uiFolder))
            {
                AssetDatabase.CreateFolder("Assets", "UI");
            }

            string reticlePath = $"{uiFolder}/TacticalReticle.png";
            Sprite sp = AssetDatabase.LoadAssetAtPath<Sprite>(reticlePath);
            if (sp != null) return sp;

            int size = 64;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            Color[] pixels = new Color[size * size];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.clear;

            float center = (size - 1) * 0.5f;
            ColorUtility.TryParseHtmlString("#00FFFF", out Color cyan);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - center;
                    float dy = y - center;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = 0f;

                    // 1. Center precision dot (radius 1.8px)
                    if (dist <= 1.8f)
                    {
                        a = Mathf.Max(a, Mathf.Clamp01((1.8f - dist) / 0.8f) * 0.95f);
                    }

                    // 2. Crosshair 4 arms (gap from 5px to 22px)
                    float absDx = Mathf.Abs(dx);
                    float absDy = Mathf.Abs(dy);

                    if (dist >= 5f && dist <= 22f)
                    {
                        // Horizontal arms
                        if (absDy <= 1.2f && absDx >= 5f && absDx <= 22f)
                        {
                            a = Mathf.Max(a, Mathf.Clamp01((1.2f - absDy) / 0.8f) * 0.9f);
                        }
                        // Vertical arms
                        if (absDx <= 1.2f && absDy >= 5f && absDy <= 22f)
                        {
                            a = Mathf.Max(a, Mathf.Clamp01((1.2f - absDx) / 0.8f) * 0.9f);
                        }
                    }

                    // 3. Precision tick marks at radius 14px
                    if (Mathf.Abs(absDx - 14f) <= 1.0f && absDy <= 3.5f)
                    {
                        a = Mathf.Max(a, 0.85f);
                    }
                    if (Mathf.Abs(absDy - 14f) <= 1.0f && absDx <= 3.5f)
                    {
                        a = Mathf.Max(a, 0.85f);
                    }

                    // 4. Subtle outer corner bracket notches at radius 23..26px
                    if (dist >= 23f && dist <= 26f)
                    {
                        float ringDist = Mathf.Abs(dist - 24.5f);
                        float angle = Mathf.Abs(Mathf.Atan2(dy, dx) * Mathf.Rad2Deg);
                        if (Mathf.Abs(angle - 45f) <= 15f || Mathf.Abs(angle - 135f) <= 15f)
                        {
                            a = Mathf.Max(a, Mathf.Clamp01((1.5f - ringDist) / 1.0f) * 0.7f);
                        }
                    }

                    if (a > 0f)
                    {
                        pixels[y * size + x] = new Color(cyan.r, cyan.g, cyan.b, a);
                    }
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();

            byte[] pngBytes = tex.EncodeToPNG();
            string fullPath = Path.Combine(Application.dataPath, "UI/TacticalReticle.png");
            string dir = Path.GetDirectoryName(fullPath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllBytes(fullPath, pngBytes);
            AssetDatabase.ImportAsset(reticlePath, ImportAssetOptions.ForceUpdate);

            TextureImporter importer = AssetImporter.GetAtPath(reticlePath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spritePixelsPerUnit = 100f;
                importer.filterMode = FilterMode.Bilinear;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Sprite>(reticlePath);
        }

        private static void SaveTextureAndImportUISprite(Texture2D tex, string assetPath, Vector4? border = null)
        {
            byte[] pngBytes = tex.EncodeToPNG();
            string fullPath = Path.Combine(Application.dataPath, assetPath.Substring("Assets/".Length));
            string dir = Path.GetDirectoryName(fullPath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllBytes(fullPath, pngBytes);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spritePixelsPerUnit = 100f;
                importer.filterMode = FilterMode.Bilinear;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.wrapMode = TextureWrapMode.Clamp;
                if (border.HasValue)
                {
                    importer.spriteBorder = border.Value;
                }
                importer.SaveAndReimport();
            }
        }

        private static Sprite CreateRoundedRectSprite(string assetPath, int width, int height, Color fillColor, Color borderColor, float borderWidth = 1.5f, float cornerRadius = 8f, bool verticalGradient = false, Color? gradientTopColor = null)
        {
            Sprite existing = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            if (existing != null) return existing;

            Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            Color[] pixels = new Color[width * height];

            for (int y = 0; y < height; y++)
            {
                float tY = (float)y / (height - 1);
                Color currentFill = verticalGradient && gradientTopColor.HasValue
                    ? Color.Lerp(fillColor, gradientTopColor.Value, tY)
                    : fillColor;

                for (int x = 0; x < width; x++)
                {
                    float dx = Mathf.Max(0f, Mathf.Max(cornerRadius - x, x - (width - 1 - cornerRadius)));
                    float dy = Mathf.Max(0f, Mathf.Max(cornerRadius - y, y - (height - 1 - cornerRadius)));
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);

                    if (dist > cornerRadius + 0.5f)
                    {
                        pixels[y * width + x] = Color.clear;
                    }
                    else if (dist > cornerRadius - borderWidth)
                    {
                        float alpha = Mathf.Clamp01(cornerRadius + 0.5f - dist);
                        pixels[y * width + x] = new Color(borderColor.r, borderColor.g, borderColor.b, borderColor.a * alpha);
                    }
                    else if (x < borderWidth || x >= width - borderWidth || y < borderWidth || y >= height - borderWidth)
                    {
                        pixels[y * width + x] = borderColor;
                    }
                    else
                    {
                        pixels[y * width + x] = currentFill;
                    }
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();

            SaveTextureAndImportUISprite(tex, assetPath, new Vector4(12, 12, 12, 12));
            AssetDatabase.Refresh();
            return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        }

        private static Sprite GetOrCreateCyberButtonSprite()
        {
            string path = "Assets/UI/CyberButton_Cyan.png";
            Color darkFill = new Color(15f / 255f, 20f / 255f, 29f / 255f, 0.95f); // #0f141d
            Color cyanBorder = new Color(0f / 255f, 240f / 255f, 255f / 255f, 1f); // #00f0ff Electric Cyan
            return CreateRoundedRectSprite(path, 64, 64, darkFill, cyanBorder, 2f, 4f);
        }

        private static Sprite GetOrCreateGlitchCrimsonButtonSprite()
        {
            string path = "Assets/UI/CyberButton_Crimson.png";
            Color darkFill = new Color(15f / 255f, 20f / 255f, 29f / 255f, 0.95f); // #0f141d
            Color crimsonBorder = new Color(255f / 255f, 0f / 255f, 85f / 255f, 1f); // #ff0055 Glitch Crimson
            return CreateRoundedRectSprite(path, 64, 64, darkFill, crimsonBorder, 2f, 4f);
        }

        private static Sprite GetOrCreateCyberPanelSprite()
        {
            string path = "Assets/UI/CyberPanel_Dark.png";
            Color darkSlateFill = new Color(15f / 255f, 20f / 255f, 29f / 255f, 0.90f); // #0f141d 90% opacity
            Color borderSlate = new Color(30f / 255f, 41f / 255f, 59f / 255f, 1f); // #1e293b 1px border
            return CreateRoundedRectSprite(path, 64, 64, darkSlateFill, borderSlate, 1.5f, 6f);
        }

        private static Sprite GetOrCreateCyberCardRowSprite()
        {
            string path = "Assets/UI/CyberCardRow_Dark.png";
            Color darkFill = new Color(15f / 255f, 20f / 255f, 29f / 255f, 0.85f);
            Color borderSlate = new Color(30f / 255f, 41f / 255f, 59f / 255f, 1f);
            return CreateRoundedRectSprite(path, 64, 64, darkFill, borderSlate, 1.2f, 4f);
        }

        private static Sprite GetOrCreateCyberTopBarSprite()
        {
            string path = "Assets/UI/CyberTopBar_Dark.png";
            Color topBarFill = new Color(15f / 255f, 20f / 255f, 29f / 255f, 0.80f); // #0f141d 80% opacity
            Color borderSlate = new Color(30f / 255f, 41f / 255f, 59f / 255f, 1f); // #1e293b bottom border
            return CreateRoundedRectSprite(path, 64, 64, topBarFill, borderSlate, 1f, 0f);
        }

        private static Sprite GetOrCreatePrimaryButtonSprite()
        {
            return GetOrCreateCyberButtonSprite();
        }

        private static Sprite GetOrCreateSecondaryButtonSprite()
        {
            return GetOrCreateCyberButtonSprite();
        }

        private static Sprite GetOrCreatePanelCardSprite()
        {
            return GetOrCreateCyberPanelSprite();
        }

        private static Sprite GetOrCreateCardRowSprite()
        {
            return GetOrCreateCyberCardRowSprite();
        }

        private static Sprite GetOrCreateNeuralEmblemSprite()
        {
            string assetPath = "Assets/UI/NeuralEmblem.png";
            Sprite sp = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            if (sp != null) return sp;

            int size = 64;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            Color[] pixels = new Color[size * size];

            Vector2 center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
            Color bgCard = new Color(10f / 255f, 30f / 255f, 53f / 255f, 0.95f); // #0A1E35
            Color tealBorder = new Color(13f / 255f, 148f / 255f, 136f / 255f, 1f); // #0D9488
            Color cyanNode = new Color(56f / 255f, 189f / 255f, 248f / 255f, 1f); // #38BDF8
            Color lightTeal = new Color(153f / 255f, 246f / 255f, 228f / 255f, 1f); // #99F6E4
            Color goldCore = new Color(254f / 255f, 240f / 255f, 138f / 255f, 1f); // #FEF08A
            Color accentTeal = new Color(94f / 255f, 234f / 255f, 212f / 255f, 1f); // #5EEAD4

            float cornerRadius = 10f;
            float borderWidth = 2f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Max(0f, Mathf.Max(cornerRadius - x, x - (size - 1 - cornerRadius)));
                    float dy = Mathf.Max(0f, Mathf.Max(cornerRadius - y, y - (size - 1 - cornerRadius)));
                    float distCorner = Mathf.Sqrt(dx * dx + dy * dy);

                    if (distCorner > cornerRadius + 0.5f)
                    {
                        pixels[y * size + x] = Color.clear;
                        continue;
                    }

                    Color c = bgCard;
                    if (distCorner > cornerRadius - borderWidth || x < borderWidth || x >= size - borderWidth || y < borderWidth || y >= size - borderWidth)
                    {
                        c = tealBorder;
                    }

                    float relX = Mathf.Abs(x - center.x);
                    float relY = Mathf.Abs(y - center.y);
                    float manhattan = relX + relY;
                    float euclidean = Mathf.Sqrt(relX * relX + relY * relY);

                    if ((relX <= 1.2f && relY <= 18f && relY >= 6f) || (relY <= 1.2f && relX <= 18f && relX >= 6f))
                    {
                        c = accentTeal;
                    }
                    if ((Mathf.Abs(relY - 18f) <= 2.2f && relX <= 2.2f) || (Mathf.Abs(relX - 18f) <= 2.2f && relY <= 2.2f))
                    {
                        c = lightTeal;
                    }
                    if (Mathf.Abs(manhattan - 13f) <= 1.5f)
                    {
                        c = cyanNode;
                    }
                    if (manhattan <= 8f)
                    {
                        c = Color.Lerp(cyanNode, lightTeal, (8f - manhattan) / 8f);
                    }
                    if (euclidean <= 3.2f)
                    {
                        c = goldCore;
                    }

                    pixels[y * size + x] = c;
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();

            SaveTextureAndImportUISprite(tex, assetPath);
            AssetDatabase.Refresh();
            return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        }

        private static Button CreateCyberButton(
            Transform parent,
            string btnName,
            Vector2 anchoredPos,
            Vector2 size,
            string text,
            float fontSize = 20f,
            TMP_FontAsset fontAsset = null,
            bool isCrimson = false)
        {
            if (fontAsset == null) fontAsset = GetOrCreateDefaultTMPFont();

            GameObject btnObj = GetOrRecreateChild(parent, btnName);
            RectTransform btnRect = btnObj.GetComponent<RectTransform>();
            btnRect.anchorMin = new Vector2(0.5f, 0.5f);
            btnRect.anchorMax = new Vector2(0.5f, 0.5f);
            btnRect.pivot = new Vector2(0.5f, 0.5f);
            btnRect.anchoredPosition = anchoredPos;
            btnRect.sizeDelta = size;
            btnRect.localScale = Vector3.one;

            Image btnImg = btnObj.GetComponent<Image>() ?? btnObj.AddComponent<Image>();
            btnImg.sprite = isCrimson ? GetOrCreateGlitchCrimsonButtonSprite() : GetOrCreateCyberButtonSprite();
            btnImg.type = Image.Type.Sliced;
            btnImg.color = Color.white;

            Button btn = btnObj.GetComponent<Button>() ?? btnObj.AddComponent<Button>();
            btn.transition = Selectable.Transition.ColorTint;
            btn.targetGraphic = btnImg;

            ColorBlock cb = btn.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = isCrimson ? new Color(1f, 0.45f, 0.55f, 1f) : ColorGlowHighlight; // #38bdf8
            cb.selectedColor = isCrimson ? new Color(1f, 0.45f, 0.55f, 1f) : ColorGlowHighlight;
            cb.pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);
            cb.disabledColor = new Color(0.25f, 0.25f, 0.25f, 0.5f);
            cb.colorMultiplier = 1f;
            btn.colors = cb;

            // Attach smooth hover scale effect (Vector3 scale 1.05x on pointer enter/select)
            UIHoverScaleEffect hoverEffect = btnObj.GetComponent<UIHoverScaleEffect>() ?? btnObj.AddComponent<UIHoverScaleEffect>();

            LayoutElement le = btnObj.GetComponent<LayoutElement>() ?? btnObj.AddComponent<LayoutElement>();
            le.minWidth = size.x;
            le.minHeight = size.y;
            le.preferredWidth = size.x;
            le.preferredHeight = size.y;

            ContentSizeFitter btnCsf = btnObj.GetComponent<ContentSizeFitter>();
            if (btnCsf != null) Object.DestroyImmediate(btnCsf);

            GameObject labelObj = GetOrRecreateChild(btnObj.transform, "Text");
            RectTransform labelRect = labelObj.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            labelRect.sizeDelta = size;
            labelRect.localScale = Vector3.one;

            ContentSizeFitter labelCsf = labelObj.GetComponent<ContentSizeFitter>();
            if (labelCsf != null) Object.DestroyImmediate(labelCsf);

            TextMeshProUGUI labelTmp = labelObj.GetComponent<TextMeshProUGUI>() ?? labelObj.AddComponent<TextMeshProUGUI>();
            labelTmp.text = text;
            labelTmp.textWrappingMode = TextWrappingModes.NoWrap;
            labelTmp.overflowMode = TextOverflowModes.Overflow;
            ConfigureTMPText(labelTmp, fontAsset, fontSize: fontSize, alignment: TextAlignmentOptions.Center, color: ColorCrispWhite);

            return btn;
        }

        private static Button CreateGlitchCrimsonButton(
            Transform parent,
            string btnName,
            Vector2 anchoredPos,
            Vector2 size,
            string text,
            float fontSize = 20f,
            TMP_FontAsset fontAsset = null)
        {
            return CreateCyberButton(parent, btnName, anchoredPos, size, text, fontSize, fontAsset, true);
        }

        private static Button CreatePrimaryButton(
            Transform parent,
            string btnName,
            Vector2 anchoredPos,
            Vector2 size,
            string text,
            float fontSize = 22f,
            TMP_FontAsset fontAsset = null)
        {
            return CreateCyberButton(parent, btnName, anchoredPos, size, text, fontSize, fontAsset, false);
        }

        private static Button CreateSecondaryButton(
            Transform parent,
            string btnName,
            Vector2 anchoredPos,
            Vector2 size,
            string text,
            float fontSize = 15f,
            TMP_FontAsset fontAsset = null)
        {
            return CreateCyberButton(parent, btnName, anchoredPos, size, text, fontSize, fontAsset, false);
        }

        private static GameObject SetupPlayer(string folderPath, int enemyLayerIndex)
        {
            GameObject playerObj = GameObject.FindWithTag("Player");
            if (playerObj == null)
            {
                playerObj = GameObject.Find("Player");
            }
            if (playerObj == null)
            {
                playerObj = new GameObject("Player");
                Undo.RegisterCreatedObjectUndo(playerObj, "Create Player");
            }

            playerObj.transform.position = new Vector3(0f, -1000f, 0f);
            playerObj.transform.rotation = Quaternion.identity;
            playerObj.tag = "Player";

            // SpriteRenderer on root
            SpriteRenderer sr = playerObj.GetComponent<SpriteRenderer>();
            if (sr == null)
            {
                sr = playerObj.AddComponent<SpriteRenderer>();
            }

            GetOrCreatePlayerDirectionalSprites(out Sprite frontSprite, out Sprite backSprite, out Sprite sideSprite);
            Sprite weaponSprite = GetOrCreateWeaponBlasterSprite();

            sr.sprite = frontSprite;
            sr.color = Color.white;
            sr.enabled = true;

            // Rigidbody2D: Continuous collision detection
            Rigidbody2D rb = playerObj.GetComponent<Rigidbody2D>();
            if (rb == null)
            {
                rb = playerObj.AddComponent<Rigidbody2D>();
            }
            rb.gravityScale = 0f;
            rb.freezeRotation = true;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Discrete;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;

            CircleCollider2D col = playerObj.GetComponent<CircleCollider2D>();
            if (col == null)
            {
                col = playerObj.AddComponent<CircleCollider2D>();
            }

            PlayerController playerCtrl = playerObj.GetComponent<PlayerController>();
            if (playerCtrl == null)
            {
                playerCtrl = playerObj.AddComponent<PlayerController>();
            }
            playerCtrl.maxHealth = 5;
            playerCtrl.currentHealth = 5;
            playerCtrl.HalfWidth = 18f;
            playerCtrl.HalfHeight = 10f;
            playerCtrl.spriteFront = frontSprite;
            playerCtrl.spriteBack = backSprite;
            playerCtrl.spriteSide = sideSprite;
            playerCtrl.weaponSpriteAsset = weaponSprite;

            // Ensure VisualRoot, Body, WeaponPivot, WeaponSprite, and MuzzlePoint hierarchy
            playerCtrl.EnsureVisualHierarchy();

            if (playerCtrl.weaponSprite != null && playerCtrl.weaponSpriteRenderer == null)
            {
                playerCtrl.weaponSpriteRenderer = playerCtrl.weaponSprite.GetComponent<SpriteRenderer>() ?? playerCtrl.weaponSprite.gameObject.AddComponent<SpriteRenderer>();
            }

            if (playerCtrl.bodySpriteRenderer != null)
            {
                playerCtrl.bodySpriteRenderer.sprite = frontSprite;
                playerCtrl.bodySpriteRenderer.color = Color.white;
            }
            if (playerCtrl.weaponSpriteRenderer != null)
            {
                playerCtrl.weaponSpriteRenderer.sprite = weaponSprite;
                playerCtrl.weaponSpriteRenderer.sortingOrder = 5;
            }

            SerializedObject pSo = new SerializedObject(playerCtrl);
            SetSerializedProp(pSo, "weaponSpriteRenderer", playerCtrl.weaponSpriteRenderer);
            SetSerializedProp(pSo, "weaponSprite", playerCtrl.weaponSprite);
            SetSerializedProp(pSo, "weaponPivot", playerCtrl.weaponPivot);
            SetSerializedProp(pSo, "muzzlePoint", playerCtrl.muzzlePoint);
            SetSerializedProp(pSo, "weaponSpriteAsset", weaponSprite);
            pSo.ApplyModifiedProperties();
            EditorUtility.SetDirty(playerCtrl);

            // Strip legacy obsolete PulseAbility if present
            Component pulseComp = playerObj.GetComponent("PulseAbility");
            if (pulseComp != null) Object.DestroyImmediate(pulseComp);

            // 1. RaycastShooter setup with dedicated beam LineRenderer (Amber Yellow #FFE600, sortingOrder = 10)
            RaycastShooter shooter = playerObj.GetComponent<RaycastShooter>() ?? playerObj.AddComponent<RaycastShooter>();
            shooter.fireRate = 0.2f;
            shooter.weaponRange = 15f;
            shooter.damage = 1;

            int enemiesMask = LayerMask.GetMask("Enemies");
            if (enemiesMask == 0) enemiesMask = LayerMask.GetMask("Enemy");
            if (enemiesMask == 0 && enemyLayerIndex != -1) enemiesMask = (1 << enemyLayerIndex);
            // Strictly exclude Default layer or any non-enemy layers under all conditions
            int defaultMask = LayerMask.GetMask("Default");
            if (defaultMask != 0) enemiesMask &= ~defaultMask;
            enemiesMask &= ~1;

            shooter.enemyLayer = enemiesMask;
            shooter.enemyLayerMask = enemiesMask;

            SerializedObject shooterSo = new SerializedObject(shooter);
            SerializedProperty elProp = shooterSo.FindProperty("enemyLayer");
            if (elProp != null) elProp.intValue = enemiesMask;
            SerializedProperty elmProp = shooterSo.FindProperty("enemyLayerMask");
            if (elmProp != null) elmProp.intValue = enemiesMask;
            shooterSo.ApplyModifiedProperties();

            // LineRenderer configured for 2D (width 0.08f, Material "Sprites/Default", Amber Yellow #FFE600, sortingOrder = 10)
            LineRenderer lineRenderer = playerObj.GetComponent<LineRenderer>();
            if (lineRenderer == null)
            {
                lineRenderer = playerObj.AddComponent<LineRenderer>();
            }
            lineRenderer.useWorldSpace = true;
            lineRenderer.startWidth = 0.08f;
            lineRenderer.endWidth = 0.08f;
            lineRenderer.positionCount = 2;
            lineRenderer.enabled = false;
            lineRenderer.sortingOrder = 10;

            ColorUtility.TryParseHtmlString("#FFE600", out Color amberYellow);
            Shader spriteShader = Shader.Find("Sprites/Default");
            if (spriteShader != null)
            {
                Material mat = new Material(spriteShader);
                mat.color = amberYellow;
                lineRenderer.material = mat;
            }

            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new GradientColorKey[] { new GradientColorKey(amberYellow, 0.0f), new GradientColorKey(amberYellow, 1.0f) },
                new GradientAlphaKey[] { new GradientAlphaKey(1.0f, 0.0f), new GradientAlphaKey(1.0f, 1.0f) }
            );
            lineRenderer.colorGradient = gradient;
            lineRenderer.startColor = amberYellow;
            lineRenderer.endColor = amberYellow;

            shooter.beamRenderer = lineRenderer;

            // Destroy legacy AimLaser child to eliminate cyan pointer line
            Transform existingAimLaser = playerObj.transform.Find("AimLaser");
            if (existingAimLaser != null)
            {
                Object.DestroyImmediate(existingAimLaser.gameObject);
            }
            shooter.aimLaserRenderer = null;

            // 2. EMPAbility setup (EMP Overclock, Right-Click, 10s cooldown)
            EMPAbility empAbility = playerObj.GetComponent<EMPAbility>() ?? playerObj.AddComponent<EMPAbility>();
            empAbility.cooldown = 10f;

            // Attach NeuralTether & dedicated Tether LineRenderer (0.05f width, cyan tint)
            GameObject tetherChild = GetOrRecreateChild(playerObj.transform, "TetherLine");
            LineRenderer tetherLine = tetherChild.GetComponent<LineRenderer>();
            if (tetherLine == null)
            {
                tetherLine = tetherChild.AddComponent<LineRenderer>();
            }
            tetherLine.useWorldSpace = true;
            tetherLine.startWidth = 0.05f;
            tetherLine.endWidth = 0.05f;
            tetherLine.positionCount = 2;
            tetherLine.enabled = false;
            if (spriteShader != null)
            {
                tetherLine.material = new Material(spriteShader);
            }
            Color tetherCyan = new Color(0f, 1f, 1f, 1f);
            tetherLine.startColor = tetherCyan;
            tetherLine.endColor = tetherCyan;

            NeuralTether neuralTether = playerObj.GetComponent<NeuralTether>();
            if (neuralTether == null)
            {
                neuralTether = playerObj.AddComponent<NeuralTether>();
            }
            neuralTether.tetherLine = tetherLine;
            neuralTether.hijackRange = 12f;
            neuralTether.hijackDuration = 6f;
            neuralTether.enemyLayer = 1 << enemyLayerIndex;

            MiniMapMarker playerMarker = playerObj.GetComponent<MiniMapMarker>() ?? playerObj.AddComponent<MiniMapMarker>();
            playerMarker.markerType = MiniMapMarker.MarkerType.Player;
            playerMarker.ApplyDefaultConfiguration();

            Debug.Log("[GameJam] Player configured with Discrete Rigidbody2D, CircleCollider2D, PlayerController, and NeuralTether.");
            return playerObj;
        }

        private static void SetupEnemyPrefabs(
            string folderPath,
            int enemyLayerIndex,
            out GameObject chaserPrefab,
            out GameObject sniperPrefab,
            out GameObject projectilePrefab)
        {
            if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
            {
                AssetDatabase.CreateFolder("Assets", "Prefabs");
            }
            if (!AssetDatabase.IsValidFolder("Assets/Prefabs/Enemies"))
            {
                AssetDatabase.CreateFolder("Assets/Prefabs", "Enemies");
            }

            // Sanitization: Delete redundant nested directories and loose duplicate prefabs
            if (AssetDatabase.IsValidFolder("Assets/Prefabs/Enemies/Enemies"))
            {
                AssetDatabase.DeleteAsset("Assets/Prefabs/Enemies/Enemies");
            }

            string[] loosePrefabs = new string[]
            {
                "Assets/Prefabs/EnemyChaser.prefab",
                "Assets/Prefabs/EnemyProjectile.prefab",
                "Assets/Prefabs/SniperNode.prefab"
            };
            foreach (string loosePath in loosePrefabs)
            {
                if (File.Exists(loosePath) || AssetDatabase.LoadAssetAtPath<GameObject>(loosePath) != null)
                {
                    AssetDatabase.DeleteAsset(loosePath);
                }
            }

            ColorUtility.TryParseHtmlString("#FB7185", out Color coralRed);

            int effectiveEnemyLayer = LayerMask.NameToLayer("Enemies");
            if (effectiveEnemyLayer == -1) effectiveEnemyLayer = enemyLayerIndex;
            if (effectiveEnemyLayer == -1) effectiveEnemyLayer = 11;

            // 1. Create EnemyProjectile Prefab: CircleCollider2D (IsTrigger = false, Health = 1)
            string projDir = "Assets/Prefabs/Enemies";
            string projPrefabPath = $"{projDir}/EnemyProjectile.prefab";
            GameObject projObj = new GameObject("EnemyProjectile");
            projObj.layer = effectiveEnemyLayer;

            SpriteRenderer projSr = projObj.AddComponent<SpriteRenderer>();
            Sprite projSprite = LoadSprite($"{folderPath}/Tiles/tile_0114.png")
                ?? LoadSprite($"{folderPath}/Tiles/tile_0126.png")
                ?? LoadSprite($"{folderPath}/Tiles/tile_0088.png")
                ?? FindFirstSprite(folderPath);
            projSr.sprite = projSprite;
            projSr.color = coralRed;
            projObj.transform.localScale = new Vector3(0.6f, 0.6f, 1f);

            Rigidbody2D projRb = projObj.AddComponent<Rigidbody2D>();
            projRb.gravityScale = 0f;
            projRb.freezeRotation = true;
            projRb.collisionDetectionMode = CollisionDetectionMode2D.Discrete;

            CircleCollider2D projCol = projObj.AddComponent<CircleCollider2D>();
            projCol.isTrigger = false;
            projCol.radius = 0.25f;

            EnemyProjectile projComponent = projObj.AddComponent<EnemyProjectile>();
            projComponent.Health = 1;

            MiniMapMarker projMarker = projObj.AddComponent<MiniMapMarker>();
            projMarker.markerType = MiniMapMarker.MarkerType.Grenade;
            projMarker.ApplyDefaultConfiguration();

            projectilePrefab = PrefabUtility.SaveAsPrefabAsset(projObj, projPrefabPath);
            Object.DestroyImmediate(projObj);

            // 2. Create SniperEnemy Prefab: CircleCollider2D (IsTrigger = false), Rigidbody2D, SniperNode, MiniMapMarker
            string sniperPrefabPath = $"{projDir}/SniperEnemy.prefab";
            GameObject sniperObj = new GameObject("SniperEnemy");
            sniperObj.layer = effectiveEnemyLayer;
            sniperObj.tag = "Enemy";

            SpriteRenderer sniperSr = sniperObj.AddComponent<SpriteRenderer>();
            Sprite sniperSprite = LoadSprite($"{folderPath}/Tiles/tile_0111.png")
                ?? LoadSprite($"{folderPath}/Tiles/tile_0109.png")
                ?? LoadSprite($"{folderPath}/Tiles/tile_0110.png")
                ?? FindFirstSprite(folderPath);
            sniperSr.sprite = sniperSprite;
            sniperSr.color = coralRed;

            Rigidbody2D sniperRb = sniperObj.AddComponent<Rigidbody2D>();
            sniperRb.gravityScale = 0f;
            sniperRb.freezeRotation = true;
            sniperRb.collisionDetectionMode = CollisionDetectionMode2D.Discrete;

            CircleCollider2D sniperCol = sniperObj.AddComponent<CircleCollider2D>();
            sniperCol.isTrigger = false;
            sniperCol.radius = 0.45f;

            SniperNode sniperScript = sniperObj.AddComponent<SniperNode>();
            sniperScript.projectilePrefab = projectilePrefab;

            MiniMapMarker sniperMarker = sniperObj.AddComponent<MiniMapMarker>();
            sniperMarker.markerType = MiniMapMarker.MarkerType.Sniper;
            sniperMarker.ApplyDefaultConfiguration();

            SerializedObject sniperSo = new SerializedObject(sniperScript);
            SetSerializedProp(sniperSo, "projectilePrefab", projectilePrefab);
            sniperSo.ApplyModifiedProperties();

            sniperPrefab = PrefabUtility.SaveAsPrefabAsset(sniperObj, sniperPrefabPath);
            Object.DestroyImmediate(sniperObj);

            // 3. Create ChaserEnemy Prefab: CircleCollider2D, Rigidbody2D (Discrete, low drag), ChaserNode, MiniMapMarker
            string chaserPrefabPath = $"{projDir}/ChaserEnemy.prefab";
            GameObject chaserObj = new GameObject("ChaserEnemy");
            chaserObj.layer = effectiveEnemyLayer;
            chaserObj.tag = "Enemy";

            SpriteRenderer chaserSr = chaserObj.AddComponent<SpriteRenderer>();
            Sprite chaserSprite = LoadSprite($"{folderPath}/Tiles/tile_0108.png")
                ?? LoadSprite($"{folderPath}/Tiles/tile_0109.png")
                ?? LoadSprite($"{folderPath}/Tiles/tile_0110.png")
                ?? FindFirstSprite(folderPath);
            chaserSr.sprite = chaserSprite;
            chaserSr.color = coralRed;

            Rigidbody2D chaserRb = chaserObj.AddComponent<Rigidbody2D>();
            chaserRb.gravityScale = 0f;
            chaserRb.freezeRotation = true;
            chaserRb.collisionDetectionMode = CollisionDetectionMode2D.Discrete;
            chaserRb.linearDamping = 0.1f;

            CircleCollider2D chaserCol = chaserObj.AddComponent<CircleCollider2D>();
            chaserCol.isTrigger = true;
            chaserCol.radius = 0.45f;

            ChaserNode chaserNode = chaserObj.AddComponent<ChaserNode>();
            EnemyChaser enemyChaser = chaserObj.AddComponent<EnemyChaser>();

            MiniMapMarker chaserMarker = chaserObj.AddComponent<MiniMapMarker>();
            chaserMarker.markerType = MiniMapMarker.MarkerType.Chaser;
            chaserMarker.ApplyDefaultConfiguration();

            SerializedObject chaserSo = new SerializedObject(enemyChaser);
            SerializedProperty dmgProp = chaserSo.FindProperty("damageToPlayer");
            if (dmgProp != null)
            {
                dmgProp.intValue = 1;
                chaserSo.ApplyModifiedProperties();
            }

            chaserPrefab = PrefabUtility.SaveAsPrefabAsset(chaserObj, chaserPrefabPath);
            Object.DestroyImmediate(chaserObj);

            Debug.Log($"[GameJam] Standard enemy prefabs saved strictly to {projDir}/ (ChaserEnemy, SniperEnemy, EnemyProjectile).");
        }

        private static void SetupWaveSpawner(GameObject playerObj, GameObject chaserPrefab, GameObject sniperPrefab)
        {
            GameObject spawnerObj = GameObject.Find("WaveSpawner");
            if (spawnerObj == null)
            {
                spawnerObj = new GameObject("WaveSpawner");
                Undo.RegisterCreatedObjectUndo(spawnerObj, "Create WaveSpawner");
            }

            WaveSpawner waveSpawner = spawnerObj.GetComponent<WaveSpawner>();
            if (waveSpawner == null)
            {
                waveSpawner = spawnerObj.AddComponent<WaveSpawner>();
            }

            waveSpawner.chaserPrefab = chaserPrefab;
            waveSpawner.sniperPrefab = sniperPrefab;
            waveSpawner.ChaserPrefab = chaserPrefab;
            waveSpawner.SniperPrefab = sniperPrefab;
            waveSpawner.EnemyPrefab = chaserPrefab;
            waveSpawner.HalfWidth = 18f;
            waveSpawner.HalfHeight = 10f;
            if (playerObj != null) waveSpawner.Player = playerObj.transform;

            SerializedObject spawnerSo = new SerializedObject(waveSpawner);
            SetSerializedProp(spawnerSo, "chaserPrefab", chaserPrefab);
            SetSerializedProp(spawnerSo, "sniperPrefab", sniperPrefab);
            SetSerializedProp(spawnerSo, "enemyPrefab", chaserPrefab);
            if (playerObj != null)
            {
                SetSerializedProp(spawnerSo, "player", playerObj.transform);
            }
            spawnerSo.ApplyModifiedProperties();
            EditorUtility.SetDirty(waveSpawner);

            Debug.Log("[GameJam] WaveSpawner configured with chaserPrefab, sniperPrefab, Player, and 36x20 arena boundary.");
        }

        private static void SetupArenaBoundaries(float width = 36f, float height = 20f)
        {
            float halfWidth = width * 0.5f;   // 18f
            float halfHeight = height * 0.5f; // 10f

            // Create or replace root GameObject "Arena_Boundaries"
            GameObject arenaObj = GameObject.Find("Arena_Boundaries");
            if (arenaObj == null)
            {
                arenaObj = new GameObject("Arena_Boundaries");
                Undo.RegisterCreatedObjectUndo(arenaObj, "Create Arena Boundaries");
            }

            arenaObj.transform.position = Vector3.zero;
            arenaObj.transform.rotation = Quaternion.identity;
            arenaObj.transform.localScale = Vector3.one;

            while (arenaObj.transform.childCount > 0)
            {
                Object.DestroyImmediate(arenaObj.transform.GetChild(0).gameObject);
            }

            // 1. Build Visible Yellow Boundary Border
            GameObject perimeterObj = new GameObject("Perimeter_Line");
            perimeterObj.transform.SetParent(arenaObj.transform);
            perimeterObj.transform.localPosition = Vector3.zero;

            LineRenderer lineRenderer = perimeterObj.AddComponent<LineRenderer>();
            lineRenderer.loop = true;
            lineRenderer.positionCount = 4;
            lineRenderer.SetPositions(new Vector3[]
            {
                new Vector3(-halfWidth, -halfHeight, 0f),
                new Vector3(halfWidth, -halfHeight, 0f),
                new Vector3(halfWidth, halfHeight, 0f),
                new Vector3(-halfWidth, halfHeight, 0f)
            });
            lineRenderer.startWidth = 0.12f;
            lineRenderer.endWidth = 0.12f;

            // Assign standard default UI/Sprites-Default material
            Material spriteDefaultMat = AssetDatabase.GetBuiltinExtraResource<Material>("Sprites-Default.mat");
            if (spriteDefaultMat == null)
            {
                Shader spriteShader = Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default");
                if (spriteShader != null)
                {
                    spriteDefaultMat = new Material(spriteShader);
                }
            }
            lineRenderer.material = spriteDefaultMat;

            Color brightYellow = new Color(1f, 0.9f, 0f, 1f); // Bright Yellow #FFE600
            lineRenderer.startColor = brightYellow;
            lineRenderer.endColor = brightYellow;

            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new GradientColorKey[] { new GradientColorKey(brightYellow, 0.0f), new GradientColorKey(brightYellow, 1.0f) },
                new GradientAlphaKey[] { new GradientAlphaKey(1.0f, 0.0f), new GradientAlphaKey(1.0f, 1.0f) }
            );
            lineRenderer.colorGradient = gradient;

            lineRenderer.sortingOrder = 5;
            lineRenderer.useWorldSpace = true;

            // 2. Add Physical Collision Walls (Prevent Player & Enemy Escape)
            int obstacleLayer = LayerMask.NameToLayer("Obstacles");
            int boundaryLayer = LayerMask.NameToLayer("Boundary");
            int wallLayer = obstacleLayer != -1 ? obstacleLayer : (boundaryLayer != -1 ? boundaryLayer : LayerMask.NameToLayer("Default"));
            if (wallLayer == -1) wallLayer = 0;

            if (boundaryLayer != -1)
            {
                perimeterObj.layer = boundaryLayer;
                arenaObj.layer = boundaryLayer;
            }

            CreateWall(arenaObj.transform, "Wall_Top", new Vector2(0f, halfHeight + 0.5f), new Vector2(width + 2f, 1f), wallLayer);
            CreateWall(arenaObj.transform, "Wall_Bottom", new Vector2(0f, -halfHeight - 0.5f), new Vector2(width + 2f, 1f), wallLayer);
            CreateWall(arenaObj.transform, "Wall_Left", new Vector2(-halfWidth - 0.5f, 0f), new Vector2(1f, height + 2f), wallLayer);
            CreateWall(arenaObj.transform, "Wall_Right", new Vector2(halfWidth + 0.5f, 0f), new Vector2(1f, height + 2f), wallLayer);

            Debug.Log($"[GameJam] Arena boundaries created with Perimeter_Line (bright yellow #FFE600, {width}x{height}) and 4 BoxCollider2D walls (isTrigger = false).");
        }

        private static void CreateWall(Transform parent, string wallName, Vector2 position, Vector2 size, int layer = 0)
        {
            GameObject wallObj = new GameObject(wallName);
            wallObj.layer = layer;
            wallObj.transform.SetParent(parent);
            wallObj.transform.localPosition = position;

            BoxCollider2D collider = wallObj.AddComponent<BoxCollider2D>();
            collider.size = size;
            collider.isTrigger = false;
        }

        private static void PruneSceneHierarchy()
        {
            var allowedNames = new System.Collections.Generic.HashSet<string>
            {
                "Main Camera",
                "_GameManager",
                "Player",
                "WaveSpawner",
                "Arena_Boundaries",
                "UI_Canvas",
                "EventSystem"
            };

            Scene activeScene = SceneManager.GetActiveScene();
            GameObject[] rootObjects = activeScene.GetRootGameObjects();
            foreach (GameObject rootObj in rootObjects)
            {
                if (rootObj == null) continue;

                if (rootObj.name == "GameManager")
                {
                    if (GameObject.Find("_GameManager") == null)
                    {
                        rootObj.name = "_GameManager";
                    }
                }

                if (!allowedNames.Contains(rootObj.name))
                {
                    Debug.Log($"[GameJam Prune] Destroying extraneous scene root GameObject: {rootObj.name}");
                    Object.DestroyImmediate(rootObj);
                }
            }

            // Destroy empty duplicate UI_Canvas or UI Canvas objects that lack a Canvas component
            GameObject[] uiCanvases = System.Array.FindAll(activeScene.GetRootGameObjects(), go => go != null && (go.name == "UI_Canvas" || go.name == "UI Canvas"));
            if (uiCanvases.Length > 1)
            {
                foreach (GameObject cObj in uiCanvases)
                {
                    if (cObj.GetComponent<Canvas>() == null)
                    {
                        Debug.Log($"[GameJam Prune] Destroying empty duplicate UI Canvas GameObject: {cObj.name}");
                        Object.DestroyImmediate(cObj);
                    }
                }
            }

            // Ensure only one EventSystem exists
            EventSystem[] eventSystems = Object.FindObjectsByType<EventSystem>(FindObjectsInactive.Include);
            for (int i = 1; i < eventSystems.Length; i++)
            {
                Object.DestroyImmediate(eventSystems[i].gameObject);
            }

            // Ensure only one Canvas named UI_Canvas exists
            Canvas[] canvases = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include);
            for (int i = 0; i < canvases.Length; i++)
            {
                if (canvases[i].gameObject.name != "UI_Canvas")
                {
                    Object.DestroyImmediate(canvases[i].gameObject);
                }
            }

            // Strip any missing MonoBehaviours across all remaining GameObjects
            foreach (GameObject rootObj in activeScene.GetRootGameObjects())
            {
                if (rootObj == null) continue;
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(rootObj);
                foreach (Transform child in rootObj.GetComponentsInChildren<Transform>(true))
                {
                    if (child != null && child.gameObject != null)
                    {
                        GameObjectUtility.RemoveMonoBehavioursWithMissingScript(child.gameObject);
                    }
                }
            }
        }

        // ==================== CYBER ACTION SURVIVAL UI PALETTE & CONFIG ====================
        private static readonly Color ColorPanelBg = new Color(15f / 255f, 20f / 255f, 29f / 255f, 0.90f); // #0f141d with 90% opacity
        private static readonly Color ColorPanelBorder = new Color(30f / 255f, 41f / 255f, 59f / 255f, 1f); // #1e293b 1px border
        private static readonly Color ColorElectricCyan = new Color(0f / 255f, 240f / 255f, 255f / 255f, 1f); // #00f0ff Electric Cyan
        private static readonly Color ColorGlowHighlight = new Color(56f / 255f, 189f / 255f, 248f / 255f, 1f); // #38bdf8 Glow / Hover Highlight
        private static readonly Color ColorGlitchCrimson = new Color(255f / 255f, 0f / 255f, 85f / 255f, 1f); // #ff0055 Glitch Crimson
        private static readonly Color ColorCrispWhite = new Color(1f, 1f, 1f, 1f); // #ffffff Crisp White
        private static readonly Color ColorMutedSlate = new Color(148f / 255f, 163f / 255f, 184f / 255f, 1f); // #94a3b8 Muted Slate
        private static readonly Color ColorTimerYellow = new Color(255f / 255f, 230f / 255f, 0f / 255f, 1f); // #ffe600 Timer Yellow
        private static readonly Color ColorDarkBackdrop = new Color(0f, 0f, 0f, 0.80f); // #000000 with 0.8 alpha
        private static readonly Color ColorTopBarBg = new Color(15f / 255f, 20f / 255f, 29f / 255f, 0.80f); // #0f141d with 80% opacity

        // Compatibility aliases
        private static readonly Color ColorDeepDarkNavy = ColorPanelBg;
        private static readonly Color ColorDeepDarkNavyDarker = ColorPanelBg;
        private static readonly Color ColorSecondaryPanelBg = ColorPanelBg;
        private static readonly Color ColorCardRowBg = ColorPanelBg;
        private static readonly Color ColorModalBackdrop = ColorDarkBackdrop;
        private static readonly Color ColorPrimaryTeal = ColorElectricCyan;
        private static readonly Color ColorPrimaryTealBright = ColorGlowHighlight;
        private static readonly Color ColorHighlightYellow = ColorTimerYellow;
        private static readonly Color ColorSecondaryBtnBg = ColorPanelBg;
        private static readonly Color ColorSecondaryBorderAccent = ColorPanelBorder;
        private static readonly Color ColorTitleWhite = ColorCrispWhite;
        private static readonly Color ColorSubtitleCyan = ColorElectricCyan;
        private static readonly Color ColorTaglineTeal = ColorMutedSlate;
        private static readonly Color ColorBadgeTeal = ColorElectricCyan;
        private static readonly Color ColorToastBg = ColorPanelBg;
        private static readonly Color ColorHealthBarFillTeal = ColorElectricCyan;
        private static readonly Color ColorHealthBarBgDark = new Color(10f / 255f, 15f / 255f, 22f / 255f, 1f);
        private static readonly Color ColorRadarBgDark = ColorPanelBg;
        private static readonly Color ColorRadarOutlineCyan = ColorElectricCyan;
        private static readonly Color ColorCriticalRed = ColorGlitchCrimson;
        private static readonly Color ColorMainPanelBg = ColorPanelBg;
        private static readonly Color ColorVictoryPanelBg = ColorPanelBg;
        private static readonly Color ColorPausePanelBg = ColorPanelBg;
        private static readonly Color ColorPauseTitleGold = ColorTimerYellow;
        private static readonly Color ColorButtonCharcoal = ColorPanelBg;
        private static readonly Color ColorButtonHoverHighlight = ColorGlowHighlight;
        private static readonly Color ColorButtonActiveSelected = ColorElectricCyan;
        private static readonly Color ColorStartBtnEmerald = ColorElectricCyan;
        private static readonly Color ColorStartBtnHover = ColorGlowHighlight;
        private static readonly Color ColorHealthBarBg = ColorHealthBarBgDark;
        private static readonly Color ColorHealthBarFill = ColorElectricCyan;
        private static readonly Color ColorTimerGold = ColorTimerYellow;
        private static readonly Color ColorSlateMuted = ColorMutedSlate;
        private static readonly Color ColorDarkNavyBg = ColorPanelBg;
        private static readonly Color ColorTaglineLightTeal = ColorMutedSlate;

        private static TMP_FontAsset GetOrCreateDefaultTMPFont()
        {
            TMP_FontAsset fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/SpaceGrotesk SDF.asset");
            if (fontAsset == null)
            {
                fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
            }
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
                if (fontAsset.atlasTexture != null)
                {
                    fontAsset.atlasTexture.filterMode = FilterMode.Bilinear;
                }

                Material mat = fontAsset.material;
                if (mat != null)
                {
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
            }

            return fontAsset;
        }

        private static void ConfigureTMPText(
            TextMeshProUGUI tmp,
            TMP_FontAsset fontAsset,
            float fontSize,
            TextAlignmentOptions alignment = TextAlignmentOptions.Center,
            Color? color = null,
            float characterSpacing = 0f)
        {
            if (tmp == null) return;
            if (fontAsset != null) tmp.font = fontAsset;

            ContentSizeFitter csf = tmp.GetComponent<ContentSizeFitter>();
            if (csf != null)
            {
                Object.DestroyImmediate(csf);
            }

            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.enableAutoSizing = false;
            tmp.fontSize = fontSize;
            tmp.alignment = alignment;
            if (color.HasValue) tmp.color = color.Value;
            tmp.fontStyle = FontStyles.Bold;
            tmp.characterSpacing = characterSpacing;
            tmp.raycastTarget = false;
            tmp.rectTransform.localScale = Vector3.one;

            if (tmp.transform.parent != null && tmp.transform.parent is RectTransform parentRect)
            {
                if (parentRect.GetComponent<Button>() != null)
                {
                    Vector2 parentSize = parentRect.rect.size;
                    if (parentSize.x > 0f && parentSize.y > 0f)
                    {
                        Vector2 curDelta = tmp.rectTransform.sizeDelta;
                        tmp.rectTransform.sizeDelta = new Vector2(
                            Mathf.Max(curDelta.x, parentSize.x),
                            Mathf.Max(curDelta.y, parentSize.y)
                        );
                    }
                }
            }
        }

        private static void SetupGameUIAndManager(GameObject playerObj)
        {
            // 1. Total Wipe: Find and DestroyImmediate all existing objects named "Canvas", "UI Canvas", "HUD", "EventSystem", or containing Canvas components
            Canvas[] existingCanvases = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include);
            for (int i = existingCanvases.Length - 1; i >= 0; i--)
            {
                if (existingCanvases[i] != null && existingCanvases[i].gameObject != null)
                {
                    Object.DestroyImmediate(existingCanvases[i].gameObject);
                }
            }

            string[] wipeNames = new string[] { "Canvas", "UI Canvas", "UI_Canvas", "HUD", "EventSystem" };
            Transform[] allSceneTransforms = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include);
            for (int i = allSceneTransforms.Length - 1; i >= 0; i--)
            {
                if (allSceneTransforms[i] != null && allSceneTransforms[i].gameObject != null)
                {
                    string objName = allSceneTransforms[i].gameObject.name;
                    foreach (string wipeTarget in wipeNames)
                    {
                        if (objName.Equals(wipeTarget, System.StringComparison.OrdinalIgnoreCase))
                        {
                            Object.DestroyImmediate(allSceneTransforms[i].gameObject);
                            break;
                        }
                    }
                }
            }

            TMP_FontAsset fontAsset = GetOrCreateDefaultTMPFont();

            // Create fresh root GameObject "UI_Canvas"
            GameObject canvasObj = new GameObject("UI_Canvas");
            Undo.RegisterCreatedObjectUndo(canvasObj, "Create UI_Canvas");
            Canvas canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;
            canvas.pixelPerfect = false;

            CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            scaler.referencePixelsPerUnit = 100f;
            scaler.dynamicPixelsPerUnit = 10.0f;

            canvasObj.AddComponent<GraphicRaycaster>();

            // Create fresh GameObject "EventSystem"
            GameObject eventSystemObj = new GameObject("EventSystem");
            Undo.RegisterCreatedObjectUndo(eventSystemObj, "Create EventSystem");
            eventSystemObj.AddComponent<EventSystem>();
            eventSystemObj.AddComponent<StandaloneInputModule>();

            // Ensure _GameManager exists
            GameObject gmObj = GameObject.Find("_GameManager");
            if (gmObj == null)
            {
                GameObject oldGm = GameObject.Find("GameManager");
                if (oldGm != null)
                {
                    gmObj = oldGm;
                    gmObj.name = "_GameManager";
                }
                else
                {
                    gmObj = new GameObject("_GameManager");
                    Undo.RegisterCreatedObjectUndo(gmObj, "Create _GameManager");
                }
            }
            GameManager gm = gmObj.GetComponent<GameManager>() ?? gmObj.AddComponent<GameManager>();

            // 2. Build HUDPanel (Persistent In-Game HUD, Stretch full screen, localScale = Vector3.one)
            GameObject hudPanelObj = new GameObject("HUDPanel", typeof(RectTransform));
            hudPanelObj.transform.SetParent(canvasObj.transform, false);
            RectTransform hudRect = hudPanelObj.GetComponent<RectTransform>();
            hudRect.anchorMin = Vector2.zero;
            hudRect.anchorMax = Vector2.one;
            hudRect.pivot = new Vector2(0.5f, 0.5f);
            hudRect.offsetMin = Vector2.zero;
            hudRect.offsetMax = Vector2.zero;
            hudRect.localScale = Vector3.one;
            hudPanelObj.SetActive(true);

            // Explicitly purge any legacy MiniMap_Root or Radar UI GameObjects before clean rebuild
            Transform existingMiniMap = hudPanelObj.transform.Find("MiniMap_Root");
            if (existingMiniMap != null) Object.DestroyImmediate(existingMiniMap.gameObject);
            GameObject strayMiniMap = GameObject.Find("MiniMap_Root");
            if (strayMiniMap != null) Object.DestroyImmediate(strayMiniMap);

            // --- 1. TopBar_Container (Anchored across the top of HUDPanel: #0f141d at 80% opacity, 1px bottom border) ---
            GameObject topBarObj = new GameObject("TopBar_Container", typeof(RectTransform));
            topBarObj.transform.SetParent(hudPanelObj.transform, false);
            RectTransform topBarRt = topBarObj.GetComponent<RectTransform>();
            topBarRt.anchorMin = new Vector2(0f, 1f);
            topBarRt.anchorMax = new Vector2(1f, 1f);
            topBarRt.pivot = new Vector2(0.5f, 1f);
            topBarRt.anchoredPosition = Vector2.zero;
            topBarRt.sizeDelta = new Vector2(0f, 64f);
            topBarRt.localScale = Vector3.one;

            Image topBarImg = topBarObj.AddComponent<Image>();
            topBarImg.sprite = GetOrCreateCyberTopBarSprite();
            topBarImg.type = Image.Type.Sliced;
            topBarImg.color = Color.white;

            // --- Left: Health Bar (Inside TopBar_Container): Pos: (24, 0), Size: (260, 34) ---
            GameObject hbRootObj = new GameObject("HealthBar_Root", typeof(RectTransform));
            hbRootObj.transform.SetParent(topBarObj.transform, false);
            RectTransform hbRootRect = hbRootObj.GetComponent<RectTransform>();
            hbRootRect.anchorMin = new Vector2(0f, 0.5f);
            hbRootRect.anchorMax = new Vector2(0f, 0.5f);
            hbRootRect.pivot = new Vector2(0f, 0.5f);
            hbRootRect.anchoredPosition = new Vector2(24f, 0f);
            hbRootRect.sizeDelta = new Vector2(260f, 34f);
            hbRootRect.localScale = Vector3.one;

            Image hbBg = hbRootObj.AddComponent<Image>();
            hbBg.sprite = GetOrCreateCyberCardRowSprite();
            hbBg.type = Image.Type.Sliced;
            hbBg.color = Color.white;

            GameObject fillObj = new GameObject("FillImage", typeof(RectTransform));
            fillObj.transform.SetParent(hbRootObj.transform, false);
            RectTransform fillRect = fillObj.GetComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = new Vector2(2f, 2f);
            fillRect.offsetMax = new Vector2(-2f, -2f);
            fillRect.localScale = Vector3.one;

            Image fillImg = fillObj.AddComponent<Image>();
            fillImg.sprite = HealthBarUI.GetOrCreateWhiteSprite();
            fillImg.type = Image.Type.Filled;
            fillImg.fillMethod = Image.FillMethod.Horizontal;
            fillImg.fillOrigin = (int)Image.OriginHorizontal.Left;
            fillImg.fillAmount = 1f;
            fillImg.color = ColorElectricCyan; // #00f0ff

            GameObject hpTextObj = new GameObject("HP_Text", typeof(RectTransform));
            hpTextObj.transform.SetParent(hbRootObj.transform, false);
            RectTransform hpTextRect = hpTextObj.GetComponent<RectTransform>();
            hpTextRect.anchorMin = Vector2.zero;
            hpTextRect.anchorMax = Vector2.one;
            hpTextRect.offsetMin = Vector2.zero;
            hpTextRect.offsetMax = Vector2.zero;
            hpTextRect.localScale = Vector3.one;

            TextMeshProUGUI hpTmp = hpTextObj.AddComponent<TextMeshProUGUI>();
            hpTmp.text = "HP: 5 / 5";
            ConfigureTMPText(hpTmp, fontAsset, fontSize: 18f, alignment: TextAlignmentOptions.Center, color: ColorCrispWhite);

            HealthBarUI healthUI = hbRootObj.AddComponent<HealthBarUI>();
            healthUI.backgroundImage = hbBg;
            healthUI.fillImage = fillImg;
            healthUI.hpText = hpTmp;
            healthUI.hpPrefix = "HP: ";
            if (playerObj != null)
            {
                healthUI.player = playerObj.GetComponent<PlayerController>();
            }

            // --- Center: Timer (Inside TopBar_Container): Formatted as "SURVIVE: XXs" in #ffe600 ---
            GameObject timerObj = new GameObject("Timer_Text", typeof(RectTransform));
            timerObj.transform.SetParent(topBarObj.transform, false);
            RectTransform timerRect = timerObj.GetComponent<RectTransform>();
            timerRect.anchorMin = new Vector2(0.5f, 0.5f);
            timerRect.anchorMax = new Vector2(0.5f, 0.5f);
            timerRect.pivot = new Vector2(0.5f, 0.5f);
            timerRect.anchoredPosition = Vector2.zero;
            timerRect.sizeDelta = new Vector2(340f, 48f);
            timerRect.localScale = Vector3.one;

            TextMeshProUGUI timerTmp = timerObj.AddComponent<TextMeshProUGUI>();
            timerTmp.text = "SURVIVE: 20s";
            ConfigureTMPText(timerTmp, fontAsset, fontSize: 34f, alignment: TextAlignmentOptions.Center, color: ColorTimerYellow);

            // --- Right: "NODES PURGED: XXXX" counter in #ffffff (Inside TopBar_Container) ---
            GameObject scoreObj = new GameObject("Score_Text", typeof(RectTransform));
            scoreObj.transform.SetParent(topBarObj.transform, false);
            RectTransform scoreRect = scoreObj.GetComponent<RectTransform>();
            scoreRect.anchorMin = new Vector2(1f, 0.5f);
            scoreRect.anchorMax = new Vector2(1f, 0.5f);
            scoreRect.pivot = new Vector2(1f, 0.5f);
            scoreRect.anchoredPosition = new Vector2(-24f, 0f);
            scoreRect.sizeDelta = new Vector2(340f, 40f);
            scoreRect.localScale = Vector3.one;

            TextMeshProUGUI scoreTmp = scoreObj.AddComponent<TextMeshProUGUI>();
            scoreTmp.text = "NODES PURGED: 0";
            ConfigureTMPText(scoreTmp, fontAsset, fontSize: 26f, alignment: TextAlignmentOptions.Right, color: ColorCrispWhite);

            GameObject multObj = new GameObject("Multiplier_Text", typeof(RectTransform));
            multObj.transform.SetParent(scoreObj.transform, false);
            RectTransform multRect = multObj.GetComponent<RectTransform>();
            multRect.anchorMin = new Vector2(1f, 0f);
            multRect.anchorMax = new Vector2(1f, 0f);
            multRect.pivot = new Vector2(1f, 1f);
            multRect.anchoredPosition = new Vector2(0f, -4f);
            multRect.sizeDelta = new Vector2(300f, 24f);
            multRect.localScale = Vector3.one;

            TextMeshProUGUI multTmp = multObj.AddComponent<TextMeshProUGUI>();
            multTmp.text = "";
            ConfigureTMPText(multTmp, fontAsset, fontSize: 18f, alignment: TextAlignmentOptions.Right, color: ColorGlowHighlight);

            // --- Mini-Map Root: Positioned directly below TopBar: Pos: (24, -78), Size: (170, 170) ---
            GameObject miniMapRootObj = new GameObject("MiniMap_Root", typeof(RectTransform));
            miniMapRootObj.transform.SetParent(hudPanelObj.transform, false);
            RectTransform mmRootRect = miniMapRootObj.GetComponent<RectTransform>();
            mmRootRect.anchorMin = new Vector2(0f, 1f);
            mmRootRect.anchorMax = new Vector2(0f, 1f);
            mmRootRect.pivot = new Vector2(0f, 1f);
            mmRootRect.anchoredPosition = new Vector2(24f, -78f);
            mmRootRect.sizeDelta = new Vector2(170f, 170f);
            mmRootRect.localScale = Vector3.one;

            Image mmBg = miniMapRootObj.AddComponent<Image>();
            mmBg.sprite = MiniMapController.GetOrCreateRadarBackgroundSprite();
            mmBg.type = Image.Type.Simple;
            mmBg.color = Color.white;

            GameObject blipsContainerObj = new GameObject("MiniMap_BlipsContainer", typeof(RectTransform));
            blipsContainerObj.transform.SetParent(miniMapRootObj.transform, false);
            RectTransform blipsRect = blipsContainerObj.GetComponent<RectTransform>();
            blipsRect.anchorMin = Vector2.zero;
            blipsRect.anchorMax = Vector2.one;
            blipsRect.pivot = new Vector2(0.5f, 0.5f);
            blipsRect.offsetMin = Vector2.zero;
            blipsRect.offsetMax = Vector2.zero;
            blipsRect.localScale = Vector3.one;

            MiniMapController mmCtrl = miniMapRootObj.AddComponent<MiniMapController>();
            mmCtrl.blipsContainer = blipsRect;
            if (playerObj != null)
            {
                mmCtrl.playerTransform = playerObj.transform;
            }

            // --- Neural Hijack Indicator: Positioned below Mini-Map: Pos: (24, -260), Size: (350, 30) ---
            GameObject hijackObj = new GameObject("Hijack_Text", typeof(RectTransform));
            hijackObj.transform.SetParent(hudPanelObj.transform, false);
            RectTransform hijackRect = hijackObj.GetComponent<RectTransform>();
            hijackRect.anchorMin = new Vector2(0f, 1f);
            hijackRect.anchorMax = new Vector2(0f, 1f);
            hijackRect.pivot = new Vector2(0f, 1f);
            hijackRect.anchoredPosition = new Vector2(24f, -260f);
            hijackRect.sizeDelta = new Vector2(350f, 30f);
            hijackRect.localScale = Vector3.one;

            TextMeshProUGUI hijackTmp = hijackObj.AddComponent<TextMeshProUGUI>();
            hijackTmp.text = "HIJACK: [READY] (R-CLICK)";
            ConfigureTMPText(hijackTmp, fontAsset, fontSize: 20f, alignment: TextAlignmentOptions.Left, color: ColorElectricCyan);

            if (playerObj != null)
            {
                NeuralTether tether = playerObj.GetComponent<NeuralTether>();
                if (tether != null)
                {
                    tether.statusText = hijackTmp;
                }
            }

            // 3. Build StartMenuPanel (Modal Overlay, Dark Charcoal #0f141d with 90% opacity & 1px #1e293b border)
            Transform oldStartMenu = canvasObj.transform.Find("StartMenuPanel");
            if (oldStartMenu != null) Object.DestroyImmediate(oldStartMenu.gameObject);
            Transform oldStart = canvasObj.transform.Find("StartPanel");
            if (oldStart != null) Object.DestroyImmediate(oldStart.gameObject);

            GameObject startMenuPanelObj = new GameObject("StartMenuPanel", typeof(RectTransform));
            startMenuPanelObj.transform.SetParent(canvasObj.transform, false);
            RectTransform startRect = startMenuPanelObj.GetComponent<RectTransform>();
            startRect.anchorMin = Vector2.zero;
            startRect.anchorMax = Vector2.one;
            startRect.offsetMin = Vector2.zero;
            startRect.offsetMax = Vector2.zero;
            startRect.localScale = Vector3.one;
            GameObject startPanelObj = startMenuPanelObj;

            Image startBg = startMenuPanelObj.AddComponent<Image>();
            startBg.sprite = GetOrCreateCyberPanelSprite();
            startBg.type = Image.Type.Sliced;
            startBg.color = Color.white;

            // --- Top Badge: "v1.0.4 • BUILD 802" in Light Cyan #00f0ff at top-left ---
            GameObject badgeObj = new GameObject("Badge_Version", typeof(RectTransform));
            badgeObj.transform.SetParent(startMenuPanelObj.transform, false);
            RectTransform badgeRt = badgeObj.GetComponent<RectTransform>();
            badgeRt.anchorMin = new Vector2(0f, 1f);
            badgeRt.anchorMax = new Vector2(0f, 1f);
            badgeRt.pivot = new Vector2(0f, 1f);
            badgeRt.anchoredPosition = new Vector2(40f, -35f);
            badgeRt.sizeDelta = new Vector2(260f, 32f);
            badgeRt.localScale = Vector3.one;

            TextMeshProUGUI badgeTmp = badgeObj.AddComponent<TextMeshProUGUI>();
            badgeTmp.text = "v1.0.4 • BUILD 802";
            ConfigureTMPText(badgeTmp, fontAsset, fontSize: 15f, alignment: TextAlignmentOptions.Left, color: ColorElectricCyan);

            // --- Audio Quick Toggle: Top-Right button displaying "AUDIO: ON" / "AUDIO: MUTED" ---
            Button audioToggleBtn = CreateCyberButton(startMenuPanelObj.transform, "Btn_AudioQuickToggle", new Vector2(-40f, -35f), new Vector2(170f, 40f), "AUDIO: ON", 14f, fontAsset);
            RectTransform audioRt = audioToggleBtn.GetComponent<RectTransform>();
            audioRt.anchorMin = new Vector2(1f, 1f);
            audioRt.anchorMax = new Vector2(1f, 1f);
            audioRt.pivot = new Vector2(1f, 1f);
            audioRt.anchoredPosition = new Vector2(-40f, -35f);

            // --- Title Header: Large centered text "NEURAL OVERRIDE" in bold #00f0ff with subtle #38bdf8 glow overlay, subtitle "2D ACTION SURVIVAL" in #94a3b8 ---
            GameObject titleHeaderObj = new GameObject("Title_Header", typeof(RectTransform));
            titleHeaderObj.transform.SetParent(startMenuPanelObj.transform, false);
            RectTransform titleHeaderRt = titleHeaderObj.GetComponent<RectTransform>();
            titleHeaderRt.anchorMin = new Vector2(0.5f, 0.5f);
            titleHeaderRt.anchorMax = new Vector2(0.5f, 0.5f);
            titleHeaderRt.pivot = new Vector2(0.5f, 0.5f);
            titleHeaderRt.anchoredPosition = new Vector2(0f, 160f);
            titleHeaderRt.sizeDelta = new Vector2(800f, 140f);
            titleHeaderRt.localScale = Vector3.one;

            // Title Glow Behind: Subtle #38bdf8 overlay glow
            GameObject titleGlowObj = new GameObject("Title_Glow", typeof(RectTransform));
            titleGlowObj.transform.SetParent(titleHeaderObj.transform, false);
            RectTransform titleGlowRt = titleGlowObj.GetComponent<RectTransform>();
            titleGlowRt.anchorMin = new Vector2(0.5f, 0.5f);
            titleGlowRt.anchorMax = new Vector2(0.5f, 0.5f);
            titleGlowRt.pivot = new Vector2(0.5f, 0.5f);
            titleGlowRt.anchoredPosition = new Vector2(0f, 12f);
            titleGlowRt.sizeDelta = new Vector2(800f, 75f);
            titleGlowRt.localScale = Vector3.one;

            TextMeshProUGUI titleGlowTmp = titleGlowObj.AddComponent<TextMeshProUGUI>();
            titleGlowTmp.text = "NEURAL OVERRIDE";
            ConfigureTMPText(titleGlowTmp, fontAsset, fontSize: 68f, alignment: TextAlignmentOptions.Center, color: new Color(56f / 255f, 189f / 255f, 248f / 255f, 0.35f));

            // Title Main Front: Bold #00f0ff Electric Cyan
            GameObject titleMainObj = new GameObject("Title_Main", typeof(RectTransform));
            titleMainObj.transform.SetParent(titleHeaderObj.transform, false);
            RectTransform titleMainRt = titleMainObj.GetComponent<RectTransform>();
            titleMainRt.anchorMin = new Vector2(0.5f, 0.5f);
            titleMainRt.anchorMax = new Vector2(0.5f, 0.5f);
            titleMainRt.pivot = new Vector2(0.5f, 0.5f);
            titleMainRt.anchoredPosition = new Vector2(0f, 10f);
            titleMainRt.sizeDelta = new Vector2(800f, 75f);
            titleMainRt.localScale = Vector3.one;

            TextMeshProUGUI titleMainTmp = titleMainObj.AddComponent<TextMeshProUGUI>();
            titleMainTmp.text = "NEURAL OVERRIDE";
            ConfigureTMPText(titleMainTmp, fontAsset, fontSize: 68f, alignment: TextAlignmentOptions.Center, color: ColorElectricCyan);

            // Subtitle: "2D ACTION SURVIVAL" in Muted Slate #94a3b8
            GameObject subtitleObj = new GameObject("Subtitle_Text", typeof(RectTransform));
            subtitleObj.transform.SetParent(titleHeaderObj.transform, false);
            RectTransform subRt = subtitleObj.GetComponent<RectTransform>();
            subRt.anchorMin = new Vector2(0.5f, 0.5f);
            subRt.anchorMax = new Vector2(0.5f, 0.5f);
            subRt.pivot = new Vector2(0.5f, 0.5f);
            subRt.anchoredPosition = new Vector2(0f, -48f);
            subRt.sizeDelta = new Vector2(600f, 32f);
            subRt.localScale = Vector3.one;

            TextMeshProUGUI subTmp = subtitleObj.AddComponent<TextMeshProUGUI>();
            subTmp.text = "2D ACTION SURVIVAL";
            ConfigureTMPText(subTmp, fontAsset, fontSize: 20f, alignment: TextAlignmentOptions.Center, color: ColorMutedSlate, characterSpacing: 18f);

            // --- Protocol Level Selector Row (Easy / Normal / Expert) ---
            GameObject diffGroupObj = new GameObject("DifficultyButtonGroup", typeof(RectTransform));
            diffGroupObj.transform.SetParent(startMenuPanelObj.transform, false);
            RectTransform diffGroupRect = diffGroupObj.GetComponent<RectTransform>();
            diffGroupRect.anchorMin = new Vector2(0.5f, 0.5f);
            diffGroupRect.anchorMax = new Vector2(0.5f, 0.5f);
            diffGroupRect.pivot = new Vector2(0.5f, 0.5f);
            diffGroupRect.anchoredPosition = new Vector2(0f, 42f);
            diffGroupRect.sizeDelta = new Vector2(580f, 38f);
            diffGroupRect.localScale = Vector3.one;

            HorizontalLayoutGroup hlg = diffGroupObj.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 14f;
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.childControlWidth = false;
            hlg.childControlHeight = false;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;

            Button easyBtn = CreateCyberButton(diffGroupObj.transform, "Btn_Easy", Vector2.zero, new Vector2(180f, 36f), "EASY (20s)", 14f, fontAsset);
            Button normalBtn = CreateCyberButton(diffGroupObj.transform, "Btn_Normal", Vector2.zero, new Vector2(180f, 36f), "NORMAL (50s)", 14f, fontAsset);
            Button expertBtn = CreateCyberButton(diffGroupObj.transform, "Btn_Expert", Vector2.zero, new Vector2(180f, 36f), "EXPERT (90s)", 14f, fontAsset);

            if (gm != null)
            {
                UnityEventTools.AddIntPersistentListener(easyBtn.onClick, gm.SetDifficulty, 0);
                UnityEventTools.AddIntPersistentListener(normalBtn.onClick, gm.SetDifficulty, 1);
                UnityEventTools.AddIntPersistentListener(expertBtn.onClick, gm.SetDifficulty, 2);

                gm.easyButton = easyBtn;
                gm.normalButton = normalBtn;
                gm.expertButton = expertBtn;
                gm.easyBtnImage = easyBtn.GetComponent<Image>();
                gm.normalBtnImage = normalBtn.GetComponent<Image>();
                gm.expertBtnImage = expertBtn.GetComponent<Image>();
            }

            // --- Menu Panel Layout: Centered vertical layout container (VerticalLayoutGroup with 16px spacing, child force expand width = false) ---
            GameObject menuContainerObj = new GameObject("MenuContainer", typeof(RectTransform));
            menuContainerObj.transform.SetParent(startMenuPanelObj.transform, false);
            RectTransform menuContainerRt = menuContainerObj.GetComponent<RectTransform>();
            menuContainerRt.anchorMin = new Vector2(0.5f, 0.5f);
            menuContainerRt.anchorMax = new Vector2(0.5f, 0.5f);
            menuContainerRt.pivot = new Vector2(0.5f, 0.5f);
            menuContainerRt.anchoredPosition = new Vector2(0f, -75f);
            menuContainerRt.sizeDelta = new Vector2(360f, 200f);
            menuContainerRt.localScale = Vector3.one;

            VerticalLayoutGroup menuVlg = menuContainerObj.AddComponent<VerticalLayoutGroup>();
            menuVlg.spacing = 16f;
            menuVlg.childAlignment = TextAnchor.MiddleCenter;
            menuVlg.childControlWidth = false;
            menuVlg.childControlHeight = false;
            menuVlg.childForceExpandWidth = false;
            menuVlg.childForceExpandHeight = false;

            // Buttons: "START MISSION", "SETTINGS", and "QUIT" (Cyber button components with smooth hover scale 1.05x)
            Button startBtn = CreateCyberButton(menuContainerObj.transform, "Btn_Start", Vector2.zero, new Vector2(340f, 56f), "START MISSION", 22f, fontAsset);
            Button settingsBtn = CreateCyberButton(menuContainerObj.transform, "Btn_Settings", Vector2.zero, new Vector2(340f, 50f), "SETTINGS", 18f, fontAsset);
            Button exitBtn = CreateCyberButton(menuContainerObj.transform, "Btn_Exit", Vector2.zero, new Vector2(340f, 50f), "QUIT", 18f, fontAsset);

            if (gm != null)
            {
                while (startBtn.onClick.GetPersistentEventCount() > 0)
                {
                    UnityEventTools.RemovePersistentListener(startBtn.onClick, 0);
                }
                UnityEventTools.AddPersistentListener(startBtn.onClick, gm.ResetGame);
                gm.StartBtnImage = startBtn.GetComponent<Image>();
                gm.StartBtnLabel = startBtn.GetComponentInChildren<TextMeshProUGUI>();

                UnityEventTools.AddPersistentListener(settingsBtn.onClick, gm.OpenSettingsFromStart);
                UnityEventTools.AddPersistentListener(exitBtn.onClick, gm.QuitGame);
            }

            // --- Secondary Action Row: Eye Comfort Filter toggle ---
            Button eyeFilterBtn = CreateCyberButton(startMenuPanelObj.transform, "Btn_EyeFilter", new Vector2(0f, -195f), new Vector2(240f, 36f), "APPLY EYE FILTER MODE", 13f, fontAsset);

            // --- Footer Banner: Left "PRESS SPACE OR CLICK START MISSION", Right "AN ORIGINAL 2D ADVENTURE • ALL RIGHTS RESERVED" ---
            GameObject footerLeftObj = new GameObject("Footer_Badge_Left", typeof(RectTransform));
            footerLeftObj.transform.SetParent(startMenuPanelObj.transform, false);
            RectTransform footerLeftRt = footerLeftObj.GetComponent<RectTransform>();
            footerLeftRt.anchorMin = new Vector2(0f, 0f);
            footerLeftRt.anchorMax = new Vector2(0f, 0f);
            footerLeftRt.pivot = new Vector2(0f, 0f);
            footerLeftRt.anchoredPosition = new Vector2(40f, 25f);
            footerLeftRt.sizeDelta = new Vector2(380f, 26f);
            footerLeftRt.localScale = Vector3.one;

            TextMeshProUGUI footerLeftTmp = footerLeftObj.AddComponent<TextMeshProUGUI>();
            footerLeftTmp.text = "PRESS SPACE OR CLICK START MISSION";
            ConfigureTMPText(footerLeftTmp, fontAsset, fontSize: 13f, alignment: TextAlignmentOptions.Left, color: ColorMutedSlate);

            GameObject footerRightObj = new GameObject("Footer_Text_Right", typeof(RectTransform));
            footerRightObj.transform.SetParent(startMenuPanelObj.transform, false);
            RectTransform footerRightRt = footerRightObj.GetComponent<RectTransform>();
            footerRightRt.anchorMin = new Vector2(1f, 0f);
            footerRightRt.anchorMax = new Vector2(1f, 0f);
            footerRightRt.pivot = new Vector2(1f, 0f);
            footerRightRt.anchoredPosition = new Vector2(-40f, 25f);
            footerRightRt.sizeDelta = new Vector2(460f, 26f);
            footerRightRt.localScale = Vector3.one;

            TextMeshProUGUI footerRightTmp = footerRightObj.AddComponent<TextMeshProUGUI>();
            footerRightTmp.text = "AN ORIGINAL 2D ADVENTURE • ALL RIGHTS RESERVED";
            ConfigureTMPText(footerRightTmp, fontAsset, fontSize: 13f, alignment: TextAlignmentOptions.Right, color: ColorMutedSlate);

            startMenuPanelObj.SetActive(true);

            // 4. Build GameOver Panel (Dark Desaturated Charcoal #0f141d with 90% opacity, Glitch Crimson #ff0055 accent)
            GameObject gameOverPanelObj = new GameObject("GameOverPanel", typeof(RectTransform));
            gameOverPanelObj.transform.SetParent(canvasObj.transform, false);
            RectTransform goRect = gameOverPanelObj.GetComponent<RectTransform>();
            goRect.anchorMin = Vector2.zero;
            goRect.anchorMax = Vector2.one;
            goRect.offsetMin = Vector2.zero;
            goRect.offsetMax = Vector2.zero;
            goRect.localScale = Vector3.one;

            Image goBg = gameOverPanelObj.AddComponent<Image>();
            goBg.sprite = GetOrCreateCyberPanelSprite();
            goBg.type = Image.Type.Sliced;
            goBg.color = Color.white;

            // Header: "CRITICAL FAILURE" in Glitch Crimson #ff0055
            GameObject goHeaderObj = new GameObject("Header_Text", typeof(RectTransform));
            goHeaderObj.transform.SetParent(gameOverPanelObj.transform, false);
            RectTransform goHeaderRect = goHeaderObj.GetComponent<RectTransform>();
            goHeaderRect.anchorMin = new Vector2(0.5f, 0.5f);
            goHeaderRect.anchorMax = new Vector2(0.5f, 0.5f);
            goHeaderRect.pivot = new Vector2(0.5f, 0.5f);
            goHeaderRect.anchoredPosition = new Vector2(0f, 105f);
            goHeaderRect.sizeDelta = new Vector2(900f, 80f);
            goHeaderRect.localScale = Vector3.one;

            TextMeshProUGUI goHeaderTmp = goHeaderObj.AddComponent<TextMeshProUGUI>();
            goHeaderTmp.text = "CRITICAL FAILURE";
            ConfigureTMPText(goHeaderTmp, fontAsset, fontSize: 56f, alignment: TextAlignmentOptions.Center, color: ColorGlitchCrimson);

            // Score: "NODES PURGED: 0"
            GameObject goScoreObj = new GameObject("GameOverScore_Text", typeof(RectTransform));
            goScoreObj.transform.SetParent(gameOverPanelObj.transform, false);
            RectTransform goScoreRect = goScoreObj.GetComponent<RectTransform>();
            goScoreRect.anchorMin = new Vector2(0.5f, 0.5f);
            goScoreRect.anchorMax = new Vector2(0.5f, 0.5f);
            goScoreRect.pivot = new Vector2(0.5f, 0.5f);
            goScoreRect.anchoredPosition = new Vector2(0f, 25f);
            goScoreRect.sizeDelta = new Vector2(600f, 45f);
            goScoreRect.localScale = Vector3.one;

            TextMeshProUGUI goScoreTmp = goScoreObj.AddComponent<TextMeshProUGUI>();
            goScoreTmp.text = "NODES PURGED: 0";
            ConfigureTMPText(goScoreTmp, fontAsset, fontSize: 32f, alignment: TextAlignmentOptions.Center, color: ColorCrispWhite);

            GameObject goStatsObj = new GameObject("GameOverStats_Text", typeof(RectTransform));
            goStatsObj.transform.SetParent(gameOverPanelObj.transform, false);
            RectTransform goStatsRect = goStatsObj.GetComponent<RectTransform>();
            goStatsRect.anchorMin = new Vector2(0.5f, 0.5f);
            goStatsRect.anchorMax = new Vector2(0.5f, 0.5f);
            goStatsRect.pivot = new Vector2(0.5f, 0.5f);
            goStatsRect.anchoredPosition = new Vector2(0f, -20f);
            goStatsRect.sizeDelta = new Vector2(800f, 35f);
            goStatsRect.localScale = Vector3.one;

            TextMeshProUGUI goStatsTmp = goStatsObj.AddComponent<TextMeshProUGUI>();
            goStatsTmp.text = "";
            ConfigureTMPText(goStatsTmp, fontAsset, fontSize: 20f, alignment: TextAlignmentOptions.Center, color: ColorMutedSlate);

            // Action Buttons
            Button restartBtn = CreateGlitchCrimsonButton(gameOverPanelObj.transform, "Btn_Restart", new Vector2(0f, -85f), new Vector2(400f, 54f), "REBOOT (R)", 22f, fontAsset);
            Button goSettingsBtn = CreateCyberButton(gameOverPanelObj.transform, "Btn_Settings", new Vector2(0f, -145f), new Vector2(400f, 48f), "SETTINGS", 18f, fontAsset);
            Button goMenuBtn = CreateCyberButton(gameOverPanelObj.transform, "Btn_GameOverMenu", new Vector2(0f, -200f), new Vector2(400f, 48f), "MAIN MENU", 18f, fontAsset);

            if (gm != null)
            {
                while (restartBtn.onClick.GetPersistentEventCount() > 0)
                {
                    UnityEventTools.RemovePersistentListener(restartBtn.onClick, 0);
                }
                UnityEventTools.AddPersistentListener(restartBtn.onClick, gm.ResetGame);
                UnityEventTools.AddPersistentListener(goSettingsBtn.onClick, gm.OpenSettingsFromPause);
                UnityEventTools.AddPersistentListener(goMenuBtn.onClick, gm.ExitToMenu);
                gm.GameOverScoreText = goScoreTmp;
                gm.GameOverStatsText = goStatsTmp;
            }

            gameOverPanelObj.SetActive(false);

            // 5. Build Victory Panel (Cyber Vector Palette, Electric Cyan #00f0ff accent)
            GameObject victoryPanelObj = new GameObject("VictoryPanel", typeof(RectTransform));
            victoryPanelObj.transform.SetParent(canvasObj.transform, false);
            RectTransform vicRect = victoryPanelObj.GetComponent<RectTransform>();
            vicRect.anchorMin = Vector2.zero;
            vicRect.anchorMax = Vector2.one;
            vicRect.offsetMin = Vector2.zero;
            vicRect.offsetMax = Vector2.zero;
            vicRect.localScale = Vector3.one;

            Image vicBg = victoryPanelObj.AddComponent<Image>();
            vicBg.sprite = GetOrCreateCyberPanelSprite();
            vicBg.type = Image.Type.Sliced;
            vicBg.color = Color.white;

            GameObject vicTitleObj = new GameObject("Title_Text", typeof(RectTransform));
            vicTitleObj.transform.SetParent(victoryPanelObj.transform, false);
            RectTransform vicTitleRect = vicTitleObj.GetComponent<RectTransform>();
            vicTitleRect.anchorMin = new Vector2(0.5f, 0.5f);
            vicTitleRect.anchorMax = new Vector2(0.5f, 0.5f);
            vicTitleRect.pivot = new Vector2(0.5f, 0.5f);
            vicTitleRect.anchoredPosition = new Vector2(0f, 130f);
            vicTitleRect.sizeDelta = new Vector2(900f, 75f);
            vicTitleRect.localScale = Vector3.one;

            TextMeshProUGUI vicTitleTmp = vicTitleObj.AddComponent<TextMeshProUGUI>();
            vicTitleTmp.text = "NEURAL PURGE COMPLETE";
            ConfigureTMPText(vicTitleTmp, fontAsset, fontSize: 48f, alignment: TextAlignmentOptions.Center, color: ColorElectricCyan);

            GameObject vicSubObj = new GameObject("Subtitle_Text", typeof(RectTransform));
            vicSubObj.transform.SetParent(victoryPanelObj.transform, false);
            RectTransform vicSubRect = vicSubObj.GetComponent<RectTransform>();
            vicSubRect.anchorMin = new Vector2(0.5f, 0.5f);
            vicSubRect.anchorMax = new Vector2(0.5f, 0.5f);
            vicSubRect.pivot = new Vector2(0.5f, 0.5f);
            vicSubRect.anchoredPosition = new Vector2(0f, 72f);
            vicSubRect.sizeDelta = new Vector2(700f, 36f);
            vicSubRect.localScale = Vector3.one;

            TextMeshProUGUI vicSubTmp = vicSubObj.AddComponent<TextMeshProUGUI>();
            vicSubTmp.text = "SECTOR SECURED";
            ConfigureTMPText(vicSubTmp, fontAsset, fontSize: 22f, alignment: TextAlignmentOptions.Center, color: ColorCrispWhite);

            Button nextLvlBtn = CreateCyberButton(victoryPanelObj.transform, "Btn_NextLevel", new Vector2(0f, 15f), new Vector2(400f, 52f), "ADVANCE TO NEXT LEVEL", 20f, fontAsset);
            Button replayBtn = CreateCyberButton(victoryPanelObj.transform, "Btn_Replay", new Vector2(0f, -45f), new Vector2(400f, 48f), "RETRY (R)", 18f, fontAsset);
            Button vicSettingsBtn = CreateCyberButton(victoryPanelObj.transform, "Btn_Settings", new Vector2(0f, -100f), new Vector2(400f, 48f), "SETTINGS", 18f, fontAsset);
            Button vicMenuBtn = CreateCyberButton(victoryPanelObj.transform, "Btn_VictoryMenu", new Vector2(0f, -155f), new Vector2(400f, 48f), "MAIN MENU", 18f, fontAsset);

            if (gm != null)
            {
                while (nextLvlBtn.onClick.GetPersistentEventCount() > 0)
                {
                    UnityEventTools.RemovePersistentListener(nextLvlBtn.onClick, 0);
                }
                UnityEventTools.AddPersistentListener(nextLvlBtn.onClick, gm.NextLevel);

                while (replayBtn.onClick.GetPersistentEventCount() > 0)
                {
                    UnityEventTools.RemovePersistentListener(replayBtn.onClick, 0);
                }
                UnityEventTools.AddPersistentListener(replayBtn.onClick, gm.RestartGame);

                while (vicSettingsBtn.onClick.GetPersistentEventCount() > 0)
                {
                    UnityEventTools.RemovePersistentListener(vicSettingsBtn.onClick, 0);
                }
                UnityEventTools.AddPersistentListener(vicSettingsBtn.onClick, gm.OpenSettingsFromVictory);

                while (vicMenuBtn.onClick.GetPersistentEventCount() > 0)
                {
                    UnityEventTools.RemovePersistentListener(vicMenuBtn.onClick, 0);
                }
                UnityEventTools.AddPersistentListener(vicMenuBtn.onClick, gm.ExitToMenu);
            }

            victoryPanelObj.SetActive(false);

            // 6. Build Pause Panel (Cyber Vector Palette, Timer Yellow #ffe600 accent)
            GameObject pausePanelObj = new GameObject("PausePanel", typeof(RectTransform));
            pausePanelObj.transform.SetParent(canvasObj.transform, false);
            RectTransform pauseRect = pausePanelObj.GetComponent<RectTransform>();
            pauseRect.anchorMin = Vector2.zero;
            pauseRect.anchorMax = Vector2.one;
            pauseRect.offsetMin = Vector2.zero;
            pauseRect.offsetMax = Vector2.zero;
            pauseRect.localScale = Vector3.one;

            Image pauseBg = pausePanelObj.AddComponent<Image>();
            pauseBg.sprite = GetOrCreateCyberPanelSprite();
            pauseBg.type = Image.Type.Sliced;
            pauseBg.color = Color.white;

            GameObject pauseTitleObj = new GameObject("Title_Text", typeof(RectTransform));
            pauseTitleObj.transform.SetParent(pausePanelObj.transform, false);
            RectTransform pauseTitleRect = pauseTitleObj.GetComponent<RectTransform>();
            pauseTitleRect.anchorMin = new Vector2(0.5f, 0.5f);
            pauseTitleRect.anchorMax = new Vector2(0.5f, 0.5f);
            pauseTitleRect.pivot = new Vector2(0.5f, 0.5f);
            pauseTitleRect.anchoredPosition = new Vector2(0f, 130f);
            pauseTitleRect.sizeDelta = new Vector2(800f, 70f);
            pauseTitleRect.localScale = Vector3.one;

            TextMeshProUGUI pauseTitleTmp = pauseTitleObj.AddComponent<TextMeshProUGUI>();
            pauseTitleTmp.text = "SYSTEM PAUSED";
            ConfigureTMPText(pauseTitleTmp, fontAsset, fontSize: 46f, alignment: TextAlignmentOptions.Center, color: ColorTimerYellow);

            Button resumeBtn = CreateCyberButton(pausePanelObj.transform, "Btn_Resume", new Vector2(0f, 65f), new Vector2(380f, 52f), "RESUME (ESC)", 20f, fontAsset);
            Button pauseSettingsBtn = CreateCyberButton(pausePanelObj.transform, "Btn_Settings", new Vector2(0f, 8f), new Vector2(380f, 48f), "SETTINGS", 18f, fontAsset);
            Button pauseMenuBtn = CreateCyberButton(pausePanelObj.transform, "Btn_PauseMenu", new Vector2(0f, -48f), new Vector2(380f, 48f), "MAIN MENU", 18f, fontAsset);
            Button quitBtn = CreateGlitchCrimsonButton(pausePanelObj.transform, "Btn_Quit", new Vector2(0f, -108f), new Vector2(380f, 48f), "EXIT GAME", 18f, fontAsset);

            if (gm != null)
            {
                while (resumeBtn.onClick.GetPersistentEventCount() > 0)
                {
                    UnityEventTools.RemovePersistentListener(resumeBtn.onClick, 0);
                }
                UnityEventTools.AddPersistentListener(resumeBtn.onClick, gm.ResumeGame);

                while (pauseSettingsBtn.onClick.GetPersistentEventCount() > 0)
                {
                    UnityEventTools.RemovePersistentListener(pauseSettingsBtn.onClick, 0);
                }
                UnityEventTools.AddPersistentListener(pauseSettingsBtn.onClick, gm.OpenSettingsFromPause);

                while (pauseMenuBtn.onClick.GetPersistentEventCount() > 0)
                {
                    UnityEventTools.RemovePersistentListener(pauseMenuBtn.onClick, 0);
                }
                UnityEventTools.AddPersistentListener(pauseMenuBtn.onClick, gm.ExitToMenu);

                while (quitBtn.onClick.GetPersistentEventCount() > 0)
                {
                    UnityEventTools.RemovePersistentListener(quitBtn.onClick, 0);
                }
                UnityEventTools.AddPersistentListener(quitBtn.onClick, gm.QuitGame);
            }

            pausePanelObj.SetActive(false);

            // 7. Update GameManager References:
            if (gm != null)
            {
                gm.TimerText = timerTmp;
                gm.ScoreText = scoreTmp;
                gm.ComboText = multTmp;
                gm.StartPanel = startPanelObj;
                gm.GameOverPanel = gameOverPanelObj;
                gm.VictoryPanel = victoryPanelObj;
                gm.victoryPanel = victoryPanelObj;
                gm.PausePanel = pausePanelObj;
                gm.pausePanel = pausePanelObj;
                gm.HudPanel = hudPanelObj;
                gm.HealthBarUIComponent = healthUI;
                gm.nextLevelBtn = nextLvlBtn.gameObject;
                gm.NextLevelBtn = nextLvlBtn.gameObject;
                gm.nextLevelButton = nextLvlBtn;
                gm.nextLevelButtonText = nextLvlBtn.GetComponentInChildren<TextMeshProUGUI>();
                gm.replayLevelButton = replayBtn;
                gm.victoryTitleText = vicTitleTmp;
                gm.VictoryStatsText = vicSubTmp;

                SerializedObject gmSo = new SerializedObject(gm);
                SetSerializedProp(gmSo, "timerText", timerTmp);
                SetSerializedProp(gmSo, "scoreText", scoreTmp);
                SetSerializedProp(gmSo, "comboText", multTmp);
                SetSerializedProp(gmSo, "startPanel", startPanelObj);
                SetSerializedProp(gmSo, "gameOverPanel", gameOverPanelObj);
                SetSerializedProp(gmSo, "victoryPanel", victoryPanelObj);
                SetSerializedProp(gmSo, "pausePanel", pausePanelObj);
                SetSerializedProp(gmSo, "hudPanel", hudPanelObj);
                SetSerializedProp(gmSo, "healthBarUI", healthUI);
                SetSerializedProp(gmSo, "startBtnImage", startBtn.GetComponent<Image>());
                SetSerializedProp(gmSo, "startBtnLabel", startBtn.GetComponentInChildren<TextMeshProUGUI>());
                SetSerializedProp(gmSo, "easyButton", easyBtn);
                SetSerializedProp(gmSo, "normalButton", normalBtn);
                SetSerializedProp(gmSo, "expertButton", expertBtn);
                SetSerializedProp(gmSo, "easyBtnImage", easyBtn.GetComponent<Image>());
                SetSerializedProp(gmSo, "normalBtnImage", normalBtn.GetComponent<Image>());
                SetSerializedProp(gmSo, "expertBtnImage", expertBtn.GetComponent<Image>());
                SetSerializedProp(gmSo, "nextLevelBtn", nextLvlBtn.gameObject);
                SetSerializedProp(gmSo, "nextLevelButton", nextLvlBtn);
                SetSerializedProp(gmSo, "nextLevelButtonText", nextLvlBtn.GetComponentInChildren<TextMeshProUGUI>());
                SetSerializedProp(gmSo, "replayLevelButton", replayBtn);
                SetSerializedProp(gmSo, "gameOverScoreText", goScoreTmp);
                SetSerializedProp(gmSo, "gameOverStatsText", goStatsTmp);
                SetSerializedProp(gmSo, "victoryStatsText", vicSubTmp);
                SetSerializedProp(gmSo, "victoryTitleText", vicTitleTmp);
                gmSo.ApplyModifiedProperties();

                // Apply default Level 1 (Easy mode: 20s)
                gm.currentLevel = 1;
                gm.SetDifficulty(0);

                // Scan Kenney audio folders and link pleasant SFX to GameManager
                SetupGameAudio(gm);
            }

            // Explicitly destroy any custom UI reticle to preserve native hardware cursor
            Transform existingReticle = canvasObj.transform.Find("Tactical_Reticle");
            if (existingReticle != null)
            {
                Object.DestroyImmediate(existingReticle.gameObject);
            }
            if (playerObj != null)
            {
                RaycastShooter shooter = playerObj.GetComponent<RaycastShooter>();
                if (shooter != null)
                {
                    shooter.customCrosshairUI = null;
                }
            }
            Cursor.visible = true;

            // Build ScreenEffectOverlay
            Transform existingOverlay = canvasObj.transform.Find("ScreenEffectOverlay");
            if (existingOverlay != null) Object.DestroyImmediate(existingOverlay.gameObject);

            GameObject screenOverlayObj = new GameObject("ScreenEffectOverlay", typeof(RectTransform), typeof(Image));
            screenOverlayObj.transform.SetParent(canvasObj.transform, false);
            RectTransform soRt = screenOverlayObj.GetComponent<RectTransform>();
            soRt.anchorMin = Vector2.zero;
            soRt.anchorMax = Vector2.one;
            soRt.offsetMin = Vector2.zero;
            soRt.offsetMax = Vector2.zero;
            soRt.localScale = Vector3.one;
            Image soImg = screenOverlayObj.GetComponent<Image>();
            soImg.sprite = HealthBarUI.GetOrCreateWhiteSprite();
            soImg.color = Color.clear;
            soImg.raycastTarget = false;
            screenOverlayObj.SetActive(false);

            // Build EyeFilterOverlay (Warm sepia/amber screen filter overlay)
            Transform existingEyeOverlay = canvasObj.transform.Find("EyeFilterOverlay");
            if (existingEyeOverlay != null) Object.DestroyImmediate(existingEyeOverlay.gameObject);

            GameObject eyeFilterOverlayObj = new GameObject("EyeFilterOverlay", typeof(RectTransform), typeof(Image));
            eyeFilterOverlayObj.transform.SetParent(canvasObj.transform, false);
            RectTransform efRt = eyeFilterOverlayObj.GetComponent<RectTransform>();
            efRt.anchorMin = Vector2.zero;
            efRt.anchorMax = Vector2.one;
            efRt.offsetMin = Vector2.zero;
            efRt.offsetMax = Vector2.zero;
            efRt.localScale = Vector3.one;
            Image efImg = eyeFilterOverlayObj.GetComponent<Image>();
            efImg.sprite = HealthBarUI.GetOrCreateWhiteSprite();
            efImg.color = Color.clear;
            efImg.raycastTarget = false;
            eyeFilterOverlayObj.SetActive(false);

            // Build Toast_Banner (Floating notification banner at bottom-center)
            Transform existingToast = canvasObj.transform.Find("Toast_Banner");
            if (existingToast != null) Object.DestroyImmediate(existingToast.gameObject);

            GameObject toastObj = new GameObject("Toast_Banner", typeof(RectTransform));
            toastObj.transform.SetParent(canvasObj.transform, false);
            RectTransform toastRt = toastObj.GetComponent<RectTransform>();
            toastRt.anchorMin = new Vector2(0.5f, 0f);
            toastRt.anchorMax = new Vector2(0.5f, 0f);
            toastRt.pivot = new Vector2(0.5f, 0f);
            toastRt.anchoredPosition = new Vector2(0f, 45f);
            toastRt.sizeDelta = new Vector2(420f, 44f);
            toastRt.localScale = Vector3.one;

            Image toastImg = toastObj.AddComponent<Image>();
            toastImg.sprite = GetOrCreateCardRowSprite();
            toastImg.type = Image.Type.Sliced;
            toastImg.color = ColorToastBg; // #091A2F

            CanvasGroup toastCg = toastObj.AddComponent<CanvasGroup>();
            toastCg.alpha = 0f;
            toastCg.blocksRaycasts = false;

            GameObject toastTextObj = new GameObject("Toast_Text", typeof(RectTransform));
            toastTextObj.transform.SetParent(toastObj.transform, false);
            RectTransform ttRt = toastTextObj.GetComponent<RectTransform>();
            ttRt.anchorMin = Vector2.zero;
            ttRt.anchorMax = Vector2.one;
            ttRt.offsetMin = new Vector2(16f, 0f);
            ttRt.offsetMax = new Vector2(-16f, 0f);
            ttRt.localScale = Vector3.one;

            TextMeshProUGUI toastTmp = toastTextObj.AddComponent<TextMeshProUGUI>();
            toastTmp.text = "";
            ConfigureTMPText(toastTmp, fontAsset, fontSize: 14f, alignment: TextAlignmentOptions.Center, color: ColorSubtitleCyan);

            toastObj.SetActive(false);

            // Build UIManager & System Settings Modal Dialog
            UIManager uiMgr = canvasObj.GetComponent<UIManager>() ?? canvasObj.AddComponent<UIManager>();
            uiMgr.startMenuPanel = startMenuPanelObj;
            uiMgr.hudPanel = hudPanelObj;
            uiMgr.pausePanel = pausePanelObj;
            uiMgr.gameOverPanel = gameOverPanelObj;
            uiMgr.victoryPanel = victoryPanelObj;
            uiMgr.eyeFilterOverlay = efImg;
            uiMgr.eyeFilterButton = eyeFilterBtn;
            uiMgr.eyeFilterButtonText = eyeFilterBtn.GetComponentInChildren<TextMeshProUGUI>();
            uiMgr.audioQuickToggleBtn = audioToggleBtn;
            uiMgr.audioQuickToggleText = audioToggleBtn.GetComponentInChildren<TextMeshProUGUI>();
            uiMgr.toastBanner = toastObj;
            uiMgr.toastCanvasGroup = toastCg;
            uiMgr.toastText = toastTmp;

            SettingsManager sm = gm != null ? (gm.GetComponent<SettingsManager>() ?? gm.gameObject.AddComponent<SettingsManager>()) : null;
            if (sm != null)
            {
                sm.screenEffectOverlay = soImg;
            }

            GameObject settingsModalObj = BuildSettingsModalPanel(canvasObj, uiMgr, sm, fontAsset);
            if (uiMgr != null)
            {
                uiMgr.settingsModalPanel = settingsModalObj;
                uiMgr.BindUIEvents();
            }

            // Ensure crisp UI quality across the canvas
            EnsureCrispUIQuality(canvas);

            Debug.Log("[GameJam] Modern Crisp Vector UI hierarchy rebuilt and linked successfully.");
        }

        private static void SetupGameAudio(GameManager gm)
        {
            if (gm == null) return;

            AudioSource aSource = gm.GetComponent<AudioSource>();
            if (aSource == null)
            {
                aSource = gm.gameObject.AddComponent<AudioSource>();
            }
            aSource.playOnAwake = false;
            gm.audioSource = aSource;

            // Dedicated shoot and hit audio sources for smooth, crisp arcade sound mixing
            if (gm.shootAudioSource == null)
            {
                gm.shootAudioSource = gm.gameObject.AddComponent<AudioSource>();
                gm.shootAudioSource.playOnAwake = false;
            }
            if (gm.hitAudioSource == null)
            {
                gm.hitAudioSource = gm.gameObject.AddComponent<AudioSource>();
                gm.hitAudioSource.playOnAwake = false;
            }

            // Scan Kenney audio folders and link clips:
            // Laser/pew sound -> crisp tactile mechanical switch
            AudioClip shotClip = FindKenneyAudioClip("switch_001", "switch", "pluck_001", "click_001", "glitch_001", "laser", "pew");
            // Explosion/impact sound -> crystal glass/gem pop with high harmonic clarity
            AudioClip hitClip = FindKenneyAudioClip("glass_004", "glass_002", "glass", "drop_001", "drop", "impact", "hit");
            // Hurt sound -> soft muffled low-frequency bubble drop
            AudioClip hurtClip = FindKenneyAudioClip("drop_001", "error_001", "bong_001", "error", "hurt");
            // Celebration chord jingle -> uplifting confirmation chime
            AudioClip victoryClip = FindKenneyAudioClip("confirmation_001", "confirmation_002", "maximize_001", "maximize", "victory", "chime");
            // Defeat sound -> gentle descending sequence
            AudioClip defeatClip = FindKenneyAudioClip("minimize_005", "minimize_001", "error_008", "error_007", "defeat", "buzzer");

            gm.shotSFX = shotClip;
            gm.enemyHitSFX = hitClip;
            gm.playerHurtSFX = hurtClip;
            gm.victorySFX = victoryClip;
            gm.defeatSFX = defeatClip;

            SerializedObject gmSo = new SerializedObject(gm);
            SetSerializedAudioProp(gmSo, "audioSource", aSource);
            SetSerializedAudioProp(gmSo, "shootAudioSource", gm.shootAudioSource);
            SetSerializedAudioProp(gmSo, "hitAudioSource", gm.hitAudioSource);
            SetSerializedAudioProp(gmSo, "shotSFX", shotClip);
            SetSerializedAudioProp(gmSo, "enemyHitSFX", hitClip);
            SetSerializedAudioProp(gmSo, "playerHurtSFX", hurtClip);
            SetSerializedAudioProp(gmSo, "victorySFX", victoryClip);
            SetSerializedAudioProp(gmSo, "defeatSFX", defeatClip);
            gmSo.ApplyModifiedProperties();

            Debug.Log($"[GameJam] Audio clips scanned and linked to GameManager: Shot='{shotClip?.name}', EnemyHit='{hitClip?.name}', PlayerHurt='{hurtClip?.name}', Victory='{victoryClip?.name}', Defeat='{defeatClip?.name}'.");
        }

        private static void SetSerializedProp(SerializedObject so, string propName, Object value)
        {
            if (so == null) return;
            SerializedProperty prop = so.FindProperty(propName);
            if (prop != null)
            {
                prop.objectReferenceValue = value;
            }
        }

        private static void SetSerializedAudioProp(SerializedObject so, string propName, Object value)
        {
            SetSerializedProp(so, propName, value);
        }

        private static AudioClip FindKenneyAudioClip(params string[] preferredKeywords)
        {
            string[] guids = AssetDatabase.FindAssets("t:AudioClip");
            foreach (string keyword in preferredKeywords)
            {
                foreach (string guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    string fileName = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                    if (fileName.Contains(keyword.ToLowerInvariant()))
                    {
                        AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                        if (clip != null) return clip;
                    }
                }
            }

            if (guids.Length > 0)
            {
                return AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(guids[0]));
            }
            return null;
        }

        private static GameObject GetOrRecreateChild(Transform parent, string childName)
        {
            Transform child = parent.Find(childName);
            if (child != null)
            {
                Object.DestroyImmediate(child.gameObject);
            }
            GameObject newObj = new GameObject(childName, typeof(RectTransform));
            newObj.transform.SetParent(parent, false);
            return newObj;
        }


        private static Button CreateFlatButton(
            Transform parent,
            string btnName,
            Vector2 anchoredPos,
            Vector2 size,
            string text,
            Color normalColor,
            Color textColor,
            float fontSize = 22f,
            Color? highlightColor = null,
            TMP_FontAsset fontAsset = null)
        {
            if (fontAsset == null) fontAsset = GetOrCreateDefaultTMPFont();

            GameObject btnObj = GetOrRecreateChild(parent, btnName);
            RectTransform btnRect = btnObj.GetComponent<RectTransform>();
            btnRect.anchorMin = new Vector2(0.5f, 0.5f);
            btnRect.anchorMax = new Vector2(0.5f, 0.5f);
            btnRect.pivot = new Vector2(0.5f, 0.5f);
            btnRect.anchoredPosition = anchoredPos;
            btnRect.sizeDelta = size;
            btnRect.localScale = Vector3.one;

            Image btnImg = btnObj.GetComponent<Image>() ?? btnObj.AddComponent<Image>();
            btnImg.sprite = HealthBarUI.GetOrCreateWhiteSprite();
            btnImg.type = Image.Type.Simple;
            btnImg.color = normalColor;

            Button btn = btnObj.GetComponent<Button>() ?? btnObj.AddComponent<Button>();
            btn.transition = Selectable.Transition.ColorTint;
            btn.targetGraphic = btnImg;

            ColorBlock cb = btn.colors;
            cb.normalColor = normalColor;
            Color hColor = highlightColor.HasValue ? highlightColor.Value : ColorButtonHoverHighlight;
            cb.highlightedColor = hColor;
            cb.selectedColor = hColor;
            cb.pressedColor = new Color(Mathf.Max(normalColor.r - 0.08f, 0f), Mathf.Max(normalColor.g - 0.08f, 0f), Mathf.Max(normalColor.b - 0.08f, 0f), normalColor.a);
            cb.disabledColor = new Color(0.2f, 0.2f, 0.2f, 0.5f);
            cb.colorMultiplier = 1f;
            // Attach smooth hover scale effect (Vector3 scale 1.05x on pointer enter/select)
            UIHoverScaleEffect hoverEffect = btnObj.GetComponent<UIHoverScaleEffect>() ?? btnObj.AddComponent<UIHoverScaleEffect>();

            // Layout element to guarantee button retains fixed dimensions inside layouts
            LayoutElement le = btnObj.GetComponent<LayoutElement>() ?? btnObj.AddComponent<LayoutElement>();
            le.minWidth = size.x;
            le.minHeight = size.y;
            le.preferredWidth = size.x;
            le.preferredHeight = size.y;

            // Zero ContentSizeFitter Enforcement: strictly destroy any ContentSizeFitter on button
            ContentSizeFitter btnCsf = btnObj.GetComponent<ContentSizeFitter>();
            if (btnCsf != null)
            {
                Object.DestroyImmediate(btnCsf);
            }

            GameObject labelObj = GetOrRecreateChild(btnObj.transform, "Text");
            RectTransform labelRect = labelObj.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            labelRect.sizeDelta = new Vector2(Mathf.Max(size.x, 520f), Mathf.Max(size.y, 60f));
            labelRect.localScale = Vector3.one;

            ContentSizeFitter labelCsf = labelObj.GetComponent<ContentSizeFitter>();
            if (labelCsf != null)
            {
                Object.DestroyImmediate(labelCsf);
            }

            TextMeshProUGUI labelTmp = labelObj.GetComponent<TextMeshProUGUI>() ?? labelObj.AddComponent<TextMeshProUGUI>();
            labelTmp.text = text;
            labelTmp.textWrappingMode = TextWrappingModes.NoWrap;
            labelTmp.overflowMode = TextOverflowModes.Overflow;
            ConfigureTMPText(labelTmp, fontAsset, fontSize: fontSize, alignment: TextAlignmentOptions.Center, color: textColor);

            return btn;
        }

        private static Slider CreateFlatSlider(
            Transform parent,
            string name,
            Vector2 anchoredPos,
            Vector2 size,
            float minValue,
            float maxValue,
            float defaultValue)
        {
            GameObject sliderObj = new GameObject(name, typeof(RectTransform));
            sliderObj.transform.SetParent(parent, false);
            RectTransform sliderRt = sliderObj.GetComponent<RectTransform>();
            sliderRt.anchoredPosition = anchoredPos;
            sliderRt.sizeDelta = size;
            sliderRt.localScale = Vector3.one;

            Sprite whiteSp = HealthBarUI.GetOrCreateWhiteSprite();

            // Background
            GameObject bgObj = new GameObject("Background", typeof(RectTransform), typeof(Image));
            bgObj.transform.SetParent(sliderObj.transform, false);
            RectTransform bgRt = bgObj.GetComponent<RectTransform>();
            bgRt.anchorMin = new Vector2(0f, 0.35f);
            bgRt.anchorMax = new Vector2(1f, 0.65f);
            bgRt.offsetMin = Vector2.zero;
            bgRt.offsetMax = Vector2.zero;
            Image bgImg = bgObj.GetComponent<Image>();
            bgImg.sprite = whiteSp;
            bgImg.color = ColorButtonCharcoal;

            // Fill Area
            GameObject fillAreaObj = new GameObject("Fill Area", typeof(RectTransform));
            fillAreaObj.transform.SetParent(sliderObj.transform, false);
            RectTransform fillAreaRt = fillAreaObj.GetComponent<RectTransform>();
            fillAreaRt.anchorMin = new Vector2(0f, 0.35f);
            fillAreaRt.anchorMax = new Vector2(1f, 0.65f);
            fillAreaRt.offsetMin = Vector2.zero;
            fillAreaRt.offsetMax = Vector2.zero;

            GameObject fillObj = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fillObj.transform.SetParent(fillAreaObj.transform, false);
            RectTransform fillRt = fillObj.GetComponent<RectTransform>();
            fillRt.sizeDelta = Vector2.zero;
            Image fillImg = fillObj.GetComponent<Image>();
            fillImg.sprite = whiteSp;
            fillImg.color = ColorElectricCyan;

            // Handle Slide Area
            GameObject handleAreaObj = new GameObject("Handle Slide Area", typeof(RectTransform));
            handleAreaObj.transform.SetParent(sliderObj.transform, false);
            RectTransform handleAreaRt = handleAreaObj.GetComponent<RectTransform>();
            handleAreaRt.anchorMin = Vector2.zero;
            handleAreaRt.anchorMax = Vector2.one;
            handleAreaRt.offsetMin = new Vector2(8f, 0f);
            handleAreaRt.offsetMax = new Vector2(-8f, 0f);

            GameObject handleObj = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            handleObj.transform.SetParent(handleAreaObj.transform, false);
            RectTransform handleRt = handleObj.GetComponent<RectTransform>();
            handleRt.sizeDelta = new Vector2(18f, 0f);
            Image handleImg = handleObj.GetComponent<Image>();
            handleImg.sprite = whiteSp;
            handleImg.color = Color.white;

            Slider slider = sliderObj.AddComponent<Slider>();
            slider.fillRect = fillRt;
            slider.handleRect = handleRt;
            slider.targetGraphic = handleImg;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = minValue;
            slider.maxValue = maxValue;
            slider.value = defaultValue;

            return slider;
        }

        private static TextMeshProUGUI CreateLabel(
            Transform parent,
            string name,
            Vector2 anchoredPos,
            Vector2 size,
            string text,
            TMP_FontAsset fontAsset,
            float fontSize = 18f,
            TextAlignmentOptions alignment = TextAlignmentOptions.Left,
            Color? color = null)
        {
            GameObject labelObj = new GameObject(name, typeof(RectTransform));
            labelObj.transform.SetParent(parent, false);
            RectTransform rt = labelObj.GetComponent<RectTransform>();
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;
            rt.localScale = Vector3.one;

            TextMeshProUGUI tmp = labelObj.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            ConfigureTMPText(tmp, fontAsset, fontSize: fontSize, alignment: alignment, color: color ?? Color.white);
            return tmp;
        }

        private static GameObject BuildSettingsModalPanel(GameObject canvasObj, UIManager uiMgr, SettingsManager sm, TMP_FontAsset fontAsset)
        {
            if (canvasObj == null) return null;

            Transform oldSettings = canvasObj.transform.Find("SettingsPanel");
            if (oldSettings != null) Object.DestroyImmediate(oldSettings.gameObject);
            Transform oldModal = canvasObj.transform.Find("SettingsModalPanel");
            if (oldModal != null) Object.DestroyImmediate(oldModal.gameObject);

            // Fullscreen Modal Backdrop: #050E1C at 75% opacity
            GameObject settingsPanelObj = new GameObject("SettingsModalPanel", typeof(RectTransform));
            settingsPanelObj.transform.SetParent(canvasObj.transform, false);
            RectTransform spRt = settingsPanelObj.GetComponent<RectTransform>();
            spRt.anchorMin = Vector2.zero;
            spRt.anchorMax = Vector2.one;
            spRt.offsetMin = Vector2.zero;
            spRt.offsetMax = Vector2.zero;
            spRt.localScale = Vector3.one;

            Image spBg = settingsPanelObj.AddComponent<Image>();
            spBg.sprite = HealthBarUI.GetOrCreateWhiteSprite();
            spBg.type = Image.Type.Simple;
            spBg.color = ColorDarkBackdrop; // #000000 with 0.8 alpha

            // Centered Modal Dialog Card: Width 580, Height 540
            GameObject dialogObj = new GameObject("ModalDialog", typeof(RectTransform));
            dialogObj.transform.SetParent(settingsPanelObj.transform, false);
            RectTransform dialogRt = dialogObj.GetComponent<RectTransform>();
            dialogRt.anchorMin = new Vector2(0.5f, 0.5f);
            dialogRt.anchorMax = new Vector2(0.5f, 0.5f);
            dialogRt.pivot = new Vector2(0.5f, 0.5f);
            dialogRt.anchoredPosition = Vector2.zero;
            dialogRt.sizeDelta = new Vector2(580f, 540f);
            dialogRt.localScale = Vector3.one;

            Image dialogImg = dialogObj.AddComponent<Image>();
            dialogImg.sprite = GetOrCreateCyberPanelSprite();
            dialogImg.type = Image.Type.Sliced;
            dialogImg.color = Color.white; // #0f141d background with #1e293b border

            // Title: "SYSTEM SETTINGS"
            CreateLabel(dialogObj.transform, "Title_Text", new Vector2(0f, 235f), new Vector2(400f, 34f), "SYSTEM SETTINGS", fontAsset, 22f, TextAlignmentOptions.Center, ColorCrispWhite);

            // Close button (X) in top right
            Button closeXBtn = CreateCyberButton(dialogObj.transform, "Btn_CloseModal", new Vector2(255f, 235f), new Vector2(34f, 34f), "X", 16f, fontAsset);

            // Tabs Button Row: "AUDIO & DISPLAY" and "CONTROLS"
            GameObject tabsRowObj = new GameObject("Tabs_Row", typeof(RectTransform));
            tabsRowObj.transform.SetParent(dialogObj.transform, false);
            RectTransform tabsRowRt = tabsRowObj.GetComponent<RectTransform>();
            tabsRowRt.anchoredPosition = new Vector2(0f, 185f);
            tabsRowRt.sizeDelta = new Vector2(520f, 38f);
            tabsRowRt.localScale = Vector3.one;

            HorizontalLayoutGroup tabsHlg = tabsRowObj.AddComponent<HorizontalLayoutGroup>();
            tabsHlg.spacing = 12f;
            tabsHlg.childAlignment = TextAnchor.MiddleCenter;
            tabsHlg.childControlWidth = false;
            tabsHlg.childControlHeight = false;
            tabsHlg.childForceExpandWidth = false;
            tabsHlg.childForceExpandHeight = false;

            Button tabAudioBtn = CreateCyberButton(tabsRowObj.transform, "Btn_TabAudioDisplay", Vector2.zero, new Vector2(250f, 38f), "AUDIO & DISPLAY", 15f, fontAsset);
            Button tabCtrlBtn = CreateCyberButton(tabsRowObj.transform, "Btn_TabControls", Vector2.zero, new Vector2(250f, 38f), "CONTROLS", 15f, fontAsset);

            // ==================== TAB 1: AUDIO & DISPLAY ====================
            GameObject tabAudioObj = new GameObject("TabContent_AudioDisplay", typeof(RectTransform));
            tabAudioObj.transform.SetParent(dialogObj.transform, false);
            RectTransform taoRt = tabAudioObj.GetComponent<RectTransform>();
            taoRt.anchoredPosition = new Vector2(0f, -12f);
            taoRt.sizeDelta = new Vector2(520f, 320f);
            taoRt.localScale = Vector3.one;

            VerticalLayoutGroup taoVlg = tabAudioObj.AddComponent<VerticalLayoutGroup>();
            taoVlg.spacing = 10f;
            taoVlg.childAlignment = TextAnchor.UpperCenter;
            taoVlg.childControlWidth = false;
            taoVlg.childControlHeight = false;
            taoVlg.childForceExpandWidth = false;
            taoVlg.childForceExpandHeight = false;

            // Row 1: Master Volume
            GameObject rowVolObj = new GameObject("Row_Volume", typeof(RectTransform));
            rowVolObj.transform.SetParent(tabAudioObj.transform, false);
            RectTransform rvRt = rowVolObj.GetComponent<RectTransform>();
            rvRt.sizeDelta = new Vector2(520f, 52f);
            rvRt.localScale = Vector3.one;
            Image rvImg = rowVolObj.AddComponent<Image>();
            rvImg.sprite = GetOrCreateCyberCardRowSprite();
            rvImg.type = Image.Type.Sliced;
            rvImg.color = Color.white;

            CreateLabel(rowVolObj.transform, "Label_Volume", new Vector2(-155f, 0f), new Vector2(160f, 30f), "MASTER VOLUME", fontAsset, 14f, TextAlignmentOptions.Left, ColorCrispWhite);
            Slider volSlider = CreateFlatSlider(rowVolObj.transform, "Slider_Volume", new Vector2(10f, 0f), new Vector2(160f, 20f), 0f, 1f, sm != null ? sm.volume : 1f);
            TextMeshProUGUI volValTmp = CreateLabel(rowVolObj.transform, "Value_Volume", new Vector2(125f, 0f), new Vector2(50f, 30f), "100%", fontAsset, 14f, TextAlignmentOptions.Center, ColorElectricCyan);
            Button muteBtn = CreateCyberButton(rowVolObj.transform, "Btn_ToggleMute", new Vector2(205f, 0f), new Vector2(80f, 32f), "MUTE", 13f, fontAsset);

            // Row 2: Display Brightness
            GameObject rowBrightObj = new GameObject("Row_Brightness", typeof(RectTransform));
            rowBrightObj.transform.SetParent(tabAudioObj.transform, false);
            RectTransform rbRt = rowBrightObj.GetComponent<RectTransform>();
            rbRt.sizeDelta = new Vector2(520f, 52f);
            rbRt.localScale = Vector3.one;
            Image rbImg = rowBrightObj.AddComponent<Image>();
            rbImg.sprite = GetOrCreateCyberCardRowSprite();
            rbImg.type = Image.Type.Sliced;
            rbImg.color = Color.white;

            CreateLabel(rowBrightObj.transform, "Label_Brightness", new Vector2(-155f, 0f), new Vector2(160f, 30f), "BRIGHTNESS", fontAsset, 14f, TextAlignmentOptions.Left, ColorCrispWhite);
            Slider brightSlider = CreateFlatSlider(rowBrightObj.transform, "Slider_Brightness", new Vector2(30f, 0f), new Vector2(210f, 20f), 0f, 2f, sm != null ? sm.brightness : 1f);
            TextMeshProUGUI brightValTmp = CreateLabel(rowBrightObj.transform, "Value_Brightness", new Vector2(195f, 0f), new Vector2(65f, 30f), "100%", fontAsset, 14f, TextAlignmentOptions.Center, ColorElectricCyan);

            // Row 3: Display Contrast
            GameObject rowContrastObj = new GameObject("Row_Contrast", typeof(RectTransform));
            rowContrastObj.transform.SetParent(tabAudioObj.transform, false);
            RectTransform rcRt = rowContrastObj.GetComponent<RectTransform>();
            rcRt.sizeDelta = new Vector2(520f, 52f);
            rcRt.localScale = Vector3.one;
            Image rcImg = rowContrastObj.AddComponent<Image>();
            rcImg.sprite = GetOrCreateCyberCardRowSprite();
            rcImg.type = Image.Type.Sliced;
            rcImg.color = Color.white;

            CreateLabel(rowContrastObj.transform, "Label_Contrast", new Vector2(-155f, 0f), new Vector2(160f, 30f), "CONTRAST", fontAsset, 14f, TextAlignmentOptions.Left, ColorCrispWhite);
            Slider contrastSlider = CreateFlatSlider(rowContrastObj.transform, "Slider_Contrast", new Vector2(30f, 0f), new Vector2(210f, 20f), 0.5f, 1.5f, sm != null ? sm.contrast : 1f);
            TextMeshProUGUI contrastValTmp = CreateLabel(rowContrastObj.transform, "Value_Contrast", new Vector2(195f, 0f), new Vector2(65f, 30f), "100%", fontAsset, 14f, TextAlignmentOptions.Center, ColorElectricCyan);

            // Row 4: Graphics Quality
            GameObject rowQualityObj = new GameObject("Row_Quality", typeof(RectTransform));
            rowQualityObj.transform.SetParent(tabAudioObj.transform, false);
            RectTransform rqRt = rowQualityObj.GetComponent<RectTransform>();
            rqRt.sizeDelta = new Vector2(520f, 52f);
            rqRt.localScale = Vector3.one;
            Image rqImg = rowQualityObj.AddComponent<Image>();
            rqImg.sprite = GetOrCreateCyberCardRowSprite();
            rqImg.type = Image.Type.Sliced;
            rqImg.color = Color.white;

            CreateLabel(rowQualityObj.transform, "Label_Quality", new Vector2(-155f, 0f), new Vector2(160f, 30f), "GRAPHICS QUALITY", fontAsset, 14f, TextAlignmentOptions.Left, ColorCrispWhite);
            Button qLowBtn = CreateCyberButton(rowQualityObj.transform, "Btn_QualityLow", new Vector2(40f, 0f), new Vector2(68f, 32f), "LOW", 13f, fontAsset);
            Button qMedBtn = CreateCyberButton(rowQualityObj.transform, "Btn_QualityMed", new Vector2(118f, 0f), new Vector2(68f, 32f), "MED", 13f, fontAsset);
            Button qHighBtn = CreateCyberButton(rowQualityObj.transform, "Btn_QualityHigh", new Vector2(196f, 0f), new Vector2(68f, 32f), "HIGH", 13f, fontAsset);

            // ==================== TAB 2: CONTROLS ====================
            GameObject tabCtrlObj = new GameObject("TabContent_Controls", typeof(RectTransform));
            tabCtrlObj.transform.SetParent(dialogObj.transform, false);
            RectTransform tcoRt = tabCtrlObj.GetComponent<RectTransform>();
            tcoRt.anchoredPosition = new Vector2(0f, -12f);
            tcoRt.sizeDelta = new Vector2(520f, 320f);
            tcoRt.localScale = Vector3.one;

            VerticalLayoutGroup tcoVlg = tabCtrlObj.AddComponent<VerticalLayoutGroup>();
            tcoVlg.spacing = 10f;
            tcoVlg.childAlignment = TextAnchor.UpperCenter;
            tcoVlg.childControlWidth = false;
            tcoVlg.childControlHeight = false;
            tcoVlg.childForceExpandWidth = false;
            tcoVlg.childForceExpandHeight = false;

            // Row 1: Movement Keys
            GameObject rowMoveObj = new GameObject("Row_Movement", typeof(RectTransform));
            rowMoveObj.transform.SetParent(tabCtrlObj.transform, false);
            RectTransform rmovRt = rowMoveObj.GetComponent<RectTransform>();
            rmovRt.sizeDelta = new Vector2(520f, 52f);
            rmovRt.localScale = Vector3.one;
            Image rmovImg = rowMoveObj.AddComponent<Image>();
            rmovImg.sprite = GetOrCreateCyberCardRowSprite();
            rmovImg.type = Image.Type.Sliced;
            rmovImg.color = Color.white;

            CreateLabel(rowMoveObj.transform, "Label_Movement", new Vector2(-155f, 0f), new Vector2(160f, 30f), "MOVEMENT KEYS", fontAsset, 14f, TextAlignmentOptions.Left, ColorCrispWhite);
            Button wasdBtn = CreateCyberButton(rowMoveObj.transform, "Btn_MoveWASD", new Vector2(30f, 0f), new Vector2(72f, 32f), "WASD", 13f, fontAsset);
            Button arrowsBtn = CreateCyberButton(rowMoveObj.transform, "Btn_MoveArrows", new Vector2(112f, 0f), new Vector2(76f, 32f), "ARROWS", 13f, fontAsset);
            Button bothBtn = CreateCyberButton(rowMoveObj.transform, "Btn_MoveBoth", new Vector2(196f, 0f), new Vector2(72f, 32f), "BOTH", 13f, fontAsset);

            // Row 2: Primary Action Binding
            GameObject rowPriActObj = new GameObject("Row_PrimaryAction", typeof(RectTransform));
            rowPriActObj.transform.SetParent(tabCtrlObj.transform, false);
            RectTransform rpaRt = rowPriActObj.GetComponent<RectTransform>();
            rpaRt.sizeDelta = new Vector2(520f, 52f);
            rpaRt.localScale = Vector3.one;
            Image rpaImg = rowPriActObj.AddComponent<Image>();
            rpaImg.sprite = GetOrCreateCyberCardRowSprite();
            rpaImg.type = Image.Type.Sliced;
            rpaImg.color = Color.white;

            CreateLabel(rowPriActObj.transform, "Label_PrimaryAct", new Vector2(-155f, 0f), new Vector2(160f, 30f), "PRIMARY FIRE", fontAsset, 14f, TextAlignmentOptions.Left, ColorCrispWhite);
            Button priMouseBtn = CreateCyberButton(rowPriActObj.transform, "Btn_PrimaryMouse", new Vector2(120f, 0f), new Vector2(210f, 32f), "PRIMARY: LEFT CLICK", 13f, fontAsset);

            // Row 3: Secondary Action Binding
            GameObject rowSecActObj = new GameObject("Row_SecondaryAction", typeof(RectTransform));
            rowSecActObj.transform.SetParent(tabCtrlObj.transform, false);
            RectTransform rsaRt = rowSecActObj.GetComponent<RectTransform>();
            rsaRt.sizeDelta = new Vector2(520f, 52f);
            rsaRt.localScale = Vector3.one;
            Image rsaImg = rowSecActObj.AddComponent<Image>();
            rsaImg.sprite = GetOrCreateCyberCardRowSprite();
            rsaImg.type = Image.Type.Sliced;
            rsaImg.color = Color.white;

            CreateLabel(rowSecActObj.transform, "Label_SecondaryAct", new Vector2(-155f, 0f), new Vector2(160f, 30f), "NEURAL TETHER", fontAsset, 14f, TextAlignmentOptions.Left, ColorCrispWhite);
            Button secMouseBtn = CreateCyberButton(rowSecActObj.transform, "Btn_SecondaryMouse", new Vector2(120f, 0f), new Vector2(210f, 32f), "SECONDARY: RIGHT CLICK", 13f, fontAsset);

            // Row 4: Keybind Preview Overview
            GameObject rowPreviewObj = new GameObject("Row_KeybindPreview", typeof(RectTransform));
            rowPreviewObj.transform.SetParent(tabCtrlObj.transform, false);
            RectTransform rprevRt = rowPreviewObj.GetComponent<RectTransform>();
            rprevRt.sizeDelta = new Vector2(520f, 68f);
            rprevRt.localScale = Vector3.one;
            Image rprevImg = rowPreviewObj.AddComponent<Image>();
            rprevImg.sprite = GetOrCreateCyberCardRowSprite();
            rprevImg.type = Image.Type.Sliced;
            rprevImg.color = Color.white;

            TextMeshProUGUI keybindPreviewTmp = CreateLabel(rowPreviewObj.transform, "KeybindPreview_Text", Vector2.zero, new Vector2(500f, 60f), "", fontAsset, 12f, TextAlignmentOptions.Center, ColorCrispWhite);

            tabCtrlObj.SetActive(false); // Tab 1 active by default

            // ==================== BOTTOM ACTION ROW ====================
            GameObject btmRowObj = new GameObject("BottomActionRow", typeof(RectTransform));
            btmRowObj.transform.SetParent(dialogObj.transform, false);
            RectTransform btmRt = btmRowObj.GetComponent<RectTransform>();
            btmRt.anchoredPosition = new Vector2(0f, -225f);
            btmRt.sizeDelta = new Vector2(520f, 44f);
            btmRt.localScale = Vector3.one;

            HorizontalLayoutGroup btmHlg = btmRowObj.AddComponent<HorizontalLayoutGroup>();
            btmHlg.spacing = 16f;
            btmHlg.childAlignment = TextAnchor.MiddleCenter;
            btmHlg.childControlWidth = false;
            btmHlg.childControlHeight = false;
            btmHlg.childForceExpandWidth = false;
            btmHlg.childForceExpandHeight = false;

            Button backBtn = CreateCyberButton(btmRowObj.transform, "Btn_ApplyClose", Vector2.zero, new Vector2(280f, 42f), "BACK", 16f, fontAsset);
            Button resetBtn = CreateGlitchCrimsonButton(btmRowObj.transform, "Btn_ResetDefaults", Vector2.zero, new Vector2(180f, 42f), "RESET DEFAULTS", 14f, fontAsset);

            // Wire to SettingsManager
            if (sm != null)
            {
                sm.settingsPanel = settingsPanelObj;
                sm.tabDisplayAudioObj = tabAudioObj;
                sm.tabControlsObj = tabCtrlObj;
                sm.tabDisplayAudioBtn = tabAudioBtn;
                sm.tabControlsBtn = tabCtrlBtn;
                sm.tabDisplayAudioImg = tabAudioBtn.GetComponent<Image>();
                sm.tabControlsImg = tabCtrlBtn.GetComponent<Image>();
                sm.tabDisplayAudioText = tabAudioBtn.GetComponentInChildren<TextMeshProUGUI>();
                sm.tabControlsText = tabCtrlBtn.GetComponentInChildren<TextMeshProUGUI>();

                sm.volumeSlider = volSlider;
                sm.volumeValueText = volValTmp;
                sm.muteButton = muteBtn;
                sm.muteButtonText = muteBtn.GetComponentInChildren<TextMeshProUGUI>();

                sm.brightnessSlider = brightSlider;
                sm.brightnessValueText = brightValTmp;

                sm.contrastSlider = contrastSlider;
                sm.contrastValueText = contrastValTmp;

                sm.qualityLowBtn = qLowBtn;
                sm.qualityMedBtn = qMedBtn;
                sm.qualityHighBtn = qHighBtn;
                sm.qualityLowImg = qLowBtn.GetComponent<Image>();
                sm.qualityMedImg = qMedBtn.GetComponent<Image>();
                sm.qualityHighImg = qHighBtn.GetComponent<Image>();

                sm.moveWasdBtn = wasdBtn;
                sm.moveArrowsBtn = arrowsBtn;
                sm.moveBothBtn = bothBtn;
                sm.moveWasdImg = wasdBtn.GetComponent<Image>();
                sm.moveArrowsImg = arrowsBtn.GetComponent<Image>();
                sm.moveBothImg = bothBtn.GetComponent<Image>();

                sm.mousePrimaryBtn = priMouseBtn;
                sm.mousePrimaryText = priMouseBtn.GetComponentInChildren<TextMeshProUGUI>();
                sm.mouseSecondaryBtn = secMouseBtn;
                sm.mouseSecondaryText = secMouseBtn.GetComponentInChildren<TextMeshProUGUI>();

                sm.keybindPreviewText = keybindPreviewTmp;
                sm.backButton = backBtn;
                sm.resetDefaultsButton = resetBtn;

                sm.BindUIEvents();
                sm.SwitchTab(0);
                sm.UpdateUIFromSettings();
            }

            // Wire to UIManager
            if (uiMgr != null)
            {
                uiMgr.settingsModalPanel = settingsPanelObj;
                uiMgr.masterVolumeSlider = volSlider;
                uiMgr.masterVolumeValueText = volValTmp;
                uiMgr.applyCloseBtn = backBtn;
                uiMgr.modalCloseXBtn = closeXBtn;
                uiMgr.BindUIEvents();
            }

            settingsPanelObj.SetActive(false);
            return settingsPanelObj;
        }

        private static Sprite LoadSprite(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                return null;
            }

            Sprite direct = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            if (direct != null)
            {
                return direct;
            }

            Object[] allAssets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            foreach (Object obj in allAssets)
            {
                if (obj is Sprite s)
                {
                    return s;
                }
            }

            return null;
        }

        private static Sprite FindFirstSprite(string folderPath)
        {
            string[] guids = AssetDatabase.FindAssets("t:Sprite", new[] { folderPath });
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                Sprite s = LoadSprite(path);
                if (s != null)
                {
                    return s;
                }
            }

            return null;
        }

        private static void EnsureCrispUIQuality(Canvas canvas)
        {
            if (canvas == null) return;

            QualitySettings.resolutionScalingFixedDPIFactor = 1.0f;
            QualitySettings.pixelLightCount = 4;

            CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
            if (scaler != null)
            {
                scaler.dynamicPixelsPerUnit = 10.0f;
            }

            TMP_FontAsset fontAsset = GetOrCreateDefaultTMPFont();

            // 0. Zero ContentSizeFitter Enforcement Across Canvas Hierarchy:
            // Absolutely DO NOT allow ContentSizeFitter anywhere under UI_Canvas.
            // If an existing object contains a ContentSizeFitter, explicitly destroy it.
            ContentSizeFitter[] allCsfs = canvas.GetComponentsInChildren<ContentSizeFitter>(true);
            foreach (ContentSizeFitter fitter in allCsfs)
            {
                if (fitter != null)
                {
                    Object.DestroyImmediate(fitter);
                }
            }

            // Enforce Modal Panels: Fullscreen anchors (0, 0 to 1, 1)
            string[] modalPanelNames = new string[] { "StartPanel", "StartMenuPanel", "GameOverPanel", "VictoryPanel", "PausePanel", "SettingsPanel", "SettingsModalPanel" };
            foreach (string panelName in modalPanelNames)
            {
                Transform pt = canvas.transform.Find(panelName);
                if (pt != null && pt is RectTransform prt)
                {
                    prt.anchorMin = Vector2.zero;
                    prt.anchorMax = Vector2.one;
                    prt.offsetMin = Vector2.zero;
                    prt.offsetMax = Vector2.zero;
                    prt.localScale = Vector3.one;
                }
            }

            // 1. Replace any legacy UnityEngine.UI.Text components across Canvas with TMPro.TextMeshProUGUI
            Text[] legacyTexts = canvas.GetComponentsInChildren<Text>(true);
            foreach (Text legacy in legacyTexts)
            {
                GameObject go = legacy.gameObject;
                string textContent = legacy.text;
                Color col = legacy.color;
                int fs = legacy.fontSize > 0 ? legacy.fontSize : 22;
                Object.DestroyImmediate(legacy);

                TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
                tmp.text = textContent;
                tmp.color = col;
                ConfigureTMPText(tmp, fontAsset, fontSize: fs, alignment: TextAlignmentOptions.Center);
            }

            // 2. Configure all TextMeshProUGUI elements: strict scale = 1, NO auto-sizing, assigned font, no word wrapping, overflow
            TextMeshProUGUI[] tmps = canvas.GetComponentsInChildren<TextMeshProUGUI>(true);
            foreach (TextMeshProUGUI tmp in tmps)
            {
                tmp.rectTransform.localScale = Vector3.one;
                if (tmp.transform.parent != null && tmp.transform.parent is RectTransform parentRect)
                {
                    parentRect.localScale = Vector3.one;
                }

                if (fontAsset != null)
                {
                    tmp.font = fontAsset;
                }
                tmp.fontStyle = FontStyles.Bold;

                tmp.textWrappingMode = TextWrappingModes.NoWrap;
                tmp.overflowMode = TextOverflowModes.Overflow;
                tmp.enableAutoSizing = false;

                // Set rectTransform.sizeDelta equal to or larger than parent container dimensions only if parent is a Button
                if (tmp.transform.parent != null && tmp.transform.parent is RectTransform pRect)
                {
                    if (pRect.GetComponent<Button>() != null)
                    {
                        Vector2 pSize = pRect.rect.size;
                        if (pSize.x > 0f && pSize.y > 0f)
                        {
                            Vector2 curDelta = tmp.rectTransform.sizeDelta;
                            tmp.rectTransform.sizeDelta = new Vector2(
                                Mathf.Max(curDelta.x, pSize.x),
                                Mathf.Max(curDelta.y, pSize.y)
                            );
                        }
                    }
                }

                string goName = tmp.gameObject.name;
                Transform p = tmp.transform.parent;
                bool isButton = (p != null && p.GetComponent<Button>() != null) || goName.StartsWith("Btn_");
                bool isStartBtn = goName == "Btn_Start" || goName == "Btn_StartMission" || (p != null && (p.name == "Btn_Start" || p.name == "Btn_StartMission"));
                bool isTitle = goName.Contains("Title") || goName.Contains("Header");

                if (goName == "Badge_Version")
                {
                    tmp.fontSize = 15f;
                }
                else if (goName == "Tagline_Text")
                {
                    tmp.fontSize = 16f;
                    tmp.characterSpacing = 28f;
                }
                else if (goName == "Title_Main" || goName == "Title_Glow")
                {
                    tmp.fontSize = 68f;
                    tmp.characterSpacing = 4f;
                }
                else if (goName == "Subtitle_Text")
                {
                    tmp.fontSize = 20f;
                    tmp.characterSpacing = 8f;
                }
                else if (goName == "Title_Text_1" || goName == "Title_Text_2")
                {
                    tmp.fontSize = 72f;
                }
                else if (goName.StartsWith("Footer_"))
                {
                    tmp.fontSize = 13f;
                }
                else if (goName == "Btn_AudioQuickToggle" || (p != null && p.name == "Btn_AudioQuickToggle"))
                {
                    tmp.fontSize = 14f;
                }
                else if (goName == "Btn_EyeFilter" || (p != null && p.name == "Btn_EyeFilter"))
                {
                    tmp.fontSize = 13f;
                }
                else if (goName == "Btn_ApplyClose" || (p != null && p.name == "Btn_ApplyClose"))
                {
                    tmp.fontSize = 18f;
                }
                else if (goName == "Btn_CloseModal" || (p != null && p.name == "Btn_CloseModal"))
                {
                    tmp.fontSize = 16f;
                }
                else if (goName == "Btn_NextLevel" || (p != null && p.name == "Btn_NextLevel"))
                {
                    tmp.fontSize = 20f;
                }
                else if (goName == "Btn_Replay" || (p != null && p.name == "Btn_Replay") || goName == "Btn_VictoryMenu" || (p != null && p.name == "Btn_VictoryMenu"))
                {
                    tmp.fontSize = 18f;
                }
                else if (p != null && p.name == "PausePanel" && isTitle)
                {
                    tmp.fontSize = 46f;
                }
                else if (p != null && p.name == "VictoryPanel" && isTitle)
                {
                    tmp.fontSize = 48f;
                }
                else if (p != null && p.parent != null && p.parent.name == "PausePanel" && isButton)
                {
                    tmp.fontSize = 20f;
                }
                else if (isStartBtn)
                {
                    tmp.fontSize = 24f;
                }
                else if (isTitle)
                {
                    tmp.fontSize = 56f;
                }
                else if (goName == "Timer_Text")
                {
                    tmp.fontSize = 34f;
                }
                else if (goName == "Score_Text" || goName.Contains("Score"))
                {
                    tmp.fontSize = 24f;
                }
                else if (goName == "HP_Text")
                {
                    tmp.fontSize = 18f;
                }
                else if (isButton)
                {
                    tmp.fontSize = 18f;
                }
                else if (goName.Contains("HP") || goName.Contains("Subtitle") || goName.Contains("Stats"))
                {
                    tmp.fontSize = (p != null && p.name == "VictoryPanel") ? 22f : 20f;
                }
                else if (goName.Contains("Hijack") || goName.Contains("Multiplier"))
                {
                    tmp.fontSize = 20f;
                }
                else
                {
                    if (tmp.fontSize <= 0f || tmp.fontSize > 30f)
                    {
                        tmp.fontSize = 18f;
                    }
                }

                tmp.raycastTarget = false;
            }

            // 3. Ensure all RectTransforms in Canvas have strictly localScale = Vector3.one
            RectTransform[] allRects = canvas.GetComponentsInChildren<RectTransform>(true);
            foreach (RectTransform rt in allRects)
            {
                rt.localScale = Vector3.one;
            }

            // 4. Destroy any pixelated borders, scanline filters, or custom sprite sheets from UI elements
            Image[] images = canvas.GetComponentsInChildren<Image>(true);
            Sprite whiteSp = HealthBarUI.GetOrCreateWhiteSprite();
            foreach (Image img in images)
            {
                if (img == null || img.gameObject == null) continue;

                string name = img.gameObject.name.ToLowerInvariant();
                if (name.Contains("scanline") || name.Contains("pixelborder") || name.Contains("overlay_filter") || name.Contains("crt"))
                {
                    Object.DestroyImmediate(img.gameObject);
                    continue;
                }

                // Ensure all UI images use clean solid flat sprite rather than Kenney pixelated sprite sheets
                if (img.sprite == null || (img.sprite != whiteSp && img.sprite.name.StartsWith("tile_")))
                {
                    img.sprite = whiteSp;
                }
            }
        }
    }
}
#endif
// Minimalist reset ready
