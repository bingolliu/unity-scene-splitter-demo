using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using Path = System.IO.Path;

namespace SceneSplitterDemo.EditorTools
{
    /// <summary>
    /// 场景切割工具。
    ///
    /// 原理：
    /// - 把场景按 cubeSize 划分成空间方块；
    /// - 用 MeshCollider + Physics.OverlapBox 找出每个方块覆盖到的场景物体；
    /// - 为每个物体记录它依赖的 mesh / material 资源路径（uuid 由层级索引生成）；
    /// - 生成运行时可读取的切割数据（方块 -> uuid，uuid -> 资源 id）。
    ///
    /// 实现要点：
    /// - 资源根目录为 Assets/SceneSplitterDemo/Resources/SceneRes；
    /// - 数据里记录 Resources 相对路径，加载端直接拼接；
    /// - 序列化使用 JsonUtility，不依赖第三方 JSON 库；
    /// - mesh 未剥离时会自动生成 Mesh 资源；
    /// - 方块遍历从包围盒的整数格边界开始，保证边缘不遗漏。
    /// </summary>
    public class SceneCuttingWindow : EditorWindow
    {
        private const string SceneFolder = "Assets/SceneSplitterDemo/Scenes";
        private const string ResourceRootAsset = "Assets/SceneSplitterDemo/Resources/SceneRes";

        private static readonly string[] SkipNamePrefixes =
        {
            "terr", "water", "npc", "fx"
        };

        private List<string> _scenePaths = new List<string>();
        private string[] _sceneNames = new string[0];
        private int _selectedIndex = 0;

        private int _cubeSize = 10;
        private bool _showCutCubes = true;

        private readonly List<RuntimeCube> _previewCubes = new List<RuntimeCube>();
        private string _lastCutScene = string.Empty;

        private Vector2 _scroll;

        [MenuItem("Tools/场景切割与按需加载/切割窗口", false, 1)]
        public static void ShowWindow()
        {
            SceneCuttingWindow window = GetWindow<SceneCuttingWindow>("场景切割");
            window.minSize = new Vector2(460f, 420f);
            window.RefreshSceneList();
        }

        [MenuItem("Tools/场景切割与按需加载/打开演示场景", false, 20)]
        public static void OpenDemoScene()
        {
            string scenePath = SceneFolder + "/map35.unity";
            if (!File.Exists(scenePath))
            {
                EditorUtility.DisplayDialog("打开演示场景", "找不到场景: " + scenePath, "确定");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        }

        private void OnEnable()
        {
            RefreshSceneList();
            SceneView.duringSceneGui += OnSceneGui;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGui;
        }

        private void RefreshSceneList()
        {
            _scenePaths.Clear();
            if (Directory.Exists(SceneFolder))
            {
                _scenePaths.AddRange(
                    Directory.GetFiles(SceneFolder, "*.unity", SearchOption.TopDirectoryOnly)
                        .Select(p => p.Replace('\\', '/'))
                        .OrderBy(p => p));
            }

            _sceneNames = _scenePaths.Select(Path.GetFileNameWithoutExtension).ToArray();
            _selectedIndex = Mathf.Clamp(_selectedIndex, 0, Mathf.Max(0, _scenePaths.Count - 1));
        }

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("场景切割 / 按需加载", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "1. 选择场景 -> 切割；\n" +
                "2. 切割数据会写入 Resources/SceneRes/<场景名>/<场景名>_data.bytes；\n" +
                "3. 运行时 SceneResLoadOnDemand 读取该数据，按英雄附近的九宫格加载资源。",
                MessageType.Info);

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("场景列表", EditorStyles.boldLabel);

            if (_sceneNames.Length == 0)
            {
                EditorGUILayout.HelpBox("在 " + SceneFolder + " 下没有找到 .unity 场景。", MessageType.Warning);
            }
            else
            {
                _selectedIndex = EditorGUILayout.Popup("场景", _selectedIndex, _sceneNames);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("打开选中场景"))
                    {
                        OpenSelectedScene();
                    }

                    if (GUILayout.Button("切割选中场景"))
                    {
                        CutSelectedScene();
                    }
                }
            }

