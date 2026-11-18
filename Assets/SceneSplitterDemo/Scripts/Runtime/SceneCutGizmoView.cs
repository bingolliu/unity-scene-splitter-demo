using UnityEngine;

namespace SceneSplitterDemo
{
    /// <summary>
    /// 在 Scene 视图 / Game 视图的 Gizmos 中显示当前参与加载的方块范围。
    /// </summary>
    public class SceneCutGizmoView : MonoBehaviour
    {
        public bool drawAllCutCubes = false;
        public bool drawVisibleCubes = true;

        private void OnDrawGizmos()
        {
            RuntimeSceneData data = SceneResLoadOnDemand.runtimeSceneData;
            if (data == null)
            {
                return;
            }

            if (drawVisibleCubes)
            {
                Gizmos.color = new Color(0.2f, 1f, 0.35f, 0.85f);
                for (int i = 0; i < SceneResLoadOnDemand.visiableCubeList.Count; i++)
                {
                    RuntimeCube cube = SceneResLoadOnDemand.visiableCubeList[i];
                    Gizmos.DrawWireCube(cube.pos, Vector3.one * SceneResLoadOnDemand.cubeSize);
                }
            }

            if (drawAllCutCubes && data.cubeDict != null)
            {
                Gizmos.color = new Color(1f, 1f, 1f, 0.15f);
                foreach (RuntimeCube cube in data.cubeDict.Values)
                {
                    Gizmos.DrawWireCube(cube.pos, Vector3.one * SceneResLoadOnDemand.cubeSize);
                }
            }

            Transform hero = SceneResLoadOnDemand.GetTrsHero();
            if (hero != null)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireSphere(hero.position, 1.5f);
            }
        }
    }
}
