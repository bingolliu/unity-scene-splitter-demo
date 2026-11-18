# 场景切割 + 按需加载 演示工程

把一个大场景按空间切成固定大小的方块，运行时只加载玩家周围的 mesh / material，走远就卸载。
目标是在**不重构美术资源**的前提下，把内存峰值压到 WebGL 小游戏这类内存敏感平台的可接受范围内。

- Unity 版本：`6000.5.8f1`
- 渲染管线：Built-in
- 第三方依赖：无
- 演示场景：`Assets/SceneSplitterDemo/Scenes/map35.unity`

---

## 1. 为什么需要这套方案

### 1.1 面临的三个约束

**① 场景资源引用混乱。**
场景里的物件大量散落在 `Model` / `Texture` / `Materials` / `Prefab` 多个目录，很多物件还是从公共目录直接引用，彼此交织。想按"资源归属"把它们拆干净，成本很高。

**② 常规优化手段要美术大量返工。**
业界常见做法是把场景内物件改成独立 Prefab，再交给 Addressables / AssetBundle 按需加载。
但这要求美术逐个拆分物件、重新挂引用、重建 Prefab —— **工作量大、周期长**，与上线节奏冲突。

**③ 上线时间紧，且目标是 WebGL 小游戏。**
浏览器环境内存非常敏感：整个场景一次性加载，轻则长时间白屏、卡顿，重则直接 OOM 崩溃；包体也不允许无限膨胀。

### 1.2 方案的切入点

既然改资源结构来不及，就**不改资源结构**，改"什么时候加载、什么时候释放"：

- 场景保持原样，美术无需拆分 Prefab、无需重挂引用；
- 编辑器自动分析场景，把"空间位置"和"物件依赖的 mesh / material"记录下来，产出一份切割数据；
- 运行时先把场景物件全部隐藏，只加载玩家附近方块所需的资源，物件到货后再显示并回填 mesh / material；
- 离开范围就隐藏物件、卸载资源，内存随视野波动，而不是随场景总量增长。

### 1.3 这套方案带来的收益

| 收益 | 说明 |
|---|---|
| 美术零改造 | 不需要拆 Prefab、不需要重挂引用，资源目录结构保持原样 |
| 加载粒度更细 | 以 mesh / material 为单位加载，比"整场景"或"整 Prefab"更省内存 |
| 内存可控 | 峰值由视野范围决定，与场景总量解耦 |
| 可重复执行 | 场景增删物件后重新切一次即可，无需人工维护清单 |
| 可迁移 | 切割数据只记录相对路径，加载方式可换成 AssetBundle / Addressables |

---

## 2. 方案组成

| 模块 | 文件 | 作用 |
|---|---|---|
| 切割工具（编辑器） | `Scripts/Editor/SceneCuttingWindow.cs` | 按方块遍历场景、检测物件、记录依赖、输出切割数据 |
| 按需加载（运行时） | `Scripts/Runtime/SceneResLoadOnDemand.cs` | 读切割数据、按玩家位置加载/卸载资源、控制物件显隐 |
| 数据模型 | `Scripts/Runtime/RuntimeSceneData.cs` | 方块表、资源路径表、物件→资源映射 |
| 演示入口 | `Scripts/Runtime/DemoAutoBoot.cs` | 自动创建相机/英雄/HUD 并启动加载流程 |
| 演示操作 | `DemoHeroController.cs` / `DemoHud.cs` / `SceneCutGizmoView.cs` | 移动、状态面板、方块线框 |

---

## 3. 快速开始

1. 用 Unity Hub 以 **6000.5.8f1** 打开本目录。
2. 打开场景 `Assets/SceneSplitterDemo/Scenes/map35.unity`。
3. 点击 Play。
4. `WASD` / 方向键移动橙色胶囊（英雄），`Shift` 加速，数字键 `1 / 2 / 3` 切换加载半径。

运行后会自动发生：

- 创建相机、英雄、HUD，并启动 `SceneResLoadOnDemand`；
- 读取 `Resources/SceneRes/map35/map35_data.bytes` 切割数据；
- 场景物件先全部隐藏，只加载英雄附近方块中的 mesh / material；
- 资源就绪的物件被显示，离开范围的物件重新隐藏、资源被卸载；
- HUD 实时显示可见方块数、已加载/加载中资源数、已激活物件数；
- Scene 视图用绿色线框标出当前参与加载的方块。

英雄从地图一角出生，向中心走会看到越来越多资源被加载，走远则被卸载。

---

## 4. 目录结构

