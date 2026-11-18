using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace SceneSplitterDemo
{
    /// <summary>
    /// 场景资源按需加载（空间方块流式加载）。
    ///
    /// 流程：
    /// 1. 场景加载完成后调用 OnSceneStart()，异步读取该场景的切割数据；
    /// 2. 设置英雄 Transform（SetTrsHero）后，每 6 帧检查一次英雄是否移动；
    /// 3. 以英雄所在方块为中心，取 viewGrid 范围内的九宫格方块；
    /// 4. 对这些方块里的 uuid 收集 mesh / material 资源，异步加载；
    /// 5. 资源到齐后把物体 SetActive(true) 并回填 mesh / material；
    /// 6. 离开范围的物体隐藏，不再被引用的资源卸载。
    ///
    /// 资源加载走 Resources.LoadAsync，卸载走 Resources.UnloadAsset；
    /// 生产环境可替换为 AssetBundle / Addressables，数据结构保持不变。
    /// </summary>
    public class SceneResLoadOnDemand : MonoBehaviour
    {
        #region 常量 / 配置

        /// <summary>
        /// 参与流式加载的根节点名称。物体 uuid 由层级索引拼成，例如 Scene/Item/X -> 0_1_0。
        /// </summary>
        public static List<string> nodeNames = new List<string> { "Scene", "Effect", "Animation" };

        /// <summary>
        /// 切割数据的 Resources 根目录。
        /// 资源路径形如：SceneRes/map35/map35_data
        /// </summary>
        public static string ResourceRoot = "SceneRes";

        /// <summary>
        /// 方块大小，和切割工具保持一致。场景数据里保存了该值时会自动覆盖。
        /// </summary>
        public static int cubeSize = 10;

        /// <summary>
        /// 英雄移动多少距离后重新计算可见方块。
        /// </summary>
        public static float max_hero_delta_pos = 3f;

        /// <summary>
        /// 以英雄所在方块为中心，向各方向扩展的方块数量。
        /// 1 表示 3x3 个方块（按 X/Z 计算），可按机器性能调整。
        /// </summary>
        public static int viewGrid = 1;

        public static int viewGridAddX = 0;
        public static int viewGridAddY = 0;
        public static int viewGridAddZ = 0;

        #endregion

        #region 运行时状态

        private static bool isCheck = false;
        private static bool isFirstOpenScene = false;

        private static Transform trsHero = null;
        private static Vector3 lastestHeroPos = Vector3.zero;
        private static int frameIndex = 0;

        private static int sessionId = 0;

        public static RuntimeSceneData runtimeSceneData = null;

        // uuid -> GameObject / Renderer / MeshFilter
        public static Dictionary<string, GameObject> uuid2ObjDictionary = new Dictionary<string, GameObject>();
        public static Dictionary<string, MeshRenderer> uuid2MeshRenderDictionary = new Dictionary<string, MeshRenderer>();
        public static Dictionary<string, MeshFilter> uuid2MeshFilterDictionary = new Dictionary<string, MeshFilter>();

        // resID -> 已加载资源
        public static Dictionary<int, Object> hadDownLoadOrLoadPathList = new Dictionary<int, Object>();

        private static readonly Dictionary<int, ResourceRequest> pendingRequests = new Dictionary<int, ResourceRequest>();
        private static readonly HashSet<int> curNeededResIDs = new HashSet<int>();

        private static readonly HashSet<short> lastDownloadOrLoadPathList = new HashSet<short>();
        private static readonly HashSet<short> curDownloadOrLoadPathList = new HashSet<short>();
        private static readonly HashSet<short> notMoreNeedDownloadOrLoadPathList = new HashSet<short>();

        private static readonly HashSet<string> curUuidList = new HashSet<string>();
        private static readonly HashSet<string> lastUuidList = new HashSet<string>();

        public static List<RuntimeCube> visiableCubeList = new List<RuntimeCube>();

        public enum ResUpdateState
        {
            None = 1,
            StartLoadSceneCutData = 2,
            WaitingLoadSceneCutData = 3,
            EndLoadSceneCutData = 4,
        }

        public static ResUpdateState resUpdateState = ResUpdateState.None;

        public enum ECalcCubeListStage
        {
            None = 0,
            StartCalcCameraViewRange = 1,
            StartCalcDistanceRange = 2,
            StartDistanceSort = 3,
            StartDownload = 4,
        }

        public static ECalcCubeListStage eCalcCubeListStage = ECalcCubeListStage.None;

        #endregion

        #region MonoBehaviour

        void Update()
        {
            CheckResUpdateState();

            if (!isCheck || trsHero == null)
            {
                return;
            }

            CheckUpdateCubeListState();
            CheckCalcCubeListStage();
        }

        void OnDestroy()
        {
            OnSceneEnd();
        }

        #endregion

        #region 对外接口

        public static void SetIsCheck(bool flag)
        {
            isCheck = flag;
        }

        public static bool GetIsCheck()
        {
            return isCheck;
        }

        public static void SetTrsHero(Transform trs)
        {
            trsHero = trs;
        }

        public static Transform GetTrsHero()
        {
            return trsHero;
        }

        public static void SetResUpdateState(ResUpdateState state)
        {
            resUpdateState = state;
        }

        public static void SetCalcListStatus(ECalcCubeListStage stage)
        {
            eCalcCubeListStage = stage;
        }

        public static string GetDataResourcePath(string sceneName)
        {
            return string.Format("{0}/{1}/{1}_data", ResourceRoot, sceneName);
        }

        public static string GetNameByCurScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!string.IsNullOrEmpty(scene.path))
            {
                string path = scene.path.Replace('\\', '/');
                int index = path.LastIndexOf('/');
                string fileName = index >= 0 ? path.Substring(index + 1) : path;
                return fileName.Replace(".unity", string.Empty);
            }

            return scene.name;
        }

        /// <summary>
        /// 进入场景时调用（演示工程由 DemoAutoBoot 自动触发；接入业务时在场景加载完成后调用）。
        /// </summary>
        public static void OnSceneStart()
        {
            sessionId++;

            isFirstOpenScene = true;
            runtimeSceneData = null;
            hadDownLoadOrLoadPathList.Clear();
            pendingRequests.Clear();
            curNeededResIDs.Clear();
            visiableCubeList.Clear();
            curUuidList.Clear();
            lastUuidList.Clear();
            lastDownloadOrLoadPathList.Clear();
            curDownloadOrLoadPathList.Clear();
            notMoreNeedDownloadOrLoadPathList.Clear();

            RefreshUuid2ObjDictionary(false);
            SetResUpdateState(ResUpdateState.StartLoadSceneCutData);

            Debug.Log("[SceneResLoadOnDemand] OnSceneStart: " + GetNameByCurScene());
        }

        public static void OnSceneEnd()
        {
            sessionId++;

            isCheck = false;
            eCalcCubeListStage = ECalcCubeListStage.None;
            resUpdateState = ResUpdateState.None;

            foreach (KeyValuePair<int, Object> kv in hadDownLoadOrLoadPathList)
            {
                if (kv.Value != null)
                {
                    Resources.UnloadAsset(kv.Value);
                }
            }

            hadDownLoadOrLoadPathList.Clear();
            pendingRequests.Clear();
            curNeededResIDs.Clear();
            visiableCubeList.Clear();

            trsHero = null;
            lastestHeroPos = Vector3.zero;
            runtimeSceneData = null;

            uuid2ObjDictionary.Clear();
            uuid2MeshRenderDictionary.Clear();
            uuid2MeshFilterDictionary.Clear();

            curUuidList.Clear();
            lastUuidList.Clear();
            lastDownloadOrLoadPathList.Clear();
            curDownloadOrLoadPathList.Clear();
            notMoreNeedDownloadOrLoadPathList.Clear();

            Debug.Log("[SceneResLoadOnDemand] OnSceneEnd");
        }

        #endregion

        #region 状态机

        public static void CheckResUpdateState()
        {
            switch (resUpdateState)
            {
                case ResUpdateState.StartLoadSceneCutData:
                    SetResUpdateState(ResUpdateState.WaitingLoadSceneCutData);
                    StartLoadSceneCutData();
                    break;

                case ResUpdateState.EndLoadSceneCutData:
                    SetIsCheck(true);
                    break;
            }
        }

        private static void StartLoadSceneCutData()
        {
            string sceneName = GetNameByCurScene();
            string path = GetDataResourcePath(sceneName);
            int sid = sessionId;

            ResourceRequest request = Resources.LoadAsync<TextAsset>(path);
            request.completed += operation =>
            {
                if (sid != sessionId)
                {
                    return;
                }

                ResourceRequest finished = operation as ResourceRequest;
                TextAsset textAsset = finished != null ? finished.asset as TextAsset : null;
                EndLoadSceneCutData(textAsset);
            };
        }

        /// <summary>
        /// 切割数据加载完成。
        /// </summary>
        public static void EndLoadSceneCutData(TextAsset textAsset)
        {
            string sceneName = GetNameByCurScene();
            string path = GetDataResourcePath(sceneName);

            if (textAsset == null)
            {
                Debug.LogError(
                    "[SceneResLoadOnDemand] 找不到切割数据: Resources/" + path +
                    "。请先执行菜单 Tools/场景切割与按需加载/切割窗口，对场景执行一次切割。");
                SetResUpdateState(ResUpdateState.None);
                return;
            }

            if (runtimeSceneData != null)
            {
                return;
            }

            runtimeSceneData = JsonUtility.FromJson<RuntimeSceneData>(textAsset.text);
            if (runtimeSceneData == null)
            {
                Debug.LogError("[SceneResLoadOnDemand] 切割数据解析失败: " + path);
                SetResUpdateState(ResUpdateState.None);
                return;
            }

            runtimeSceneData.InitDict();

            if (runtimeSceneData.cubeSize > 0)
            {
                cubeSize = runtimeSceneData.cubeSize;
            }

            // 数据就绪后重新建立 uuid 字典，并把参与流式加载的物体先隐藏。
            RefreshUuid2ObjDictionary(true);

            Debug.Log(string.Format(
                "[SceneResLoadOnDemand] 切割数据加载完成: {0} 个方块, {1} 个物体, cubeSize={2}",
                runtimeSceneData.cubeDict.Count,
                runtimeSceneData.uuid2RuntimeGameObjectRes.Count,
                cubeSize));

            SetResUpdateState(ResUpdateState.EndLoadSceneCutData);
        }

        #endregion

        #region uuid 与层级

        private static void RefreshUuid2ObjDictionary(bool isRuntime)
        {
            uuid2ObjDictionary.Clear();
            uuid2MeshRenderDictionary.Clear();
            uuid2MeshFilterDictionary.Clear();

            for (int i = 0; i < nodeNames.Count; i++)
            {
                GameObject root = GameObject.Find(nodeNames[i]);
                if (root != null)
                {
                    CalcUUID(root.transform, i.ToString(), isRuntime);
                }
            }
        }

        /// <summary>
        /// 重建 uuid 字典（isRuntime 为 true 时同时缓存 Renderer / MeshFilter 并隐藏物体）。
        /// </summary>
        public static void InitUuid2ObjDictionary(bool isRuntime = true)
        {
            RefreshUuid2ObjDictionary(isRuntime);
        }

        private static void CalcUUID(Transform root, string index, bool isRuntime)
        {
            if (root == null)
            {
                return;
            }

            string lowerName = root.gameObject.name.ToLowerInvariant();
            if (lowerName.StartsWith("terr") ||
                lowerName.StartsWith("water") ||
                lowerName.StartsWith("npc") ||
                lowerName.StartsWith("fx") ||
                (lowerName.StartsWith("anim") && root.parent != null))
            {
                return;
            }

            if (!isRuntime && !root.gameObject.activeSelf)
            {
                return;
            }

            MeshRenderer meshRenderer = root.GetComponent<MeshRenderer>();
            if (meshRenderer != null)
            {
                if (isRuntime)
                {
                    if (runtimeSceneData != null &&
                        !runtimeSceneData.uuid2RuntimeGameObjectRes.ContainsKey(index))
                    {
                        return;
                    }

                    uuid2MeshRenderDictionary[index] = meshRenderer;

                    MeshFilter meshFilter = root.GetComponent<MeshFilter>();
                    if (meshFilter != null)
                    {
                        uuid2MeshFilterDictionary[index] = meshFilter;
                    }

                    uuid2ObjDictionary[index] = root.gameObject;
                    root.gameObject.SetActive(false);
                }
                else
                {
                    uuid2ObjDictionary[index] = root.gameObject;
                }
            }

            for (int childIndex = 0; childIndex < root.childCount; childIndex++)
            {
                CalcUUID(root.GetChild(childIndex), index + "_" + childIndex, isRuntime);
            }
        }

        public static string GetPathToParentByRecursion(Transform trs)
        {
            if (trs == null)
            {
                return string.Empty;
            }

            return trs.parent == null
                ? trs.name
                : GetPathToParentByRecursion(trs.parent) + "/" + trs.name;
        }

        #endregion

        #region 方块索引

        public static int FloorToIndex(float x)
        {
            if (cubeSize <= 0)
            {
                return (int)x;
            }

            float remainder = x % cubeSize;
            if (remainder == 0)
            {
                return (int)x;
            }

            return remainder > (cubeSize / 2f)
                ? (int)(x + (cubeSize - remainder))
                : (int)(x - remainder);
        }

        /// <summary>
        /// 三维坐标转一维 key。size 必须大于坐标绝对值，默认 1000。
        /// </summary>
        public static int ThreeD2index(int x, int y, int z, int size = 1000)
        {
            return x * size * size + y * size + z;
        }

        public static void Index2ThreeD(int i, ref int xx, ref int yy, ref int zz, int size = 1000)
        {
            zz = i % size;
            yy = (i - zz) / size % size;
            xx = i / (size * size);
        }

        public static void StartUpdateCubeList()
        {
            SetCalcListStatus(ECalcCubeListStage.StartCalcCameraViewRange);
        }

        public static void Get3D9Grid(Vector3 pos, Dictionary<int, RuntimeCube> cubeArray3D)
        {
            visiableCubeList.Clear();

            int arrayIndexX = FloorToIndex(pos.x);
            int arrayIndexY = FloorToIndex(pos.y);
            int arrayIndexZ = FloorToIndex(pos.z);

            int maxSizeX = cubeSize * (viewGrid + viewGridAddX);
            int maxSizeY = cubeSize * (viewGrid + viewGridAddY);
            int maxSizeZ = cubeSize * (viewGrid + viewGridAddZ);

            for (int x = arrayIndexX - maxSizeX; x <= arrayIndexX + maxSizeX; x += cubeSize)
            {
                for (int y = arrayIndexY - maxSizeY; y <= arrayIndexY + maxSizeY; y += cubeSize)
                {
                    for (int z = arrayIndexZ - maxSizeZ; z <= arrayIndexZ + maxSizeZ; z += cubeSize)
                    {
                        int index = ThreeD2index(x, y, z);
                        if (cubeArray3D.TryGetValue(index, out RuntimeCube cube))
                        {
                            visiableCubeList.Add(cube);
                        }
                    }
                }
            }

            Vector3 heroPos = trsHero != null ? trsHero.position : pos;
            visiableCubeList.Sort(delegate (RuntimeCube a, RuntimeCube b)
            {
                float distanceA = Vector3.Distance(a.pos, heroPos);
                float distanceB = Vector3.Distance(b.pos, heroPos);
                return distanceA < distanceB ? -1 : 1;
            });
        }

        public static void CheckCalcCubeListStage()
        {
            switch (eCalcCubeListStage)
            {
                case ECalcCubeListStage.StartCalcCameraViewRange:
                    Get3D9Grid(trsHero.position, runtimeSceneData.cubeDict);
                    SetCalcListStatus(ECalcCubeListStage.StartDownload);
                    break;

                case ECalcCubeListStage.StartDownload:
                    StartDownloadOrLoad();
                    SetCalcListStatus(ECalcCubeListStage.None);
                    break;
            }
        }

        private static void CheckUpdateCubeListState()
        {
            frameIndex++;
            if (frameIndex < 6)
            {
                return;
            }

            frameIndex = 0;

            if (isFirstOpenScene || Vector3.Distance(lastestHeroPos, trsHero.position) > max_hero_delta_pos)
            {
                isFirstOpenScene = false;
                lastestHeroPos = trsHero.position;
                StartUpdateCubeList();
            }

            CheckRefreshOnResLoaded();
        }

        #endregion

        #region 计算需要的资源

        private static void StartLoopLastestCubeList()
        {
            curDownloadOrLoadPathList.Clear();
            curUuidList.Clear();
            curNeededResIDs.Clear();

            for (int i = 0; i < visiableCubeList.Count; i++)
            {
                RuntimeCube cube = visiableCubeList[i];
                if (cube == null || cube.uuidArray == null)
                {
                    continue;
                }

                for (int j = 0; j < cube.uuidArray.Length; j++)
                {
                    string uuid = cube.uuidArray[j];
                    curUuidList.Add(uuid);

                    int[] resIDArray = runtimeSceneData.GetResIDArrayByUUID(uuid);
                    if (resIDArray == null)
                    {
                        continue;
                    }

                    for (int k = 0; k < resIDArray.Length; k++)
                    {
                        curDownloadOrLoadPathList.Add((short)resIDArray[k]);
                        curNeededResIDs.Add(resIDArray[k]);
                    }
                }
            }

            // 离开范围的物体隐藏
            foreach (string uuid in lastUuidList)
            {
                if (curUuidList.Contains(uuid))
                {
                    continue;
                }

                if (uuid2ObjDictionary.TryGetValue(uuid, out GameObject go) && go != null)
                {
                    go.SetActive(false);
                }
            }

            lastUuidList.Clear();
            lastUuidList.UnionWith(curUuidList);

            notMoreNeedDownloadOrLoadPathList.Clear();
            foreach (short resID in lastDownloadOrLoadPathList)
            {
                if (!curDownloadOrLoadPathList.Contains(resID))
                {
                    notMoreNeedDownloadOrLoadPathList.Add(resID);
                }
            }

            lastDownloadOrLoadPathList.Clear();
            lastDownloadOrLoadPathList.UnionWith(curDownloadOrLoadPathList);
        }

        private static void StartDownloadOrLoad()
        {
            StartLoopLastestCubeList();

            foreach (short resIDShort in curDownloadOrLoadPathList)
            {
                int resID = resIDShort;
                if (hadDownLoadOrLoadPathList.ContainsKey(resID) || pendingRequests.ContainsKey(resID))
                {
                    continue;
                }

                string path = runtimeSceneData.GetPath(resID);
                if (string.IsNullOrEmpty(path))
                {
                    continue;
                }

                string finalPath = ResourceRoot + "/" + path;
                int sid = sessionId;

                ResourceRequest request = Resources.LoadAsync<Object>(finalPath);
                pendingRequests[resID] = request;
                request.completed += operation =>
                {
                    if (sid != sessionId)
                    {
                        return;
                    }

                    ResourceRequest finished = operation as ResourceRequest;
                    OnResourceLoaded(resID, finalPath, finished != null ? finished.asset : null);
                };
            }

            foreach (short resIDShort in notMoreNeedDownloadOrLoadPathList)
            {
                int resID = resIDShort;

                if (pendingRequests.ContainsKey(resID))
                {
                    // ResourceRequest 不能取消，加载完成回调里会根据 curNeededResIDs 决定是否卸载。
                    continue;
                }

                if (hadDownLoadOrLoadPathList.TryGetValue(resID, out Object asset))
                {
                    hadDownLoadOrLoadPathList.Remove(resID);
                    if (asset != null)
                    {
                        Resources.UnloadAsset(asset);
                    }
                }
            }
        }

        private static void OnResourceLoaded(int resID, string path, Object asset)
        {
            pendingRequests.Remove(resID);

            if (asset == null)
            {
                Debug.LogError("[SceneResLoadOnDemand] 资源加载失败: Resources/" + path);
                return;
            }

            if (curNeededResIDs.Contains(resID))
            {
                hadDownLoadOrLoadPathList[resID] = asset;
            }
            else
            {
                Resources.UnloadAsset(asset);
            }
        }

        public static bool isLoadedSucc(RuntimeGameObjectRes res)
        {
            if (res == null || res.resIDArray == null)
            {
                return false;
            }

            for (int i = 0; i < res.resIDArray.Length; i++)
            {
                if (!hadDownLoadOrLoadPathList.ContainsKey(res.resIDArray[i]))
                {
                    return false;
                }
            }

            return true;
        }

        public static void CheckRefreshOnResLoaded()
        {
            for (int i = 0; i < visiableCubeList.Count; i++)
            {
                RuntimeCube cube = visiableCubeList[i];
                if (cube == null || cube.uuidArray == null)
                {
                    continue;
                }

                for (int j = 0; j < cube.uuidArray.Length; j++)
                {
                    string uuid = cube.uuidArray[j];
                    RuntimeGameObjectRes res = runtimeSceneData.GetRuntimeGameObjectResByUUID(uuid);
                    if (isLoadedSucc(res))
                    {
                        RefreshByUUID(uuid);
                    }
                }
            }
        }

        public static void RefreshByUUID(string uuid)
        {
            if (!uuid2ObjDictionary.TryGetValue(uuid, out GameObject go) || go == null || go.activeSelf)
            {
                return;
            }

            RuntimeGameObjectRes res = runtimeSceneData.GetRuntimeGameObjectResByUUID(uuid);
            if (res == null)
            {
                return;
            }

            Mesh mesh = null;
            List<Material> materials = new List<Material>();

            for (int i = 0; i < res.resIDArray.Length; i++)
            {
                if (!hadDownLoadOrLoadPathList.TryGetValue(res.resIDArray[i], out Object asset) || asset == null)
                {
                    return;
                }

                if (asset is Mesh loadedMesh)
                {
                    mesh = loadedMesh;
                }
                else if (asset is Material loadedMaterial)
                {
                    materials.Add(loadedMaterial);
                }
            }

            if (mesh != null &&
                uuid2MeshFilterDictionary.TryGetValue(uuid, out MeshFilter meshFilter) &&
                meshFilter != null)
            {
                meshFilter.sharedMesh = mesh;
            }

            if (materials.Count > 0 &&
                uuid2MeshRenderDictionary.TryGetValue(uuid, out MeshRenderer meshRenderer) &&
                meshRenderer != null)
            {
                meshRenderer.sharedMaterials = materials.ToArray();
            }

            go.SetActive(true);
        }

        #endregion

        #region 统计信息（HUD 使用）

        public static int LoadedAssetCount
        {
            get { return hadDownLoadOrLoadPathList.Count; }
        }

        public static int PendingAssetCount
        {
            get { return pendingRequests.Count; }
        }

        public static int VisibleCubeCount
        {
            get { return visiableCubeList.Count; }
        }

        public static int StreamedObjectCount
        {
            get { return uuid2ObjDictionary.Count; }
        }

        public static int ActiveStreamedObjectCount
        {
            get
            {
                int count = 0;
                foreach (KeyValuePair<string, GameObject> kv in uuid2ObjDictionary)
                {
                    if (kv.Value != null && kv.Value.activeSelf)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        #endregion
    }
}
