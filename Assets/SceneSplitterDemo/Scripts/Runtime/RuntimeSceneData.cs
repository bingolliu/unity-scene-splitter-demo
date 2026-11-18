using System;
using System.Collections.Generic;
using UnityEngine;

namespace SceneSplitterDemo
{
    /// <summary>
    /// 一个空间方块（默认 10x10x10）覆盖到的物体 uuid 列表。
    /// </summary>
    [Serializable]
    public class RuntimeCube
    {
        public Vector3 pos;
        public int key;
        public string[] uuidArray;
    }

    /// <summary>
    /// 一个场景物体依赖的资源 id 列表（mesh + material）。
    /// </summary>
    [Serializable]
    public class RuntimeGameObjectRes
    {
        public string uuid;
        public int[] resIDArray;
    }

    /// <summary>
    /// 场景切割数据。字段均可被 JsonUtility 序列化，因此不依赖第三方 Json 库。
    /// </summary>
    [Serializable]
    public class RuntimeSceneData
    {
        public int cubeSize;

        public int minArrayIndexX;
        public int maxArrayIndexX;

        public int minArrayIndexY;
        public int maxArrayIndexY;

        public int minArrayIndexZ;
        public int maxArrayIndexZ;

        public RuntimeCube[] cubeList;
        public string[] pathArray;
        public RuntimeGameObjectRes[] runtimeGameObjectResArray;

        [NonSerialized] public Dictionary<int, RuntimeCube> cubeDict;
        [NonSerialized] public Dictionary<string, RuntimeGameObjectRes> uuid2RuntimeGameObjectRes;

        public void InitDict()
        {
            cubeDict = new Dictionary<int, RuntimeCube>();
            if (cubeList != null)
            {
                foreach (RuntimeCube cube in cubeList)
                {
                    if (!cubeDict.ContainsKey(cube.key))
                    {
                        cubeDict.Add(cube.key, cube);
                    }
                }
            }

            cubeList = null;

            uuid2RuntimeGameObjectRes = new Dictionary<string, RuntimeGameObjectRes>();
            if (runtimeGameObjectResArray != null)
            {
                foreach (RuntimeGameObjectRes res in runtimeGameObjectResArray)
                {
                    if (!uuid2RuntimeGameObjectRes.ContainsKey(res.uuid))
                    {
                        uuid2RuntimeGameObjectRes.Add(res.uuid, res);
                    }
                }
            }

            runtimeGameObjectResArray = null;
        }

        public string GetPath(int resID)
        {
            if (pathArray == null || resID < 0 || resID >= pathArray.Length)
            {
                return null;
            }

            return pathArray[resID];
        }

        public RuntimeGameObjectRes GetRuntimeGameObjectResByUUID(string uuid)
        {
            if (uuid2RuntimeGameObjectRes == null || uuid == null)
            {
                return null;
            }

            return uuid2RuntimeGameObjectRes.TryGetValue(uuid, out RuntimeGameObjectRes res) ? res : null;
        }

        public int[] GetResIDArrayByUUID(string uuid)
        {
            RuntimeGameObjectRes res = GetRuntimeGameObjectResByUUID(uuid);
            return res != null ? res.resIDArray : null;
        }
    }
}
