using UnityEngine;
using UnityEngine.SceneManagement;

namespace SceneSplitterDemo
{
    /// <summary>
    /// 演示工程入口：
    /// 打开 map35 场景直接 Play 即可。启动后创建相机、英雄、HUD，
    /// 然后调用 SceneResLoadOnDemand.OnSceneStart() 开始按需加载场景资源。
    /// </summary>
    public static class DemoAutoBoot
    {
        private static readonly Vector3 FallbackSpawn = new Vector3(0f, 3f, 0f);
        private static readonly Color HeroColor = new Color(1f, 0.55f, 0.15f, 1f);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (Application.isBatchMode)
            {
                return;
            }

            Scene activeScene = SceneManager.GetActiveScene();
            if (!activeScene.IsValid())
            {
                return;
            }

            // 只有存在切割数据的场景才自动启动，避免影响工程里其它场景。
            string dataPath = SceneResLoadOnDemand.GetDataResourcePath(activeScene.name);
            TextAsset cutData = Resources.Load<TextAsset>(dataPath);
            if (cutData == null)
            {
                return;
            }

            if (Object.FindAnyObjectByType<SceneResLoadOnDemand>() != null)
            {
                return;
            }

            RuntimeSceneData probe = JsonUtility.FromJson<RuntimeSceneData>(cutData.text);
            Vector3 spawn = probe != null ? GetSpawnPoint(probe) : FallbackSpawn;
            Resources.UnloadAsset(cutData);

            Camera camera = CreateCamera();
            Transform hero = CreateHero(spawn, camera.transform);

            new GameObject("SceneSplitterDemo/HUD").AddComponent<DemoHud>();
            new GameObject("SceneSplitterDemo/CubeGizmos").AddComponent<SceneCutGizmoView>();

            GameObject loaderObject = new GameObject("SceneSplitterDemo/SceneResLoadOnDemand");
            loaderObject.AddComponent<SceneResLoadOnDemand>();

            SceneResLoadOnDemand.SetTrsHero(hero);
            SceneResLoadOnDemand.OnSceneStart();
        }

        private static Vector3 GetSpawnPoint(RuntimeSceneData data)
        {
            int size = data.cubeSize > 0 ? data.cubeSize : SceneResLoadOnDemand.cubeSize;

            // 从地图一角开始，向中心移动时可以看到更多方块和资源被加载。
            if (data.minArrayIndexX == 0 && data.maxArrayIndexX == 0 &&
                data.minArrayIndexZ == 0 && data.maxArrayIndexZ == 0)
            {
                return FallbackSpawn;
            }

            float spawnX = data.maxArrayIndexX - size;
            float spawnZ = data.maxArrayIndexZ - size;
            return new Vector3(spawnX, 3f, spawnZ);
        }

        private static Camera CreateCamera()
        {
            GameObject cameraObject = new GameObject("Demo Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0f, 12f, -18f);

            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 500f;
            camera.fieldOfView = 55f;

            cameraObject.AddComponent<AudioListener>();
            return camera;
        }

        private static Transform CreateHero(Vector3 spawn, Transform cameraTransform)
        {
            GameObject hero = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            hero.name = "Demo Hero";
            hero.transform.position = spawn;

            Renderer renderer = hero.GetComponent<Renderer>();
            if (renderer != null)
            {
                Shader shader = Shader.Find("Standard");
                if (shader == null)
                {
                    shader = Shader.Find("Unlit/Color");
                }

                if (shader != null)
                {
                    Material material = new Material(shader);
                    material.color = HeroColor;
                    renderer.sharedMaterial = material;
                }
            }

            DemoHeroController controller = hero.AddComponent<DemoHeroController>();
            controller.cameraTransform = cameraTransform;

            return hero.transform;
        }
    }
}