            if (GUILayout.Button("切割当前打开的场景"))
            {
                CutCurrentScene();
            }

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("参数", EditorStyles.boldLabel);
            _cubeSize = Mathf.Max(1, EditorGUILayout.IntField("方块大小 (世界单位)", _cubeSize));
            _showCutCubes = EditorGUILayout.Toggle("在 Scene 视图显示切割方块", _showCutCubes);

            EditorGUILayout.Space(8);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("读取当前场景切割数据"))
                {
                    LoadCurrentSceneCutData();
                }

                if (GUILayout.Button("刷新场景列表"))
                {
                    RefreshSceneList();
                }
            }

            if (!string.IsNullOrEmpty(_lastCutScene))
            {
                EditorGUILayout.Space(6);
                EditorGUILayout.LabelField("最近切割: " + _lastCutScene, EditorStyles.miniLabel);
                EditorGUILayout.LabelField("方块数量: " + _previewCubes.Count, EditorStyles.miniLabel);
            }

            EditorGUILayout.Space(8);
            EditorGUILayout.HelpBox(
                "说明：\n" +
                "- 参与切割的根节点固定为 Scene / Effect / Animation（可在 SceneResLoadOnDemand.nodeNames 修改）；\n" +
                "- Terrain / water / npc / fx 开头的物体不会被切割；\n" +
                "- mesh 会被自动提取为 <某目录>/Mesh/<mesh名>.asset，material 使用原资源；\n" +
                "- 运行时方块大小需与切割时一致（数据内已保存 cubeSize）。",
                MessageType.None);

            EditorGUILayout.EndScrollView();
        }

        private void OpenSelectedScene()
        {
            if (_scenePaths.Count == 0)
            {
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            EditorSceneManager.OpenScene(_scenePaths[_selectedIndex], OpenSceneMode.Single);
        }

        private void CutSelectedScene()
        {
            if (_scenePaths.Count == 0)
            {
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            Scene scene = EditorSceneManager.OpenScene(_scenePaths[_selectedIndex], OpenSceneMode.Single);
            CutScene(scene);
        }

        private void CutCurrentScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || string.IsNullOrEmpty(scene.path))
            {
                EditorUtility.DisplayDialog("场景切割", "当前场景尚未保存到磁盘，请先保存场景。", "确定");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            CutScene(scene);
        }

        private void CutScene(Scene scene)
        {
            string sceneName = scene.name;
            SceneResLoadOnDemand.cubeSize = _cubeSize;

            try
            {
                EditorUtility.DisplayProgressBar("场景切割", "收集场景物体 uuid...", 0f);

                List<GameObject> addedColliders = AddMeshColliders();
                Physics.SyncTransforms();
                try
                {
                    SceneResLoadOnDemand.InitUuid2ObjDictionary(false);

                    Dictionary<GameObject, string> go2Uuid = new Dictionary<GameObject, string>();
                    foreach (KeyValuePair<string, GameObject> kv in SceneResLoadOnDemand.uuid2ObjDictionary)
                    {
                        if (kv.Value != null)
                        {
                            go2Uuid[kv.Value] = kv.Key;
                        }
                    }

                    Dictionary<string, ObjectResInfo> objectInfos = new Dictionary<string, ObjectResInfo>();
                    Dictionary<string, int> path2Id = new Dictionary<string, int>();
                    List<RuntimeCube> cubes = new List<RuntimeCube>();

                    Bounds bounds = CalculateSceneBounds(go2Uuid);
                    CutBounds(bounds, go2Uuid, objectInfos, path2Id, cubes, sceneName);

                    int minX = 0, maxX = 0, minY = 0, maxY = 0, minZ = 0, maxZ = 0;
                    for (int i = 0; i < cubes.Count; i++)
                    {
                        RuntimeCube cube = cubes[i];
                        int x = Mathf.RoundToInt(cube.pos.x);
                        int y = Mathf.RoundToInt(cube.pos.y);
                        int z = Mathf.RoundToInt(cube.pos.z);

                        if (i == 0)
                        {
                            minX = maxX = x;
                            minY = maxY = y;
                            minZ = maxZ = z;
                        }
                        else
                        {
                            minX = Mathf.Min(minX, x);
                            maxX = Mathf.Max(maxX, x);
                            minY = Mathf.Min(minY, y);
                            maxY = Mathf.Max(maxY, y);
                            minZ = Mathf.Min(minZ, z);
                            maxZ = Mathf.Max(maxZ, z);
                        }
                    }

                    RuntimeSceneData data = new RuntimeSceneData
                    {
                        cubeSize = _cubeSize,
                        minArrayIndexX = minX,
                        maxArrayIndexX = maxX,
                        minArrayIndexY = minY,
                        maxArrayIndexY = maxY,
                        minArrayIndexZ = minZ,
                        maxArrayIndexZ = maxZ,
                        cubeList = cubes.ToArray(),
                        pathArray = path2Id.OrderBy(kv => kv.Value).Select(kv => kv.Key).ToArray(),
                        runtimeGameObjectResArray = BuildRuntimeObjectResArray(objectInfos)
                    };

                    SaveCutData(sceneName, data, objectInfos);

                    _previewCubes.Clear();
                    _previewCubes.AddRange(cubes);
                    _lastCutScene = sceneName;

                    Debug.Log(string.Format(
                        "[场景切割] {0}: {1} 个方块, {2} 个物体, {3} 个资源",
                        sceneName, cubes.Count, objectInfos.Count, path2Id.Count));
                }
                finally
                {
                    RemoveMeshColliders(addedColliders);
                }
            }
            catch (OperationCanceledException)
            {
                Debug.Log("[场景切割] 已取消");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorUtility.DisplayDialog("场景切割失败", e.Message, "确定");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
        }

        private Bounds CalculateSceneBounds(Dictionary<GameObject, string> go2Uuid)
        {
            Bounds bounds = new Bounds(Vector3.zero, Vector3.zero);
            bool hasBounds = false;

            foreach (GameObject go in go2Uuid.Keys)
            {
                if (go == null)
                {
                    continue;
                }

                Renderer renderer = go.GetComponent<Renderer>();
                if (renderer == null)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            if (!hasBounds)
            {
                bounds = new Bounds(Vector3.zero, Vector3.one * _cubeSize);
            }

            return bounds;
        }

        private void CutBounds(
            Bounds bounds,
            Dictionary<GameObject, string> go2Uuid,
            Dictionary<string, ObjectResInfo> objectInfos,
            Dictionary<string, int> path2Id,
            List<RuntimeCube> cubes,
            string sceneName)
        {
            int cubeSize = _cubeSize;
            int startX = Mathf.FloorToInt(bounds.min.x / cubeSize) * cubeSize;
            int endX = Mathf.CeilToInt(bounds.max.x / cubeSize) * cubeSize;
            int startY = Mathf.FloorToInt(bounds.min.y / cubeSize) * cubeSize;
            int endY = Mathf.CeilToInt(bounds.max.y / cubeSize) * cubeSize;
            int startZ = Mathf.FloorToInt(bounds.min.z / cubeSize) * cubeSize;
            int endZ = Mathf.CeilToInt(bounds.max.z / cubeSize) * cubeSize;

            int countX = (endX - startX) / cubeSize + 1;
            int countY = (endY - startY) / cubeSize + 1;
            int countZ = (endZ - startZ) / cubeSize + 1;
            int total = Mathf.Max(1, countX * countY * countZ);
            int index = 0;

            Vector3 halfExtents = Vector3.one * (cubeSize * 0.5f);

            for (int x = startX; x <= endX; x += cubeSize)
            {
                for (int y = startY; y <= endY; y += cubeSize)
                {
                    for (int z = startZ; z <= endZ; z += cubeSize)
                    {
                        index++;
                        if (EditorUtility.DisplayCancelableProgressBar(
                                "场景切割 - 检测方块",
                                string.Format("{0}/{1}  ({2}, {3}, {4})", index, total, x, y, z),
                                (float)index / total))
                        {
                            throw new OperationCanceledException("场景切割已取消");
                        }

                        Vector3 center = new Vector3(x, y, z);
                        Collider[] colliders = Physics.OverlapBox(
                            center,
                            halfExtents,
                            Quaternion.identity,
                            ~0,
                            QueryTriggerInteraction.Ignore);

                        if (colliders == null || colliders.Length == 0)
                        {
                            continue;
                        }

                        RuntimeCube cube = new RuntimeCube
                        {
                            pos = center,
                            key = SceneResLoadOnDemand.ThreeD2index(x, y, z),
                            uuidArray = null
                        };

                        HashSet<string> cubeUuids = new HashSet<string>();
                        foreach (Collider collider in colliders)
                        {
                            if (collider == null)
                            {
                                continue;
                            }

                            GameObject go = collider.gameObject;
                            if (ShouldSkip(go))
                            {
                                continue;
                            }

                            if (!go2Uuid.TryGetValue(go, out string uuid))
                            {
                                continue;
                            }

                            if (RecordObject(go, uuid, objectInfos, path2Id, sceneName))
                            {
                                cubeUuids.Add(uuid);
                            }
                        }

                        if (cubeUuids.Count > 0)
                        {
                            cube.uuidArray = cubeUuids.ToArray();
                            cubes.Add(cube);
                        }
                    }
                }
            }
        }

        private bool RecordObject(
            GameObject go,
            string uuid,
            Dictionary<string, ObjectResInfo> objectInfos,
            Dictionary<string, int> path2Id,
            string sceneName)
        {
            if (objectInfos.TryGetValue(uuid, out ObjectResInfo existing))
            {
                return existing.ResourceIds.Count > 0;
            }

            ObjectResInfo info = new ObjectResInfo
            {
                nodePath = SceneResLoadOnDemand.GetPathToParentByRecursion(go.transform)
            };

            MeshRenderer meshRenderer = go.GetComponent<MeshRenderer>();
            if (meshRenderer != null)
            {
                foreach (Material material in meshRenderer.sharedMaterials)
                {
                    if (material == null || IsSkippedMaterial(material))
                    {
                        continue;
                    }

                    string materialPath = AssetDatabase.GetAssetPath(material);
                    string resourcePath = ToResourcePath(materialPath);
                    if (string.IsNullOrEmpty(resourcePath))
                    {
                        resourcePath = CopyMaterialToResources(material, materialPath, sceneName);
                    }

                    if (string.IsNullOrEmpty(resourcePath))
                    {
                        Debug.LogWarning("[场景切割] 材质不在 Resources 下，已跳过: " + materialPath);
                        continue;
                    }

                    info.ResourceIds.Add(GetOrAddResId(path2Id, resourcePath));
                }
            }

            Mesh mesh = null;
            MeshFilter meshFilter = go.GetComponent<MeshFilter>();
            if (meshFilter != null)
            {
                mesh = meshFilter.sharedMesh;
            }

            if (mesh == null)
            {
                MeshCollider meshCollider = go.GetComponent<MeshCollider>();
                if (meshCollider != null)
                {
                    mesh = meshCollider.sharedMesh;
                }
            }

            if (mesh != null && !IsSkippedMesh(mesh, go))
            {
                string meshResourcePath = EnsureMeshAsset(mesh, sceneName);
                if (!string.IsNullOrEmpty(meshResourcePath))
                {
                    info.ResourceIds.Add(GetOrAddResId(path2Id, meshResourcePath));
                }
            }

            objectInfos[uuid] = info;
            return info.ResourceIds.Count > 0;
        }

        private static int GetOrAddResId(Dictionary<string, int> path2Id, string resourcePath)
        {
            if (path2Id.TryGetValue(resourcePath, out int id))
            {
                return id;
            }

            id = path2Id.Count;
            path2Id.Add(resourcePath, id);
            return id;
        }

        private RuntimeGameObjectRes[] BuildRuntimeObjectResArray(
            Dictionary<string, ObjectResInfo> objectInfos)
        {
            List<string> uuids = objectInfos.Keys.ToList();
            uuids.Sort(StringComparer.Ordinal);

            RuntimeGameObjectRes[] result = new RuntimeGameObjectRes[uuids.Count];
            for (int i = 0; i < uuids.Count; i++)
            {
                string uuid = uuids[i];
                result[i] = new RuntimeGameObjectRes
                {
                    uuid = uuid,
                    resIDArray = objectInfos[uuid].ResourceIds.Distinct().ToArray()
                };
            }

            return result;
        }

        private void SaveCutData(
            string sceneName,
            RuntimeSceneData data,
            Dictionary<string, ObjectResInfo> objectInfos)
        {
            string folder = ResourceRootAsset + "/" + sceneName;
            Directory.CreateDirectory(folder);

            string dataPath = folder + "/" + sceneName + "_data.bytes";
            File.WriteAllText(dataPath, JsonUtility.ToJson(data));

            DebugData debugData = new DebugData
            {
                cubeSize = _cubeSize,
                cubes = data.cubeList.Select(c => new DebugCube
                {
                    pos = c.pos,
                    uuids = c.uuidArray
                }).ToList(),
                objects = objectInfos.Select(kv => new DebugObject
                {
                    uuid = kv.Key,
                    nodePath = kv.Value.nodePath,
                    meshes = kv.Value.ResourceIds
                        .Where(id => IsMeshResource(data.pathArray[id]))
                        .Select(id => data.pathArray[id]).ToArray(),
                    materials = kv.Value.ResourceIds
                        .Where(id => !IsMeshResource(data.pathArray[id]))
                        .Select(id => data.pathArray[id]).ToArray()
                }).ToList()
            };

            string debugPath = folder + "/" + sceneName + "_debug_data.bytes";
            File.WriteAllText(debugPath, JsonUtility.ToJson(debugData));

            Debug.Log("[场景切割] 数据已保存: " + dataPath);
            Debug.Log("[场景切割] 调试数据已保存: " + debugPath);
        }

        private static bool IsMeshResource(string resourcePath)
        {
            return resourcePath.IndexOf("/Mesh/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   resourcePath.IndexOf("/Meshes/", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string ToResourcePath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                return null;
            }

            string normalized = assetPath.Replace('\\', '/');
            const string marker = "/Resources/";
            int index = normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return null;
            }

            string relative = normalized.Substring(index + marker.Length);
            int dot = relative.LastIndexOf('.');
            if (dot > 0)
            {
                relative = relative.Substring(0, dot);
            }

            return relative;
        }

        private string EnsureMeshAsset(Mesh mesh, string sceneName)
        {
            string sourcePath = AssetDatabase.GetAssetPath(mesh);
            string resourcePath = ToResourcePath(sourcePath);

            // 已经是 Resources 下的 .asset 网格，可以直接加载。
            if (!string.IsNullOrEmpty(resourcePath) &&
                sourcePath.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
            {
                return resourcePath;
            }

            string meshFolder;
            if (!string.IsNullOrEmpty(sourcePath))
            {
                string directory = Path.GetDirectoryName(sourcePath)?.Replace('\\', '/');
                if (!string.IsNullOrEmpty(directory) &&
                    directory.EndsWith("/Model", StringComparison.OrdinalIgnoreCase))
                {
                    meshFolder = directory.Substring(0, directory.Length - "Model".Length) + "Mesh";
                }
                else if (!string.IsNullOrEmpty(directory) &&
                         directory.EndsWith("/Meshes", StringComparison.OrdinalIgnoreCase))
                {
                    meshFolder = directory;
                }
                else
                {
                    meshFolder = ResourceRootAsset + "/" + sceneName + "/Mesh";
                }
            }
            else
            {
                meshFolder = ResourceRootAsset + "/" + sceneName + "/Mesh";
            }

            string targetPath = meshFolder + "/" + SanitizeFileName(mesh.name) + ".asset";
            if (File.Exists(targetPath))
            {
                return ToResourcePath(targetPath);
            }

            Directory.CreateDirectory(meshFolder);
            AssetDatabase.Refresh();

            Mesh clone = Object.Instantiate(mesh);
            clone.name = mesh.name;
            AssetDatabase.CreateAsset(clone, targetPath);
            AssetDatabase.SaveAssets();

            Debug.Log("[场景切割] 已提取 Mesh: " + targetPath);
            return ToResourcePath(targetPath);
        }

        private string CopyMaterialToResources(Material material, string sourcePath, string sceneName)
        {
            if (string.IsNullOrEmpty(sourcePath))
            {
                return null;
            }

            string folder = ResourceRootAsset + "/" + sceneName + "/Materials";
            Directory.CreateDirectory(folder);
            AssetDatabase.Refresh();

            string targetPath = folder + "/" + SanitizeFileName(material.name) + ".mat";
            if (!File.Exists(targetPath))
            {
                if (!AssetDatabase.CopyAsset(sourcePath, targetPath))
                {
                    return null;
                }
            }

            return ToResourcePath(targetPath);
        }

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return "unnamed";
            }

            char[] invalid = Path.GetInvalidFileNameChars();
            char[] chars = name.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (Array.IndexOf(invalid, chars[i]) >= 0)
                {
                    chars[i] = '_';
                }
            }

            return new string(chars);
        }

        private List<GameObject> AddMeshColliders()
        {
            List<GameObject> added = new List<GameObject>();
            foreach (string nodeName in SceneResLoadOnDemand.nodeNames)
            {
                GameObject root = GameObject.Find(nodeName);
                if (root == null)
                {
                    continue;
                }

                AddMeshCollidersRecursive(root.transform, added);
            }

            return added;
        }

        private static void AddMeshCollidersRecursive(Transform transform, List<GameObject> added)
        {
            GameObject go = transform.gameObject;
            if (!ShouldSkip(go))
            {
                if (go.GetComponent<MeshRenderer>() != null && go.GetComponent<MeshCollider>() == null)
                {
                    go.AddComponent<MeshCollider>();
                    added.Add(go);
                }
            }

            for (int i = 0; i < transform.childCount; i++)
            {
                AddMeshCollidersRecursive(transform.GetChild(i), added);
            }
        }

        private static void RemoveMeshColliders(List<GameObject> added)
        {
            foreach (GameObject go in added)
            {
                if (go == null)
                {
                    continue;
                }

                MeshCollider collider = go.GetComponent<MeshCollider>();
                if (collider != null)
                {
                    Object.DestroyImmediate(collider);
                }
            }
        }

        private static bool ShouldSkip(GameObject go)
        {
            if (go == null)
            {
                return true;
            }

            string name = go.name.ToLowerInvariant();
            foreach (string prefix in SkipNamePrefixes)
            {
                if (name.StartsWith(prefix))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsSkippedMaterial(Material material)
        {
            string name = material.name.ToLowerInvariant();
            return name.StartsWith("terr") || name.StartsWith("water");
        }

        private static bool IsSkippedMesh(Mesh mesh, GameObject go)
        {
            string name = go.name.ToLowerInvariant();
            if (name.StartsWith("terr") || name.StartsWith("water"))
            {
                return true;
            }

            string meshName = mesh.name.ToLowerInvariant();
            return meshName.StartsWith("terr") || meshName.StartsWith("water");
        }

        private void LoadCurrentSceneCutData()
        {
            string sceneName = SceneResLoadOnDemand.GetNameByCurScene();
            string dataPath = ResourceRootAsset + "/" + sceneName + "/" + sceneName + "_data.bytes";
            TextAsset asset = AssetDatabase.LoadAssetAtPath<TextAsset>(dataPath);
            if (asset == null)
            {
                EditorUtility.DisplayDialog("读取失败", "找不到切割数据:\n" + dataPath, "确定");
                return;
            }

            RuntimeSceneData data = JsonUtility.FromJson<RuntimeSceneData>(asset.text);
            if (data == null || data.cubeList == null)
            {
                EditorUtility.DisplayDialog("读取失败", "切割数据格式错误:\n" + dataPath, "确定");
                return;
            }

            _previewCubes.Clear();
            _previewCubes.AddRange(data.cubeList);
            _lastCutScene = sceneName;

            Debug.Log("[场景切割] 读取到 " + data.cubeList.Length + " 个方块: " + dataPath);
        }

        private void OnSceneGui(SceneView sceneView)
        {
            if (!_showCutCubes || _previewCubes.Count == 0)
            {
                return;
            }

            Handles.color = new Color(0.2f, 1f, 0.35f, 0.7f);
            Vector3 size = Vector3.one * _cubeSize;
            for (int i = 0; i < _previewCubes.Count; i++)
            {
                Handles.DrawWireCube(_previewCubes[i].pos, size);
            }
        }

        [Serializable]
        private class ObjectResInfo
        {
            public string nodePath;
            public readonly List<int> ResourceIds = new List<int>();
        }

        [Serializable]
        private class DebugData
        {
            public int cubeSize;
            public List<DebugCube> cubes;
            public List<DebugObject> objects;
        }

        [Serializable]
        private class DebugCube
        {
            public Vector3 pos;
            public string[] uuids;
        }

        [Serializable]
        private class DebugObject
        {
            public string uuid;
            public string nodePath;
            public string[] meshes;
            public string[] materials;
        }
    }
}
