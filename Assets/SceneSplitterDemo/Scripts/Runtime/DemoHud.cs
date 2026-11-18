using UnityEngine;

namespace SceneSplitterDemo
{
    /// <summary>
    /// 演示 HUD：显示操作方式与实时流式加载统计。
    /// 使用英文标签，避免不同平台默认 GUI 字体缺少中文字形。
    /// </summary>
    public class DemoHud : MonoBehaviour
    {
        private GUIStyle _titleStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _panelStyle;

        private void EnsureStyles()
        {
            if (_panelStyle != null)
            {
                return;
            }

            _panelStyle = new GUIStyle(GUI.skin.box);
            _panelStyle.padding = new RectOffset(14, 14, 10, 10);

            _titleStyle = new GUIStyle(GUI.skin.label);
            _titleStyle.fontSize = 16;
            _titleStyle.fontStyle = FontStyle.Bold;

            _labelStyle = new GUIStyle(GUI.skin.label);
            _labelStyle.fontSize = 13;
        }

        private void OnGUI()
        {
            EnsureStyles();

            GUILayout.BeginArea(new Rect(16, 16, 440, 270), _panelStyle);
            GUILayout.Label("Scene Split + On-Demand Loading", _titleStyle);

            if (SceneResLoadOnDemand.runtimeSceneData == null)
            {
                GUILayout.Label("Loading cut data...", _labelStyle);
                if (SceneResLoadOnDemand.resUpdateState == SceneResLoadOnDemand.ResUpdateState.None)
                {
                    GUILayout.Label(
                        "Cut data not found. Open Tools > 场景切割与按需加载 > 切割窗口 and cut this scene.",
                        _labelStyle);
                }

                GUILayout.EndArea();
                return;
            }

            GUILayout.Label("WASD / Arrow keys: move    Shift: sprint", _labelStyle);
            GUILayout.Label("1 / 2 / 3: change viewGrid radius", _labelStyle);
            GUILayout.Space(4);

            GUILayout.Label("Position: " + FormatPosition(), _labelStyle);
            GUILayout.Label("viewGrid radius: " + SceneResLoadOnDemand.viewGrid, _labelStyle);
            GUILayout.Label("Visible cubes: " + SceneResLoadOnDemand.VisibleCubeCount, _labelStyle);
            GUILayout.Label("Total cut cubes: " + SceneResLoadOnDemand.runtimeSceneData.cubeDict.Count, _labelStyle);
            GUILayout.Label(
                "Streamed objects: " + SceneResLoadOnDemand.ActiveStreamedObjectCount + " / " +
                SceneResLoadOnDemand.StreamedObjectCount,
                _labelStyle);
            GUILayout.Label("Loaded assets: " + SceneResLoadOnDemand.LoadedAssetCount, _labelStyle);
            GUILayout.Label("Loading assets: " + SceneResLoadOnDemand.PendingAssetCount, _labelStyle);

            GUILayout.Space(4);
            GUILayout.Label("Green wireframes = cubes in the current streamed area", _labelStyle);

            GUILayout.EndArea();
        }

        private static string FormatPosition()
        {
            Transform hero = SceneResLoadOnDemand.GetTrsHero();
            if (hero == null)
            {
                return "-";
            }

            Vector3 position = hero.position;
            return string.Format("({0:F1}, {1:F1}, {2:F1})", position.x, position.y, position.z);
        }
    }
}
