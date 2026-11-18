using UnityEngine;

namespace SceneSplitterDemo
{
    /// <summary>
    /// 演示用英雄移动：WASD 平移，Shift 加速，相机跟随。
    /// 移动英雄即可观察九宫格范围内的场景资源被加载 / 卸载。
    /// </summary>
    public class DemoHeroController : MonoBehaviour
    {
        public Transform cameraTransform;
        public float moveSpeed = 12f;
        public float sprintMultiplier = 3f;
        public Vector3 cameraOffset = new Vector3(0f, 10f, -16f);
        public float cameraLerp = 10f;

        private void Start()
        {
            if (cameraTransform == null && Camera.main != null)
            {
                cameraTransform = Camera.main.transform;
            }

            SnapCamera();
        }

        private void Update()
        {
            Vector3 direction = Vector3.zero;
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) direction.z += 1f;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) direction.z -= 1f;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) direction.x -= 1f;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) direction.x += 1f;

            if (direction.sqrMagnitude > 0f)
            {
                float speed = moveSpeed;
                if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                {
                    speed *= sprintMultiplier;
                }

                transform.position += direction.normalized * speed * Time.deltaTime;
            }

            if (Input.GetKeyDown(KeyCode.Alpha1))
            {
                SceneResLoadOnDemand.viewGrid = 1;
                SceneResLoadOnDemand.StartUpdateCubeList();
            }

            if (Input.GetKeyDown(KeyCode.Alpha2))
            {
                SceneResLoadOnDemand.viewGrid = 2;
                SceneResLoadOnDemand.StartUpdateCubeList();
            }

            if (Input.GetKeyDown(KeyCode.Alpha3))
            {
                SceneResLoadOnDemand.viewGrid = 3;
                SceneResLoadOnDemand.StartUpdateCubeList();
            }

            if (cameraTransform != null)
            {
                Vector3 targetPosition = transform.position + cameraOffset;
                cameraTransform.position = Vector3.Lerp(
                    cameraTransform.position,
                    targetPosition,
                    1f - Mathf.Exp(-cameraLerp * Time.deltaTime));
                cameraTransform.LookAt(transform.position + Vector3.up * 1.5f);
            }
        }

        private void SnapCamera()
        {
            if (cameraTransform == null)
            {
                return;
            }

            cameraTransform.position = transform.position + cameraOffset;
            cameraTransform.LookAt(transform.position + Vector3.up * 1.5f);
        }
    }
}