```
Assets/SceneSplitterDemo/
├─ Scripts/
│  ├─ Runtime/
│  │  ├─ SceneResLoadOnDemand.cs    # 按需加载核心
│  │  ├─ RuntimeSceneData.cs        # 切割数据模型
│  │  ├─ DemoAutoBoot.cs            # 演示入口
│  │  ├─ DemoHeroController.cs      # 移动 + 相机跟随
│  │  ├─ DemoHud.cs                 # 加载状态面板
│  │  └─ SceneCutGizmoView.cs       # 方块线框
│  └─ Editor/
│     └─ SceneCuttingWindow.cs      # 场景切割工具
├─ Scenes/
│  └─ map35.unity                   # 演示场景
├─ Resources/SceneRes/
│  ├─ map35/                        # 场景自身资源 + 切割数据
│  └─ map35_com/                    # 场景引用的公共资源
└─ External/                        # 场景用到的 shader / cginc
```

> 资源放在 `Resources/SceneRes` 是为了让演示工程"打开即跑"。生产环境可换成
> AssetBundle / Addressables，切割数据格式不用变（详见 5.3）。

---

## 5. 技术原理

### 5.1 编辑器端：把场景切成方块

菜单：`Tools/场景切割与按需加载/切割窗口`

1. 给参与切割的根节点（`Scene` / `Effect` / `Animation`）下所有带 `MeshRenderer` 的物件临时挂 `MeshCollider`；
2. 以 `cubeSize`（默认 10 世界单位）为步长，遍历场景包围盒内的每个方块，用 `Physics.OverlapBox` 求出方块覆盖到哪些物件；
3. 给每个物件生成稳定 uuid：按层级索引拼接，例如 `Scene/Item/Item_xxx` → `0_1_12`；
4. 记录每个物件依赖的资源：
   - mesh：优先用 `Mesh` 目录下已有的 `.asset`，没有就从 FBX 子资源里提取成 `Mesh/<名字>.asset`；
   - material：直接记录它在 `Resources` 下的路径（不在 `Resources` 下则自动复制一份）；
5. 输出两份文件：
   - `<场景名>_data.bytes`：运行时读取的精简数据；
   - `<场景名>_debug_data.bytes`：带 nodePath / mesh / material 明细，用于排查。

切割结束后临时 `MeshCollider` 会被移除，场景文件不会被改动。

### 5.2 运行端：只加载玩家身边的资源

`SceneResLoadOnDemand` 的循环：

1. `OnSceneStart()` 异步读取本场景的切割数据；
2. 反序列化后建立两张表：`方块 → 物件 uuid 列表`、`物件 uuid → 资源 id 列表`；
3. 按 `Scene / Effect / Animation` 根节点重建 uuid 映射，数据里出现的物件先 `SetActive(false)`；
4. 英雄移动超过 `max_hero_delta_pos` 时：
   - 取英雄所在方块为中心、半径 `viewGrid` 的方块集合；
   - 汇总这些方块里物件的资源 id，异步加载尚未加载的部分；
   - 某个物件的资源全部到齐后，回填 `sharedMesh` / `sharedMaterials` 并 `SetActive(true)`；
5. 离开范围的物件隐藏；不再被任何可见方块引用的资源被卸载。

### 5.3 几个关键取舍

**为什么不改 Prefab？**
改成独立 Prefab 才能用 Addressables 按 Prefab 加载，但需要美术逐个拆分和重挂引用。
本方案以 mesh / material 为加载单位，恰好绕开了 Prefab 改造，加载粒度反而更细。

**为什么演示工程用 `Resources`？**
演示要"打开即跑"，`Resources` 零配置。
生产环境应换成 AssetBundle / Addressables：切割数据里存的是相对路径（如 `map35_com/Mesh/Item_map35_st_04`），
只需把加载器中的 `Resources.LoadAsync` 替换成对应 API，切割算法与数据结构完全不变。

**为什么用 `JsonUtility`？**
切割数据全部是简单字段（向量、数组、字符串），不需要引入第三方 JSON 库，也不依赖反射序列化。

**为什么按 uuid 记录而不是按 Prefab？**
同一个 Prefab 可能在不同位置、不同缩放、甚至不同材质实例下出现；
按层级 uuid 记录可以让"哪个方块里是哪一个具体物件"保持唯一，显隐和回填不会串。

---

## 6. 切割数据格式

```json
{
  "cubeSize": 10,
  "minArrayIndexX": -20, "maxArrayIndexX": 30,
  "minArrayIndexY": -20, "maxArrayIndexY": 20,
  "minArrayIndexZ": -30, "maxArrayIndexZ": 30,
  "cubeList": [
    { "pos": { "x": 0, "y": 0, "z": 0 }, "key": 0, "uuidArray": ["0_1_0", "0_1_1"] }
  ],
  "pathArray": [
    "map35_com/Materials/Item_map35_yumao_01",
    "map35_com/Mesh/Item_map35_st_04"
  ],
  "runtimeGameObjectResArray": [
    { "uuid": "0_1_0", "resIDArray": [1, 0] }
  ]
}
```

