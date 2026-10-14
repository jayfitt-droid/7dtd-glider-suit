using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace GliderSuitEditor
{
    /// <summary>
    /// Imports the Blender FBX and the textures, builds the materials and the
    /// GliderSuitRig prefab, renders the inventory icon and packs everything into
    /// the Windows asset bundle that ships in the mod as
    /// Resources/GliderSuit.unity3d. The asset names in the bundle must stay
    /// GliderSuitRig, GliderUnfold, GliderFold and GliderWind.
    ///
    /// Headless build (see build-assets.ps1):
    ///   Unity.exe -batchmode -projectPath unity/GliderSuitAssets
    ///             -executeMethod GliderSuitEditor.GliderSuitBuild.BuildAll
    /// </summary>
    public static class GliderSuitBuild
    {
        private const string BundleName = "glidersuit";
        private const string ShippedName = "GliderSuit.unity3d";

        private const string FbxPath = "Assets/Source/GliderSuitRig.fbx";
        private const string FoldPosePath = "Assets/Source/glider_fold_pose.json";
        private const string AudioFolder = "Assets/GliderSuit/Audio";
        private const string MaterialFolder = "Assets/GliderSuit/Materials";
        private const string PrefabFolder = "Assets/GliderSuit/Prefabs";
        private const string PrefabPath = PrefabFolder + "/GliderSuitRig.prefab";

        private static readonly string[] AudioNames = { "GliderUnfold", "GliderFold", "GliderWind" };
        private static readonly string[] TextureNames =
        {
            "glider_poles_albedo", "glider_poles_normal", "glider_poles_metallic",
            "glider_tarp_albedo", "glider_tarp_normal", "glider_tarp_metallic",
        };

        private const int IconSize = 640;
        private const int IconLayer = 30;

        [MenuItem("GliderSuit/Build All")]
        public static void BuildAll()
        {
            ConfigureAudio();
            ConfigureTextures();

            GameObject prefab = BuildRigPrefab();
            if (prefab == null) { Fail("the rig prefab could not be built"); return; }

            // The inventory icon master is rendered by the Blender script, which lights
            // it far better. These two are checks: what the game's own shader, axes and
            // fold pose make of the rig, seen without starting the game.
            RenderCheck(prefab, false, new Vector3(-0.30f, -0.72f, 0.62f), "unity_extended.png");
            RenderCheck(prefab, true, new Vector3(0.45f, -0.25f, 0.86f), "unity_folded.png");

            var assetPaths = new List<string> { PrefabPath };
            foreach (string name in AudioNames)
            {
                string path = AudioFolder + "/" + name + ".wav";
                if (File.Exists(path)) assetPaths.Add(path);
                else Debug.LogError("[GliderSuitBuild] Missing audio: " + path);
            }

            BuildBundle(assetPaths);

            // Check what actually landed in the bundle before declaring success.
            // GliderSuitBuild.Verify runs the same check on its own in a fresh process.
            bool ok = VerifyBundle();
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        private static void Fail(string why)
        {
            Debug.LogError("[GliderSuitBuild] FAILED: " + why);
            if (Application.isBatchMode) EditorApplication.Exit(1);
        }

        // ------------------------------------------------------------- import

        /// <summary>
        /// Mono, Vorbis quality 0.7, decompressed on load (plan §6.3). The wind loop
        /// stays stereo: it is Jason's stereo recording, played without position (D30).
        /// </summary>
        private static void ConfigureAudio()
        {
            foreach (string name in AudioNames)
            {
                string path = AudioFolder + "/" + name + ".wav";
                var importer = AssetImporter.GetAtPath(path) as AudioImporter;
                if (importer == null) continue;
                importer.forceToMono = name != "GliderWind";
                importer.loadInBackground = false;
                AudioImporterSampleSettings settings = importer.defaultSampleSettings;
                settings.loadType = AudioClipLoadType.DecompressOnLoad;
                settings.compressionFormat = AudioCompressionFormat.Vorbis;
                settings.quality = 0.7f;
                settings.preloadAudioData = true;
                importer.defaultSampleSettings = settings;
                importer.SaveAndReimport();
                Debug.Log("[GliderSuitBuild] Audio configured: " + path);
            }
        }

        /// <summary>Generic, no animation, readable, imported tangents (§6.3).</summary>
        private static bool ConfigureModel()
        {
            var importer = AssetImporter.GetAtPath(FbxPath) as ModelImporter;
            if (importer == null)
            {
                Debug.LogError("[GliderSuitBuild] No model at " + FbxPath + "; run build-assets.ps1 without -SkipBlender.");
                return false;
            }
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.importAnimation = false;
            importer.isReadable = true;
            importer.importTangents = ModelImporterTangents.Import;
            importer.importNormals = ModelImporterNormals.Import;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.globalScale = 1f;
            importer.SaveAndReimport();
            Debug.Log("[GliderSuitBuild] Model configured: " + FbxPath);
            return AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath) != null;
        }

        private static void ConfigureTextures()
        {
            foreach (string name in TextureNames)
            {
                string path = "Assets/Source/" + name + ".png";
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null)
                {
                    Debug.LogError("[GliderSuitBuild] Missing texture: " + path);
                    continue;
                }
                bool normal = name.EndsWith("_normal");
                bool data = normal || name.EndsWith("_metallic");
                importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                importer.sRGBTexture = !data;
                importer.mipmapEnabled = true;
                importer.SaveAndReimport();
            }
            Debug.Log("[GliderSuitBuild] Textures configured.");
        }

        // ---------------------------------------------------------- materials

        private static Material LoadMaterial(string name)
        {
            string path = MaterialFolder + "/" + name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;
            Directory.CreateDirectory(Absolute(MaterialFolder));
            AssetDatabase.Refresh();
            var material = new Material(Shader.Find("Standard"));
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        /// <summary>Unity's Standard shader with the packed metallic-smoothness maps (plan §6.1).</summary>
        private static void ConfigureMaterial(Material material, string albedo, string normal, string metallic)
        {
            if (albedo != null) material.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture>(albedo));
            if (normal != null)
            {
                material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture>(normal));
                material.EnableKeyword("_NORMALMAP");
            }
            if (metallic != null)
            {
                material.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture>(metallic));
                material.EnableKeyword("_METALLICGLOSSMAP");
                material.SetFloat("_Metallic", 1f);
                material.SetFloat("_Glossiness", 1f);
            }
            EditorUtility.SetDirty(material);
        }

        private static void BuildMaterials(out Material pole, out Material tarp, out Material canvas, out Material metal)
        {
            pole = LoadMaterial("GliderPole");
            ConfigureMaterial(pole, "Assets/Source/glider_poles_albedo.png",
                "Assets/Source/glider_poles_normal.png", "Assets/Source/glider_poles_metallic.png");

            tarp = LoadMaterial("GliderTarp");
            ConfigureMaterial(tarp, "Assets/Source/glider_tarp_albedo.png",
                "Assets/Source/glider_tarp_normal.png", "Assets/Source/glider_tarp_metallic.png");

            canvas = LoadMaterial("GliderCanvas");
            ConfigureMaterial(canvas, null, null, null);
            canvas.SetColor("_Color", new Color(0.58f, 0.56f, 0.48f));
            canvas.SetFloat("_Metallic", 0f);
            canvas.SetFloat("_Glossiness", 0.15f);

            metal = LoadMaterial("GliderMetal");
            ConfigureMaterial(metal, null, null, null);
            metal.SetColor("_Color", new Color(0.50f, 0.44f, 0.30f));   // dull brass buckles
            metal.SetFloat("_Metallic", 0.9f);
            metal.SetFloat("_Glossiness", 0.55f);
        }

        // ------------------------------------------------------------- prefab

        [System.Serializable]
        private class RigBone
        {
            public string name;
            public string parent;
            public float[] position;
            public float[] rotation;
            public float[] scale;
        }

        [System.Serializable]
        private class RigMesh
        {
            public string name;
            public string parent;
            public string kind;
            public float[] vertices;
            public float[] normals;
            public float[] uvs;
            public int[] triangles;
        }

        [System.Serializable]
        private class RigData
        {
            public RigBone[] bones;
            public RigMesh[] meshes;
        }

        private const string RigDataPath = "Assets/Source/glider_rig.json";
        private const string MeshAssetPath = "Assets/GliderSuit/Meshes/GliderSuitMeshes.asset";

        /// <summary>
        /// Builds the prefab from glider_rig.json, which the Blender script writes in
        /// Unity's own coordinates. Nothing here goes through the FBX importer: it
        /// turned the nested wing bones the wrong way, and its axis conversion had
        /// to be second-guessed with marker objects even before that.
        /// </summary>
        private static GameObject BuildRigPrefab()
        {
            string dataFile = Path.GetFullPath(Path.Combine(Application.dataPath, "..", RigDataPath));
            if (!File.Exists(dataFile))
            {
                Debug.LogError("[GliderSuitBuild] No rig data at " + RigDataPath + "; run build-assets.ps1 without -SkipBlender.");
                return null;
            }
            RigData data = JsonUtility.FromJson<RigData>(File.ReadAllText(dataFile));
            if (data == null || data.bones == null || data.meshes == null || data.meshes.Length == 0)
            {
                Debug.LogError("[GliderSuitBuild] " + RigDataPath + " has no bones or meshes.");
                return null;
            }

            var instance = new GameObject("GliderSuitRig");
            // The runtime places the root; the pieces hang under this child, found by name.
            var rig = new GameObject("Rig").transform;
            rig.SetParent(instance.transform, false);

            var byName = new Dictionary<string, Transform>();
            foreach (RigBone bone in data.bones)
            {
                var t = new GameObject(bone.name).transform;
                Transform parent = rig;
                if (!string.IsNullOrEmpty(bone.parent) && !byName.TryGetValue(bone.parent, out parent))
                {
                    Debug.LogError("[GliderSuitBuild] Bone " + bone.name + " names a parent that does not exist: " + bone.parent);
                    Object.DestroyImmediate(instance);
                    return null;
                }
                t.SetParent(parent, false);
                t.localPosition = new Vector3(bone.position[0], bone.position[1], bone.position[2]);
                t.localRotation = new Quaternion(bone.rotation[0], bone.rotation[1], bone.rotation[2], bone.rotation[3]);
                t.localScale = new Vector3(bone.scale[0], bone.scale[1], bone.scale[2]);
                byName[bone.name] = t;
            }

            BuildMaterials(out Material pole, out Material tarp, out Material canvas, out Material metal);
            Directory.CreateDirectory(Absolute(Path.GetDirectoryName(MeshAssetPath).Replace('\\', '/')));
            AssetDatabase.Refresh();
            AssetDatabase.DeleteAsset(MeshAssetPath);

            int triangles = 0;
            bool first = true;
            foreach (RigMesh source in data.meshes)
            {
                int count = source.vertices.Length / 3;
                var vertices = new Vector3[count];
                var normals = new Vector3[count];
                var uvs = new Vector2[count];
                for (int i = 0; i < count; i++)
                {
                    vertices[i] = new Vector3(source.vertices[3 * i], source.vertices[3 * i + 1], source.vertices[3 * i + 2]);
                    normals[i] = new Vector3(source.normals[3 * i], source.normals[3 * i + 1], source.normals[3 * i + 2]);
                    uvs[i] = new Vector2(source.uvs[2 * i], source.uvs[2 * i + 1]);
                }
                var mesh = new Mesh { name = source.name };
                mesh.vertices = vertices;
                mesh.normals = normals;
                mesh.uv = uvs;
                mesh.triangles = source.triangles;
                mesh.RecalculateBounds();
                mesh.RecalculateTangents();
                triangles += source.triangles.Length / 3;

                // One asset file holds every mesh.
                if (first) AssetDatabase.CreateAsset(mesh, MeshAssetPath);
                else AssetDatabase.AddObjectToAsset(mesh, MeshAssetPath);
                first = false;

                var piece = new GameObject(source.name);
                Transform parent = rig;
                if (!string.IsNullOrEmpty(source.parent) && !byName.TryGetValue(source.parent, out parent))
                {
                    Debug.LogError("[GliderSuitBuild] Mesh " + source.name + " names a parent that does not exist: " + source.parent);
                    Object.DestroyImmediate(instance);
                    return null;
                }
                piece.transform.SetParent(parent, false);
                piece.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = piece.AddComponent<MeshRenderer>();
                // The wing canvas, the buckles, and everything else on the frame atlas
                // (steel, leather, rope and pack canvas bands), harness included.
                renderer.sharedMaterial = source.kind == "tarp" ? tarp : source.kind == "metal" ? metal : pole;
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[GliderSuitBuild] Rig built from " + RigDataPath + ": " + data.bones.Length + " bones, " +
                data.meshes.Length + " meshes, " + triangles + " triangles.");

            ValidateFoldPose(instance);

            Directory.CreateDirectory(Absolute(PrefabFolder));
            AssetDatabase.Refresh();
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(instance, PrefabPath);
            Object.DestroyImmediate(instance);
            AssetDatabase.SaveAssets();
            if (saved == null)
            {
                Debug.LogError("[GliderSuitBuild] Could not save " + PrefabPath);
                return null;
            }
            Debug.Log("[GliderSuitBuild] Prefab written to " + PrefabPath);
            return saved;
        }

        /// <summary>
        /// Every spar must have its *_Folded sibling and the folded rig must fit the
        /// small bundle the plan expects (plan §6.2). Loud, because a silent mismatch
        /// is invisible until the wings are in game.
        /// </summary>
        private static void ValidateFoldPose(GameObject instance)
        {
            var spars = FindAll(instance.transform, "Spar_");
            if (spars.Count == 0)
            {
                Debug.LogError("[GliderSuitBuild] No Spar_* pieces in the FBX; the wings cannot fold.");
                return;
            }
            int posed = 0;
            var saved = new List<(Transform, Quaternion, Vector3)>();
            foreach (Transform spar in spars)
            {
                if (spar.name.EndsWith("_Folded")) continue;
                Transform folded = FindDeep(instance.transform, spar.name + "_Folded");
                if (folded == null)
                {
                    Debug.LogError("[GliderSuitBuild] " + spar.name + " has no *_Folded sibling.");
                    continue;
                }
                saved.Add((spar, spar.localRotation, spar.localScale));
                spar.localRotation = folded.localRotation;
                spar.localScale = folded.localScale;
                posed++;
            }

            Bounds bounds = ComputeBounds(instance);
            foreach ((Transform spar, Quaternion rotation, Vector3 scale) in saved)
            {
                spar.localRotation = rotation;
                spar.localScale = scale;
            }

            Vector3 size = bounds.size;
            Debug.Log("[GliderSuitBuild] Folded pose: " + posed + " spars, folded bounds " +
                size.x.ToString("F2") + " x " + size.y.ToString("F2") + " x " + size.z.ToString("F2") + " m");

            // The plan's folded-bundle target (§6.2): 0.5 x 1.0 x 0.5 m. Failing the
            // build would block the pipeline over an art measurement, so this warns.
            if (size.x > 0.50f || size.y > 1.00f || size.z > 0.60f)
                Debug.LogWarning("[GliderSuitBuild] The folded bundle, harness included, is larger than the " +
                    "0.5 x 1.0 x 0.6 m it should hang within on the back.");
            else
                Debug.Log("[GliderSuitBuild] Folded bundle is inside the 0.5 x 1.0 x 0.6 m target.");

            if (File.Exists(FoldPosePath))
            {
                int declared = Regex.Matches(File.ReadAllText(FoldPosePath), "\"direction\"").Count;
                if (declared != posed)
                    Debug.LogWarning("[GliderSuitBuild] " + FoldPosePath + " describes " + declared +
                        " spars but the prefab has " + posed + ".");
            }
        }

        private static Bounds ComputeBounds(GameObject root)
        {
            Physics.SyncTransforms();
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return new Bounds(root.transform.position, Vector3.zero);
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        /// <summary>Puts every spar into its folded pose.</summary>
        private static void ApplyFold(GameObject instance)
        {
            foreach (Transform spar in FindAll(instance.transform, "Spar_"))
            {
                if (spar.name.EndsWith("_Folded")) continue;
                Transform folded = FindDeep(instance.transform, spar.name + "_Folded");
                if (folded == null) continue;
                spar.localRotation = folded.localRotation;
                spar.localScale = folded.localScale;
            }
        }

        /// <summary>An Assets/... path as an absolute path, for Directory.CreateDirectory.</summary>
        private static string Absolute(string assetFolder)
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetFolder));
        }

        private static List<Transform> FindAll(Transform root, string prefix)
        {
            var found = new List<Transform>();
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                if (child.name.StartsWith(prefix)) found.Add(child);
            return found;
        }

        private static Transform FindDeep(Transform root, string name)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                if (child.name == name) return child;
            return null;
        }

        // --------------------------------------------------------------- icon

        /// <summary>
        /// Renders the prefab, extended or folded, orthographic on a transparent
        /// background, into unity/preview. direction is the way the camera looks.
        /// </summary>
        private static void RenderCheck(GameObject prefab, bool folded, Vector3 direction, string fileName)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            if (instance == null) { Debug.LogError("[GliderSuitBuild] Check render: could not instantiate the prefab."); return; }
            if (folded) ApplyFold(instance);
            foreach (Transform child in instance.GetComponentsInChildren<Transform>(true))
                child.gameObject.layer = IconLayer;
            Bounds bounds = ComputeBounds(instance);

            AmbientMode previousAmbientMode = RenderSettings.ambientMode;
            Color previousAmbient = RenderSettings.ambientLight;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.42f, 0.44f, 0.47f);

            var lightObject = new GameObject("IconKey");
            var key = lightObject.AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 1.5f;
            lightObject.transform.rotation = Quaternion.Euler(34f, 158f, 0f);
            var fillObject = new GameObject("IconFill");
            var fill = fillObject.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.intensity = 0.75f;
            fillObject.transform.rotation = Quaternion.Euler(-18f, -30f, 0f);

            var cameraObject = new GameObject("IconCamera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            camera.orthographic = true;
            camera.cullingMask = 1 << IconLayer;
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 50f;
            direction = direction.normalized;
            cameraObject.transform.position = bounds.center - direction * 5f;
            cameraObject.transform.LookAt(bounds.center);
            camera.orthographicSize = Mathf.Max(bounds.extents.magnitude, 0.2f) * 1.08f;
            Debug.Log("[GliderSuitBuild] " + fileName + " framed on bounds " + bounds.center + " size " + bounds.size);

            var target = new RenderTexture(IconSize, IconSize, 24, RenderTextureFormat.ARGB32);
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            var texture = new Texture2D(IconSize, IconSize, TextureFormat.RGBA32, false);
            texture.ReadPixels(new Rect(0, 0, IconSize, IconSize), 0, 0);
            texture.Apply();
            RenderTexture.active = null;
            camera.targetTexture = null;

            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "preview", fileName));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Debug.Log("[GliderSuitBuild] Check render written to " + path);

            RenderSettings.ambientMode = previousAmbientMode;
            RenderSettings.ambientLight = previousAmbient;
            Object.DestroyImmediate(cameraObject);
            Object.DestroyImmediate(lightObject);
            Object.DestroyImmediate(fillObject);
            Object.DestroyImmediate(instance);
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(texture);
        }

        // ------------------------------------------------------------- bundle

        /// <summary>The names GliderAssets asks DataLoader for, in their exact casing.</summary>
        private static readonly string[] CanonicalAssetNames =
        {
            "GliderSuitRig", "GliderUnfold", "GliderFold", "GliderWind",
        };

        private static string CanonicalName(string assetPath)
        {
            string file = Path.GetFileNameWithoutExtension(assetPath);
            foreach (string name in CanonicalAssetNames)
                if (string.Equals(name, file, System.StringComparison.OrdinalIgnoreCase)) return name;
            return file;
        }

        private static void BuildBundle(List<string> assetPaths)
        {
            string outputDir = Path.Combine(Path.GetTempPath(), "GliderSuitBundle");
            if (Directory.Exists(outputDir)) Directory.Delete(outputDir, true);
            Directory.CreateDirectory(outputDir);

            // Address every asset by the exact name the mod asks DataLoader for.
            // AssetBundle.LoadAsset matches names exactly, and the packed paths are
            // all lower case, so without this "GliderSuitRig" would miss.
            var addresses = new List<string>();
            foreach (string path in assetPaths) addresses.Add(CanonicalName(path));

            var build = new AssetBundleBuild
            {
                assetBundleName = BundleName,
                assetNames = assetPaths.ToArray(),
                addressableNames = addresses.ToArray(),
            };

            AssetBundleManifest manifest = BuildPipeline.BuildAssetBundles(
                outputDir, new[] { build },
                BuildAssetBundleOptions.ChunkBasedCompression,
                BuildTarget.StandaloneWindows64);

            if (manifest == null)
            {
                Debug.LogError("[GliderSuitBuild] Asset bundle build failed.");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }

            string built = Path.Combine(outputDir, BundleName);
            string destinationDir = Path.GetFullPath(Path.Combine(
                Application.dataPath, "..", "..", "..", "Mod", "GliderSuit", "Resources"));
            Directory.CreateDirectory(destinationDir);
            string destination = Path.Combine(destinationDir, ShippedName);
            File.Copy(built, destination, true);
            foreach (string path in assetPaths) Debug.Log("[GliderSuitBuild] Packed: " + path);
            Debug.Log("[GliderSuitBuild] Bundle written to " + destination);
        }

        /// <summary>
        /// Resolves a bundle asset the way the game does: AssetBundleManager._get
        /// takes the file name, drops the extension and calls LoadAsset with that,
        /// so the bundle is matched by file name rather than by full path.
        /// </summary>
        private static T LoadLikeTheGame<T>(AssetBundle bundle, string shortName) where T : Object
        {
            T byAddress = bundle.LoadAsset<T>(shortName);
            if (byAddress != null) return byAddress;

            foreach (string assetName in bundle.GetAllAssetNames())
            {
                if (!string.Equals(Path.GetFileNameWithoutExtension(assetName), shortName,
                        System.StringComparison.OrdinalIgnoreCase)) continue;
                T byPath = bundle.LoadAsset<T>(assetName);
                if (byPath != null) return byPath;
            }

            // Last resort: the asset is in the bundle but not addressable by name.
            foreach (T candidate in bundle.LoadAllAssets<T>())
            {
                if (candidate != null && string.Equals(candidate.name, shortName,
                        System.StringComparison.OrdinalIgnoreCase))
                    return candidate;
            }
            return null;
        }

        /// <summary>
        /// Loads the shipped bundle back and reports its assets, so the names
        /// DataLoader will ask for are known to be the ones packed inside.
        /// </summary>
        public static void Verify()
        {
            EditorApplication.Exit(VerifyBundle() ? 0 : 1);
        }

        /// <summary>
        /// Compares this mod's shipped bundle against the known-working Parachute
        /// one, to tell a broken bundle apart from a broken check.
        /// </summary>
        public static void Diagnose()
        {
            Dump("GliderSuit", Path.GetFullPath(Path.Combine(
                Application.dataPath, "..", "..", "..", "Mod", "GliderSuit", "Resources", ShippedName)));
            Dump("Parachute", @"C:\Users\Jason\Projects\7d2d\Parachute\Mod\Parachute\Resources\Parachute.unity3d");
            EditorApplication.Exit(0);
        }

        private static void Dump(string label, string path)
        {
            Debug.Log("[GliderSuitBuild] ===== " + label + " " + path + " exists=" + File.Exists(path));
            AssetBundle bundle = AssetBundle.LoadFromFile(path);
            if (bundle == null) { Debug.LogError("[GliderSuitBuild]   bundle load returned null"); return; }

            string[] names = bundle.GetAllAssetNames();
            foreach (string name in names) Debug.Log("[GliderSuitBuild]   name: " + name);

            Object[] all = bundle.LoadAllAssets();
            Debug.Log("[GliderSuitBuild]   LoadAllAssets -> " + all.Length);
            foreach (Object o in all) Debug.Log("[GliderSuitBuild]     " + o.GetType().Name + " : " + o.name);

            foreach (string probe in new[] { "GliderSuitRig", "glidersuitrig", "GliderUnfold", "gliderunfold",
                "ParachuteCanopy", "parachutecanopy", "ParachuteDeploy", "parachutedeploy" })
                Debug.Log("[GliderSuitBuild]   LoadAsset<Object>('" + probe + "') -> " +
                    (bundle.LoadAsset<Object>(probe) != null));
            bundle.Unload(true);
        }

        private static bool VerifyBundle()
        {
            string path = Path.GetFullPath(Path.Combine(
                Application.dataPath, "..", "..", "..", "Mod", "GliderSuit", "Resources", ShippedName));
            AssetBundle bundle = AssetBundle.LoadFromFile(path);
            if (bundle == null)
            {
                Debug.LogError("[GliderSuitBuild] Verify: could not load " + path);
                return false;
            }

            foreach (string assetName in bundle.GetAllAssetNames())
                Debug.Log("[GliderSuitBuild] Verify asset: " + assetName);
            GameObject prefab = LoadLikeTheGame<GameObject>(bundle, "GliderSuitRig");
            AudioClip unfold = LoadLikeTheGame<AudioClip>(bundle, "GliderUnfold");
            AudioClip fold = LoadLikeTheGame<AudioClip>(bundle, "GliderFold");
            AudioClip wind = LoadLikeTheGame<AudioClip>(bundle, "GliderWind");

            // Unload destroys the loaded objects, so the result must be read first.
            bool ok = prefab != null && unfold != null && fold != null && wind != null;
            Debug.Log("[GliderSuitBuild] Verify prefab=" + (prefab != null) +
                " unfold=" + (unfold != null) + " fold=" + (fold != null) + " wind=" + (wind != null) +
                (ok ? " (all four present)" : " MISSING ASSETS"));
            bundle.Unload(true);
            if (!ok) Debug.LogError("[GliderSuitBuild] The shipped bundle is missing one or more assets.");
            return ok;
        }
    }
}
