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
            if (!SessionState.GetBool("FastGameSetup_UIRebuild_Done_v18", false))
            {
                SessionState.SetBool("FastGameSetup_UIRebuild_Done_v18", true);
                SetupSceneAndSprites();
            }
        }

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
            ColorUtility.TryParseHtmlString("#161922", out Color slateNavy);
            cam.backgroundColor = slateNavy;
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

        // ==================== MODERN CRISP VECTOR UI PALETTE & CONFIG ====================
        private static readonly Color ColorMainPanelBg = new Color(15f / 255f, 23f / 255f, 42f / 255f, 0.95f); // #0F172A (95% opacity)
        private static readonly Color ColorVictoryPanelBg = new Color(0.04f, 0.12f, 0.08f, 0.95f);
        private static readonly Color ColorPausePanelBg = new Color(0.06f, 0.08f, 0.12f, 0.92f);
        private static readonly Color ColorPauseTitleGold = new Color(1f, 0.85f, 0.1f, 1f);
        private static readonly Color ColorButtonCharcoal = new Color(30f / 255f, 41f / 255f, 59f / 255f, 1f); // #1E293B
        private static readonly Color ColorButtonHoverHighlight = new Color(56f / 255f, 189f / 255f, 248f / 255f, 1f); // #38BDF8 Sky Blue hover
        private static readonly Color ColorButtonActiveSelected = new Color(6f / 255f, 182f / 255f, 212f / 255f, 1f); // #06B6D4 Electric Cyan
        private static readonly Color ColorStartBtnEmerald = new Color(16f / 255f, 185f / 255f, 129f / 255f, 1f); // #10B981 Vivid Emerald Green
        private static readonly Color ColorStartBtnHover = new Color(52f / 255f, 211f / 255f, 153f / 255f, 1f); // #34D399 Bright Emerald Hover
        private static readonly Color ColorHealthBarBg = new Color(30f / 255f, 41f / 255f, 59f / 255f, 1f); // #1E293B Solid Dark Slate
        private static readonly Color ColorHealthBarFill = new Color(34f / 255f, 197f / 255f, 94f / 255f, 1f); // #22C55E Vivid Lime Green
        private static readonly Color ColorTimerGold = new Color(251f / 255f, 191f / 255f, 36f / 255f, 1f); // #FBBF24 Bright Gold
        private static readonly Color ColorElectricCyan = new Color(6f / 255f, 182f / 255f, 212f / 255f, 1f); // #06B6D4 Electric Cyan
        private static readonly Color ColorSlateMuted = new Color(148f / 255f, 163f / 255f, 184f / 255f, 1f); // #94A3B8 Slate Gray
        private static readonly Color ColorCriticalRed = new Color(239f / 255f, 68f / 255f, 68f / 255f, 1f); // #EF4444 Crimson

        private static TMP_FontAsset GetOrCreateDefaultTMPFont()
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
            Color? color = null)
        {
            if (tmp == null) return;
            if (fontAsset != null) tmp.font = fontAsset;

            ContentSizeFitter csf = tmp.GetComponent<ContentSizeFitter>();
            if (csf != null)
            {
                Object.DestroyImmediate(csf);
            }

            tmp.enableWordWrapping = false;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.enableAutoSizing = false;
            tmp.fontSize = fontSize;
            tmp.alignment = alignment;
            if (color.HasValue) tmp.color = color.Value;
            tmp.fontStyle = FontStyles.Bold;
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

            // --- 1. Mini-Map Root (Top-Left): Pos: (20, -20), Size: (180, 180), Pivot: (0, 1), Anchors: (0, 1, 0, 1) ---
            GameObject miniMapRootObj = new GameObject("MiniMap_Root", typeof(RectTransform));
            miniMapRootObj.transform.SetParent(hudPanelObj.transform, false);
            RectTransform mmRootRect = miniMapRootObj.GetComponent<RectTransform>();
            mmRootRect.anchorMin = new Vector2(0f, 1f);
            mmRootRect.anchorMax = new Vector2(0f, 1f);
            mmRootRect.pivot = new Vector2(0f, 1f);
            mmRootRect.anchoredPosition = new Vector2(20f, -20f);
            mmRootRect.sizeDelta = new Vector2(180f, 180f);
            mmRootRect.localScale = Vector3.one;

            // Background Image: Solid Slate #0A0F1D circular panel with 2px Cyan outline #06B6D4
            Image mmBg = miniMapRootObj.AddComponent<Image>();
            mmBg.sprite = MiniMapController.GetOrCreateRadarBackgroundSprite();
            mmBg.type = Image.Type.Simple;
            mmBg.color = Color.white;

            // Blips Container: RectTransform (Stretch fill)
            GameObject blipsContainerObj = new GameObject("MiniMap_BlipsContainer", typeof(RectTransform));
            blipsContainerObj.transform.SetParent(miniMapRootObj.transform, false);
            RectTransform blipsRect = blipsContainerObj.GetComponent<RectTransform>();
            blipsRect.anchorMin = Vector2.zero;
            blipsRect.anchorMax = Vector2.one;
            blipsRect.pivot = new Vector2(0.5f, 0.5f);
            blipsRect.offsetMin = Vector2.zero;
            blipsRect.offsetMax = Vector2.zero;
            blipsRect.localScale = Vector3.one;

            // Attach and configure MiniMapController
            MiniMapController mmCtrl = miniMapRootObj.AddComponent<MiniMapController>();
            mmCtrl.blipsContainer = blipsRect;
            if (playerObj != null)
            {
                mmCtrl.playerTransform = playerObj.transform;
            }

            // --- 2. Health Bar (Directly below Mini-Map): Pos: 20, -210; Size: 280, 36; Pivot: (0, 1), Anchors: (0, 1, 0, 1) ---
            GameObject hbRootObj = new GameObject("HealthBar_Root", typeof(RectTransform));
            hbRootObj.transform.SetParent(hudPanelObj.transform, false);
            RectTransform hbRootRect = hbRootObj.GetComponent<RectTransform>();
            hbRootRect.anchorMin = new Vector2(0f, 1f);
            hbRootRect.anchorMax = new Vector2(0f, 1f);
            hbRootRect.pivot = new Vector2(0f, 1f);
            hbRootRect.anchoredPosition = new Vector2(20f, -210f);
            hbRootRect.sizeDelta = new Vector2(280f, 36f);
            hbRootRect.localScale = Vector3.one;

            // Background Image: Solid Dark Slate #1E293B
            Image hbBg = hbRootObj.AddComponent<Image>();
            hbBg.sprite = HealthBarUI.GetOrCreateWhiteSprite();
            hbBg.type = Image.Type.Simple;
            ColorUtility.TryParseHtmlString("#1E293B", out Color darkSlateHbBg);
            hbBg.color = darkSlateHbBg != default ? darkSlateHbBg : ColorHealthBarBg;

            // Child "FillImage": Stretch fill (0, 0 to 1, 1). Filled horizontal. Green #22C55E
            GameObject fillObj = new GameObject("FillImage", typeof(RectTransform));
            fillObj.transform.SetParent(hbRootObj.transform, false);
            RectTransform fillRect = fillObj.GetComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
            fillRect.localScale = Vector3.one;

            Image fillImg = fillObj.AddComponent<Image>();
            fillImg.sprite = HealthBarUI.GetOrCreateWhiteSprite();
            fillImg.type = Image.Type.Filled;
            fillImg.fillMethod = Image.FillMethod.Horizontal;
            fillImg.fillOrigin = (int)Image.OriginHorizontal.Left;
            fillImg.fillAmount = 1f;
            ColorUtility.TryParseHtmlString("#22C55E", out Color greenFill);
            fillImg.color = greenFill != default ? greenFill : ColorHealthBarFill;

            // Child "HP_Text": Stretch fill. Auto-sizing min 16 max 20, center bold white
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
            ConfigureTMPText(hpTmp, fontAsset, fontSize: 18f, alignment: TextAlignmentOptions.Center, color: Color.white);

            HealthBarUI healthUI = hbRootObj.AddComponent<HealthBarUI>();
            healthUI.backgroundImage = hbBg;
            healthUI.fillImage = fillImg;
            healthUI.hpText = hpTmp;
            healthUI.hpPrefix = "HP: ";
            if (playerObj != null)
            {
                healthUI.player = playerObj.GetComponent<PlayerController>();
            }

            // --- 3. Neural Hijack Indicator (Below Health Bar): Pos: (20, -255), Size: (350, 30), Pivot: (0, 1), Anchors: (0, 1, 0, 1) ---
            GameObject hijackObj = new GameObject("Hijack_Text", typeof(RectTransform));
            hijackObj.transform.SetParent(hudPanelObj.transform, false);
            RectTransform hijackRect = hijackObj.GetComponent<RectTransform>();
            hijackRect.anchorMin = new Vector2(0f, 1f);
            hijackRect.anchorMax = new Vector2(0f, 1f);
            hijackRect.pivot = new Vector2(0f, 1f);
            hijackRect.anchoredPosition = new Vector2(20f, -255f);
            hijackRect.sizeDelta = new Vector2(350f, 30f);
            hijackRect.localScale = Vector3.one;

            TextMeshProUGUI hijackTmp = hijackObj.AddComponent<TextMeshProUGUI>();
            hijackTmp.text = "HIJACK: [READY] (R-CLICK)";
            ColorUtility.TryParseHtmlString("#06B6D4", out Color cyanHijack);
            ConfigureTMPText(hijackTmp, fontAsset, fontSize: 20f, alignment: TextAlignmentOptions.Left, color: cyanHijack != default ? cyanHijack : ColorElectricCyan);

            if (playerObj != null)
            {
                NeuralTether tether = playerObj.GetComponent<NeuralTether>();
                if (tether != null)
                {
                    tether.statusText = hijackTmp;
                }
            }

            // --- 4. Timer (Top-Center): Pos: (0, -40), Size: (400, 60), Pivot: (0.5, 1), Anchors: (0.5, 1, 0.5, 1) ---
            GameObject timerObj = new GameObject("Timer_Text", typeof(RectTransform));
            timerObj.transform.SetParent(hudPanelObj.transform, false);
            RectTransform timerRect = timerObj.GetComponent<RectTransform>();
            timerRect.anchorMin = new Vector2(0.5f, 1f);
            timerRect.anchorMax = new Vector2(0.5f, 1f);
            timerRect.pivot = new Vector2(0.5f, 1f);
            timerRect.anchoredPosition = new Vector2(0f, -40f);
            timerRect.sizeDelta = new Vector2(400f, 60f);
            timerRect.localScale = Vector3.one;

            TextMeshProUGUI timerTmp = timerObj.AddComponent<TextMeshProUGUI>();
            timerTmp.text = "SURVIVE: 20s";
            ConfigureTMPText(timerTmp, fontAsset, fontSize: 48f, alignment: TextAlignmentOptions.Center, color: Color.white);

            // --- 5. Score & Multiplier (Top-Right): Pos: (-40, -40), Size: (320, 50), Pivot: (1, 1), Anchors: (1, 1, 1, 1) ---
            GameObject scoreObj = new GameObject("Score_Text", typeof(RectTransform));
            scoreObj.transform.SetParent(hudPanelObj.transform, false);
            RectTransform scoreRect = scoreObj.GetComponent<RectTransform>();
            scoreRect.anchorMin = new Vector2(1f, 1f);
            scoreRect.anchorMax = new Vector2(1f, 1f);
            scoreRect.pivot = new Vector2(1f, 1f);
            scoreRect.anchoredPosition = new Vector2(-40f, -40f);
            scoreRect.sizeDelta = new Vector2(320f, 50f);
            scoreRect.localScale = Vector3.one;

            TextMeshProUGUI scoreTmp = scoreObj.AddComponent<TextMeshProUGUI>();
            scoreTmp.text = "SCORE: 0";
            ConfigureTMPText(scoreTmp, fontAsset, fontSize: 32f, alignment: TextAlignmentOptions.Right, color: Color.white);

            GameObject multObj = new GameObject("Multiplier_Text", typeof(RectTransform));
            multObj.transform.SetParent(scoreObj.transform, false);
            RectTransform multRect = multObj.GetComponent<RectTransform>();
            multRect.anchorMin = new Vector2(1f, 1f);
            multRect.anchorMax = new Vector2(1f, 1f);
            multRect.pivot = new Vector2(1f, 1f);
            multRect.anchoredPosition = new Vector2(0f, -36f);
            multRect.sizeDelta = new Vector2(320f, 32f);
            multRect.localScale = Vector3.one;

            TextMeshProUGUI multTmp = multObj.AddComponent<TextMeshProUGUI>();
            multTmp.text = "";
            ConfigureTMPText(multTmp, fontAsset, fontSize: 20f, alignment: TextAlignmentOptions.Right, color: ColorTimerGold);

            // 3. Build StartPanel (Modal Overlay, Dark Navy/Slate #0F172A at 95% opacity)
            GameObject startPanelObj = new GameObject("StartPanel", typeof(RectTransform));
            startPanelObj.transform.SetParent(canvasObj.transform, false);
            RectTransform startRect = startPanelObj.GetComponent<RectTransform>();
            startRect.anchorMin = Vector2.zero;
            startRect.anchorMax = Vector2.one;
            startRect.offsetMin = Vector2.zero;
            startRect.offsetMax = Vector2.zero;
            startRect.localScale = Vector3.one;

            Image startBg = startPanelObj.AddComponent<Image>();
            startBg.sprite = HealthBarUI.GetOrCreateWhiteSprite();
            startBg.type = Image.Type.Simple;
            startBg.color = ColorMainPanelBg;

            // Title Panel: min width = 900, min height = 120
            GameObject titleObj = new GameObject("Title_Text", typeof(RectTransform));
            titleObj.transform.SetParent(startPanelObj.transform, false);
            RectTransform titleRect = titleObj.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0.5f, 0.5f);
            titleRect.anchorMax = new Vector2(0.5f, 0.5f);
            titleRect.pivot = new Vector2(0.5f, 0.5f);
            titleRect.anchoredPosition = new Vector2(0f, 175f);
            titleRect.sizeDelta = new Vector2(900f, 120f);
            titleRect.localScale = Vector3.one;

            TextMeshProUGUI titleTmp = titleObj.AddComponent<TextMeshProUGUI>();
            titleTmp.text = "NEURAL OVERRIDE";
            ConfigureTMPText(titleTmp, fontAsset, fontSize: 56f, alignment: TextAlignmentOptions.Center, color: ColorElectricCyan);

            // Subtitle: (Pos: 0, 100; Size: 800, 45)
            GameObject subObj = new GameObject("Subtitle_Text", typeof(RectTransform));
            subObj.transform.SetParent(startPanelObj.transform, false);
            RectTransform subRect = subObj.GetComponent<RectTransform>();
            subRect.anchorMin = new Vector2(0.5f, 0.5f);
            subRect.anchorMax = new Vector2(0.5f, 0.5f);
            subRect.pivot = new Vector2(0.5f, 0.5f);
            subRect.anchoredPosition = new Vector2(0f, 100f);
            subRect.sizeDelta = new Vector2(800f, 45f);
            subRect.localScale = Vector3.one;

            TextMeshProUGUI subTmp = subObj.AddComponent<TextMeshProUGUI>();
            subTmp.text = "SELECT PROTOCOL LEVEL:";
            ConfigureTMPText(subTmp, fontAsset, fontSize: 20f, alignment: TextAlignmentOptions.Center, color: ColorSlateMuted);

            // Difficulty Button Row with HorizontalLayoutGroup to guarantee buttons expand proportionately
            GameObject diffGroupObj = new GameObject("DifficultyButtonGroup", typeof(RectTransform));
            diffGroupObj.transform.SetParent(startPanelObj.transform, false);
            RectTransform diffGroupRect = diffGroupObj.GetComponent<RectTransform>();
            diffGroupRect.anchorMin = new Vector2(0.5f, 0.5f);
            diffGroupRect.anchorMax = new Vector2(0.5f, 0.5f);
            diffGroupRect.pivot = new Vector2(0.5f, 0.5f);
            diffGroupRect.anchoredPosition = new Vector2(0f, 15f);
            diffGroupRect.sizeDelta = new Vector2(850f, 65f);
            diffGroupRect.localScale = Vector3.one;

            HorizontalLayoutGroup hlg = diffGroupObj.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 24f;
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.childControlWidth = false;
            hlg.childControlHeight = false;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;

            ContentSizeFitter existingDiffCsf = diffGroupObj.GetComponent<ContentSizeFitter>();
            if (existingDiffCsf != null) Object.DestroyImmediate(existingDiffCsf);

            // Buttons: Clean Charcoal #1E293B with bold crisp white text and bright hover highlights
            Button easyBtn = CreateFlatButton(diffGroupObj.transform, "Btn_Easy", Vector2.zero, new Vector2(260f, 60f), "EASY (20s)", ColorButtonCharcoal, Color.white, 22f, ColorButtonHoverHighlight, fontAsset);
            Button normalBtn = CreateFlatButton(diffGroupObj.transform, "Btn_Normal", Vector2.zero, new Vector2(260f, 60f), "NORMAL (50s)", ColorButtonCharcoal, Color.white, 22f, ColorButtonHoverHighlight, fontAsset);
            Button expertBtn = CreateFlatButton(diffGroupObj.transform, "Btn_Expert", Vector2.zero, new Vector2(260f, 60f), "EXPERT (90s)", ColorButtonCharcoal, Color.white, 22f, ColorButtonHoverHighlight, fontAsset);

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

            // Start Action Button: Width = 520, Height = 70 (Vivid Emerald Green #10B981)
            Button startBtn = CreateFlatButton(startPanelObj.transform, "Btn_Start", new Vector2(0f, -85f), new Vector2(520f, 70f), "INITIALIZE SYSTEM (SPACE)", ColorStartBtnEmerald, Color.white, 26f, ColorStartBtnHover, fontAsset);
            if (gm != null)
            {
                UnityEventTools.AddPersistentListener(startBtn.onClick, gm.StartGame);
                gm.StartBtnImage = startBtn.GetComponent<Image>();
                gm.StartBtnLabel = startBtn.GetComponentInChildren<TextMeshProUGUI>();
            }

            startPanelObj.SetActive(true);

            // 4. Build GameOver & Victory Panels (Dark Navy/Slate #0F172A at 95% opacity)
            GameObject gameOverPanelObj = new GameObject("GameOverPanel", typeof(RectTransform));
            gameOverPanelObj.transform.SetParent(canvasObj.transform, false);
            RectTransform goRect = gameOverPanelObj.GetComponent<RectTransform>();
            goRect.anchorMin = Vector2.zero;
            goRect.anchorMax = Vector2.one;
            goRect.offsetMin = Vector2.zero;
            goRect.offsetMax = Vector2.zero;
            goRect.localScale = Vector3.one;

            Image goBg = gameOverPanelObj.AddComponent<Image>();
            goBg.sprite = HealthBarUI.GetOrCreateWhiteSprite();
            goBg.type = Image.Type.Simple;
            goBg.color = ColorMainPanelBg;

            // Header: (Pos: 0, 95; Size: 900, 120), fontSize = 56, Bold Red #EF4444
            GameObject goHeaderObj = new GameObject("Header_Text", typeof(RectTransform));
            goHeaderObj.transform.SetParent(gameOverPanelObj.transform, false);
            RectTransform goHeaderRect = goHeaderObj.GetComponent<RectTransform>();
            goHeaderRect.anchorMin = new Vector2(0.5f, 0.5f);
            goHeaderRect.anchorMax = new Vector2(0.5f, 0.5f);
            goHeaderRect.pivot = new Vector2(0.5f, 0.5f);
            goHeaderRect.anchoredPosition = new Vector2(0f, 95f);
            goHeaderRect.sizeDelta = new Vector2(900f, 120f);
            goHeaderRect.localScale = Vector3.one;

            TextMeshProUGUI goHeaderTmp = goHeaderObj.AddComponent<TextMeshProUGUI>();
            goHeaderTmp.text = "CRITICAL FAILURE";
            ConfigureTMPText(goHeaderTmp, fontAsset, fontSize: 56f, alignment: TextAlignmentOptions.Center, color: ColorCriticalRed);

            // Score & Stats text for Game Over
            GameObject goScoreObj = new GameObject("GameOverScore_Text", typeof(RectTransform));
            goScoreObj.transform.SetParent(gameOverPanelObj.transform, false);
            RectTransform goScoreRect = goScoreObj.GetComponent<RectTransform>();
            goScoreRect.anchorMin = new Vector2(0.5f, 0.5f);
            goScoreRect.anchorMax = new Vector2(0.5f, 0.5f);
            goScoreRect.pivot = new Vector2(0.5f, 0.5f);
            goScoreRect.anchoredPosition = new Vector2(0f, 20f);
            goScoreRect.sizeDelta = new Vector2(600f, 45f);
            goScoreRect.localScale = Vector3.one;

            TextMeshProUGUI goScoreTmp = goScoreObj.AddComponent<TextMeshProUGUI>();
            goScoreTmp.text = "SCORE: 0";
            ConfigureTMPText(goScoreTmp, fontAsset, fontSize: 32f, alignment: TextAlignmentOptions.Center, color: Color.white);

            GameObject goStatsObj = new GameObject("GameOverStats_Text", typeof(RectTransform));
            goStatsObj.transform.SetParent(gameOverPanelObj.transform, false);
            RectTransform goStatsRect = goStatsObj.GetComponent<RectTransform>();
            goStatsRect.anchorMin = new Vector2(0.5f, 0.5f);
            goStatsRect.anchorMax = new Vector2(0.5f, 0.5f);
            goStatsRect.pivot = new Vector2(0.5f, 0.5f);
            goStatsRect.anchoredPosition = new Vector2(0f, -25f);
            goStatsRect.sizeDelta = new Vector2(800f, 35f);
            goStatsRect.localScale = Vector3.one;

            TextMeshProUGUI goStatsTmp = goStatsObj.AddComponent<TextMeshProUGUI>();
            goStatsTmp.text = "";
            ConfigureTMPText(goStatsTmp, fontAsset, fontSize: 20f, alignment: TextAlignmentOptions.Center, color: ColorSlateMuted);

            // Restart Button (Standard Button: Width = 520, Height = 60): Text = "REBOOT (R)"
            Button restartBtn = CreateFlatButton(gameOverPanelObj.transform, "Btn_Restart", new Vector2(0f, -85f), new Vector2(520f, 60f), "REBOOT (R)", ColorButtonCharcoal, Color.white, 22f, ColorButtonHoverHighlight, fontAsset);
            if (gm != null)
            {
                UnityEventTools.AddPersistentListener(restartBtn.onClick, gm.RestartGame);
                gm.GameOverScoreText = goScoreTmp;
                gm.GameOverStatsText = goStatsTmp;
            }

            gameOverPanelObj.SetActive(false);

            // 5. Build Victory Panel (Fullscreen semi-transparent backdrop: Color(0.04f, 0.12f, 0.08f, 0.95f))
            GameObject victoryPanelObj = new GameObject("VictoryPanel", typeof(RectTransform));
            victoryPanelObj.transform.SetParent(canvasObj.transform, false);
            RectTransform vicRect = victoryPanelObj.GetComponent<RectTransform>();
            vicRect.anchorMin = Vector2.zero;
            vicRect.anchorMax = Vector2.one;
            vicRect.offsetMin = Vector2.zero;
            vicRect.offsetMax = Vector2.zero;
            vicRect.localScale = Vector3.one;

            Image vicBg = victoryPanelObj.AddComponent<Image>();
            vicBg.sprite = HealthBarUI.GetOrCreateWhiteSprite();
            vicBg.type = Image.Type.Simple;
            vicBg.color = ColorVictoryPanelBg;

            // Title Text: TextMeshProUGUI (Pos: 0, 130; Size: 900, 80), text = "NEURAL PURGE COMPLETE", color = Color.green, fontSize = 48, fontStyle = Bold, alignment = Center.
            GameObject vicTitleObj = new GameObject("Title_Text", typeof(RectTransform));
            vicTitleObj.transform.SetParent(victoryPanelObj.transform, false);
            RectTransform vicTitleRect = vicTitleObj.GetComponent<RectTransform>();
            vicTitleRect.anchorMin = new Vector2(0.5f, 0.5f);
            vicTitleRect.anchorMax = new Vector2(0.5f, 0.5f);
            vicTitleRect.pivot = new Vector2(0.5f, 0.5f);
            vicTitleRect.anchoredPosition = new Vector2(0f, 130f);
            vicTitleRect.sizeDelta = new Vector2(900f, 80f);
            vicTitleRect.localScale = Vector3.one;

            TextMeshProUGUI vicTitleTmp = vicTitleObj.AddComponent<TextMeshProUGUI>();
            vicTitleTmp.text = "NEURAL PURGE COMPLETE";
            ConfigureTMPText(vicTitleTmp, fontAsset, fontSize: 48f, alignment: TextAlignmentOptions.Center, color: Color.green);

            // Sub-text / Stats: TextMeshProUGUI (Pos: 0, 65; Size: 700, 40), text = "SECTOR SECURED", color = Color.white, fontSize = 22, alignment = Center.
            GameObject vicSubObj = new GameObject("Subtitle_Text", typeof(RectTransform));
            vicSubObj.transform.SetParent(victoryPanelObj.transform, false);
            RectTransform vicSubRect = vicSubObj.GetComponent<RectTransform>();
            vicSubRect.anchorMin = new Vector2(0.5f, 0.5f);
            vicSubRect.anchorMax = new Vector2(0.5f, 0.5f);
            vicSubRect.pivot = new Vector2(0.5f, 0.5f);
            vicSubRect.anchoredPosition = new Vector2(0f, 65f);
            vicSubRect.sizeDelta = new Vector2(700f, 40f);
            vicSubRect.localScale = Vector3.one;

            TextMeshProUGUI vicSubTmp = vicSubObj.AddComponent<TextMeshProUGUI>();
            vicSubTmp.text = "SECTOR SECURED";
            ConfigureTMPText(vicSubTmp, fontAsset, fontSize: 22f, alignment: TextAlignmentOptions.Center, color: Color.white);

            // Button 1 - Next Level (Standard Button: Width = 520, Height = 60): "Btn_NextLevel"
            // Background Color: Mint/Emerald (#10B981), Text: "ADVANCE TO NEXT LEVEL" (Bold White, fontSize = 20)
            Button nextLvlBtn = CreateFlatButton(victoryPanelObj.transform, "Btn_NextLevel", new Vector2(0f, 5f), new Vector2(520f, 60f), "ADVANCE TO NEXT LEVEL", ColorStartBtnEmerald, Color.white, 20f, ColorStartBtnHover, fontAsset);
            if (gm != null)
            {
                while (nextLvlBtn.onClick.GetPersistentEventCount() > 0)
                {
                    UnityEventTools.RemovePersistentListener(nextLvlBtn.onClick, 0);
                }
                UnityEventTools.AddPersistentListener(nextLvlBtn.onClick, gm.NextLevel);
            }

            // Button 2 - Replay Sector (Standard Button: Width = 520, Height = 60): "Btn_Replay"
            // Background Color: Slate (#1E293B), Text: "RETRY (R)" (Bold White, fontSize = 20)
            Button replayBtn = CreateFlatButton(victoryPanelObj.transform, "Btn_Replay", new Vector2(0f, -65f), new Vector2(520f, 60f), "RETRY (R)", ColorButtonCharcoal, Color.white, 20f, ColorButtonHoverHighlight, fontAsset);
            if (gm != null)
            {
                UnityEventTools.AddPersistentListener(replayBtn.onClick, gm.RestartGame);
            }

            // Button 3 - Main Menu (Standard Button: Width = 520, Height = 60): "Btn_VictoryMenu"
            // Background Color: Slate (#1E293B), Text: "MAIN MENU" (Bold White, fontSize = 20)
            Button vicMenuBtn = CreateFlatButton(victoryPanelObj.transform, "Btn_VictoryMenu", new Vector2(0f, -135f), new Vector2(520f, 60f), "MAIN MENU", ColorButtonCharcoal, Color.white, 20f, ColorButtonHoverHighlight, fontAsset);
            if (gm != null)
            {
                UnityEventTools.AddPersistentListener(vicMenuBtn.onClick, gm.ExitToMenu);
            }

            victoryPanelObj.SetActive(false);

            // 6. Build Pause Panel (Fullscreen semi-transparent backdrop: Color(0.06f, 0.08f, 0.12f, 0.92f))
            GameObject pausePanelObj = new GameObject("PausePanel", typeof(RectTransform));
            pausePanelObj.transform.SetParent(canvasObj.transform, false);
            RectTransform pauseRect = pausePanelObj.GetComponent<RectTransform>();
            pauseRect.anchorMin = Vector2.zero;
            pauseRect.anchorMax = Vector2.one;
            pauseRect.offsetMin = Vector2.zero;
            pauseRect.offsetMax = Vector2.zero;
            pauseRect.localScale = Vector3.one;

            Image pauseBg = pausePanelObj.AddComponent<Image>();
            pauseBg.sprite = HealthBarUI.GetOrCreateWhiteSprite();
            pauseBg.type = Image.Type.Simple;
            pauseBg.color = ColorPausePanelBg;

            // Title Text: TextMeshProUGUI (Pos: 0, 130; Size: 800, 70), text = "SYSTEM PAUSED", color = new Color(1f, 0.85f, 0.1f, 1f), fontSize = 46, fontStyle = Bold, alignment = Center.
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
            ConfigureTMPText(pauseTitleTmp, fontAsset, fontSize: 46f, alignment: TextAlignmentOptions.Center, color: ColorPauseTitleGold);

            // Button 1 - Resume (Standard Button: Width = 520, Height = 60): "Btn_Resume" (Pos: 0, 40)
            Button resumeBtn = CreateFlatButton(pausePanelObj.transform, "Btn_Resume", new Vector2(0f, 40f), new Vector2(520f, 60f), "RESUME (ESC)", ColorButtonCharcoal, Color.white, 20f, ColorButtonHoverHighlight, fontAsset);
            if (gm != null)
            {
                UnityEventTools.AddPersistentListener(resumeBtn.onClick, gm.ResumeGame);
            }

            // Button 2 - Main Menu (Standard Button: Width = 520, Height = 60): "Btn_PauseMenu" (Pos: 0, -40)
            Button pauseMenuBtn = CreateFlatButton(pausePanelObj.transform, "Btn_PauseMenu", new Vector2(0f, -40f), new Vector2(520f, 60f), "MAIN MENU", ColorButtonCharcoal, Color.white, 20f, ColorButtonHoverHighlight, fontAsset);
            if (gm != null)
            {
                UnityEventTools.AddPersistentListener(pauseMenuBtn.onClick, gm.ExitToMenu);
            }

            // Button 3 - Exit Game (Standard Button: Width = 520, Height = 60): "Btn_Quit" (Pos: 0, -120)
            Button quitBtn = CreateFlatButton(pausePanelObj.transform, "Btn_Quit", new Vector2(0f, -120f), new Vector2(520f, 60f), "EXIT GAME", ColorButtonCharcoal, Color.white, 20f, ColorButtonHoverHighlight, fontAsset);
            if (gm != null)
            {
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
            btn.colors = cb;

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
            labelTmp.enableWordWrapping = false;
            labelTmp.overflowMode = TextOverflowModes.Overflow;
            ConfigureTMPText(labelTmp, fontAsset, fontSize: fontSize, alignment: TextAlignmentOptions.Center, color: textColor);

            return btn;
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
            string[] modalPanelNames = new string[] { "StartPanel", "GameOverPanel", "VictoryPanel", "PausePanel" };
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

                tmp.enableWordWrapping = false;
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
                bool isStartBtn = goName == "Btn_Start" || (p != null && p.name == "Btn_Start");
                bool isTitle = goName.Contains("Title") || goName.Contains("Header");

                if (goName == "Btn_NextLevel" || (p != null && p.name == "Btn_NextLevel"))
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
                else if (isTitle)
                {
                    tmp.fontSize = 56f;
                }
                else if (isStartBtn)
                {
                    tmp.fontSize = 26f;
                }
                else if (isButton)
                {
                    tmp.fontSize = 22f;
                }
                else if (goName == "Timer_Text" || goName.Contains("Score"))
                {
                    tmp.fontSize = 32f;
                }
                else if (goName == "HP_Text")
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
                        tmp.fontSize = 22f;
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