- `pathArray` 中的路径相对于 `Resources/SceneRes/`，运行时拼成 `SceneRes/<path>` 加载；
- `key` 由坐标压成整数：`x * 1000 * 1000 + y * 1000 + z`，用于 O(1) 命中方块；
- `cubeSize` 会覆盖运行时的默认值，保证切割与加载一致。

---

## 7. 参数与调优

`SceneResLoadOnDemand` 的静态字段：

| 字段 | 默认值 | 说明 |
|---|---|---|
| `cubeSize` | 10 | 方块大小，切割数据里有值时自动覆盖 |
| `viewGrid` | 1 | 加载半径，1 表示 X/Z 各 3 格（可运行时按 1/2/3 切换） |
| `max_hero_delta_pos` | 3 | 英雄移动多远触发重算 |
| `nodeNames` | Scene / Effect / Animation | 参与流式加载的根节点 |

调优经验：

- **内存吃紧** → 减小 `viewGrid` 或减小 `cubeSize`（越小越精确，但方块数量与碰撞检测耗时上升）；
- **加载卡顿** → 增大 `viewGrid` 让加载提前发生，或增大 `max_hero_delta_pos` 减少重算频率；
- **遗漏物件** → 检查物件是否在 `nodeNames` 根节点下，以及名字是否以 `terr` / `water` / `npc` / `fx` 开头（这些会被跳过）。

---

## 8. 换场景 / 重新切割

1. 把场景资源放到 `Assets/SceneSplitterDemo/Resources/SceneRes/<场景名>/`，公共资源目录与之平级；
2. 把 `.unity` 放到 `Assets/SceneSplitterDemo/Scenes/`；
3. 打开场景，执行 `Tools/场景切割与按需加载/切割窗口` →「切割选中场景」；
4. Play：`DemoAutoBoot` 会自动识别该场景的切割数据并启动。

注意：

- 运行时只能 `Resources.Load` 到 `Resources` 目录下的资源，资源必须放在 `Resources/SceneRes` 下；
- 改了 `cubeSize` 必须重新切割（数据里保存了该值）；
- 工程使用 Built-in 管线，换 URP 场景需要另行转换材质 / Shader。

---

## 9. 常见问题

### 9.1 `ObjectDisposedException: Stream has been closed`（UnityEditor.Search）

```
ObjectDisposedException: Cannot access a disposed object.
Object name: 'Stream has been closed'.
System.IO.BinaryWriter.Dispose ()
UnityEditor.Search.PropertyDatabaseFileStoreView.Dispose ()
UnityEditor.Search.SearchMonitor.InvalidateDocuments ()
```

这是 Unity 6 编辑器自身的 **Search 索引 / PropertyDatabase 缓存** 在资源刷新时被重复释放导致的内部异常，
与工程脚本无关，也不影响 Play 模式、切割工具和打包。常见触发场景：

- 首次导入大量资源（本工程约 190 个文件 / 32 MB）；
- 切换场景、批量增删资源触发 AssetDatabase 刷新；
- `Library/Search` 下 `propertyDatabase.db` 被截断而 `transactions.db` 仍很大。

处理办法：

1. 先直接重启 Unity，多数情况一次即可；
2. 若反复出现，关闭 Unity 后删除 `Library/Search`（纯缓存，Unity 会重建）：

   ```
   <工程目录>/Library/Search
   ```

3. 大型工程可关闭常驻索引：`Edit > Preferences > Search > Indexing`，
   取消勾选 `Keep indexes up to date`，或在该面板排除 `Library`、`Temp` 及大资源目录。

### 9.2 Play 后场景一片空

- 确认 `Resources/SceneRes/<场景名>/<场景名>_data.bytes` 存在（不存在时 Console 会有明确提示）；
- 确认英雄所在位置附近确实有方块（HUD 的 Visible cubes 是否为 0）；
- 确认 `nodeNames` 与场景根节点名称一致。

### 9.3 物件显示成粉红 / 材质丢失

- 材质引用的 Shader 没有一起迁移过来，补齐 `External/Res/Shader` 下的 shader 与 cginc；
- 或用「切割选中场景」重新切一次，让工具重新记录材质路径。

---

## 10. 已知限制

- 只对 `nodeNames` 根节点下、名字不以 `terr` / `water` / `npc` / `fx` 开头的物件生效，其余物件始终加载；
- 资源在 `Resources` 目录下时会全部打进包体，**减少的是运行时内存，不是包体下载量**；要压缩下载量需换成 AssetBundle / Addressables；
- 运行时才回填 mesh / material，会削弱静态合批与 GPU Instancing 的效果，物件特别密集时需评估 DrawCall；
- 依赖场景烘焙光照时，隐藏再显示不会重建光照贴图，但实时阴影/探针表现需按项目实际情况验证；
- 演示工程使用 Built-in 管线与默认渲染设置，生产接入前需按目标平台再压测一轮内存。
