# Pytools 网络仿真模块 - 用户指南与帮助文档 (V6.0)

## 1. 快速开始 (Quick Start)

欢迎使用 Pytools 轻量级网络仿真模块！本模块在底层基于高性能的离散事件仿真（DES）引擎驱动，支持泊松流量生成、三层丢包屏障、排队时延推演以及自适应 CDF 统计。

你只需要在 Pytools 编辑器中编写一个纯 Python 脚本，配置好节点和链路，点击 IDE 顶部的 **"▶ 运行"** 按钮，即可一键在终端、变量区和绘图区看到最严谨的仿真结果。

### 1.1 第一个仿真脚本
请在编辑器中新建一个 `.py` 文件，复制并运行以下基础示例代码：

```python
from pytools_sim import Simulator, Host, Router

# 1. 初始化仿真器
sim = Simulator()

# 2. 创建网络节点
h1 = Host(id="H1")
h2 = Host(id="H2")
r1 = Router(id="R1")

# 3. 将节点添加到仿真器
sim.add_node(h1)
sim.add_node(h2)
sim.add_node(r1)

# 4. 配置物理链路 (带宽Mbps, 时延ms, 丢包率, 队列深度, 超时阈值)
sim.add_link(src="H1", dst="R1", bw=100, delay=1.0)
sim.add_link(src="H2", dst="R1", bw=100, delay=2.0,
             drop_rate=0.01, max_queue_depth=200, max_timeout_ms=30.0)

# 5. 配置业务流量 (间隔ms, 包大小bytes)
sim.add_traffic(src="H1", dst="H2", type="UDP",
                interval_mean=1.0, payload_mean=512)

# 6. 启动仿真 (固定随机种子, 保证可复现)
results = sim.run(simulation_time=500, seed=42)
```

---

## 2. 网络拓扑配置详解 (Topology Configuration)

网络拓扑由节点（Nodes）和链路（Links）共同构成。在编写脚本时，必须遵循底层的网络工程学约束。

### 2.1 节点类型 (Nodes)

* **`Host(id)`**：主机节点。代表网络的流量发起者（源）或接收者（目的）。
    * **⚠️ 核心拓扑约束**：在当前版本中，**一个 Host 必须且只能连接到一个 Router**。该 Router 将自动作为该主机的默认网关。主机之间不能直接建立 Link，主机也不能连接多个路由器。
* **`Router(id)`**：路由器节点。代表网络的网络层核心转发设备。当前版本采用单跳路由模型——Host 发往任意目的地的包都经由其唯一网关 Router 完成一跳转发。

### 2.2 链路配置 (Links)

链路代表节点之间的物理网线。每条链路可以独立配置三层丢包屏障参数。

* **API 格式**：`sim.add_link(src, dst, bw, delay, drop_rate=0.0, max_queue_depth=100, max_timeout_ms=50.0)`
* **参数说明**：

| 参数 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `src` | `str` | — | 链路源节点 ID |
| `dst` | `str` | — | 链路目的节点 ID |
| `bw` | `int` | — | 链路带宽（Mbps） |
| `delay` | `float` | — | 物理传播时延（ms） |
| `drop_rate` | `float` | `0.0` | **【V6.0】** 固定概率丢包率（0.0 ~ 1.0） |
| `max_queue_depth` | `int` | `100` | **【V6.0】** 队列溢出阈值 |
| `max_timeout_ms` | `float` | `50.0` | **【V6.0】** 超时丢包阈值（ms） |

丢包参数均有默认值，如果你不需要丢包功能，直接省略即可：

```python
# 不配丢包参数：零丢包（向后兼容 V5.x 行为）
sim.add_link(src="H1", dst="R1", bw=100, delay=1.0)

# 配置丢包：1% 固定丢包 + 队列深度 50 溢出 + 30ms 超时
sim.add_link(src="H2", dst="R1", bw=100, delay=2.0,
             drop_rate=0.01, max_queue_depth=50, max_timeout_ms=30.0)
```

---

## 3. 业务流量与数据波动配置 (Traffic & Variance)

模块天然支持**并行多流（Multiple Flows）**以及**数据包大小波动（Payload Variance）**，用于在学术研究中更真实地模拟网络排队与拥塞现象。

### 3.1 并行多流 (Multiple Flows)

你可以在同一个仿真器中添加多条独立的业务流。它们将在底层的虚拟时钟中并行调度，互不干扰，内核会自动对每条流单独进行端到端延迟和丢包率统计。

```python
# 流量流 1：从 H1 发往 H2
sim.add_traffic(src="H1", dst="H2", type="UDP", interval_mean=1.0, payload_mean=512)

# 流量流 2：从 H1 同时发往 H3
sim.add_traffic(src="H1", dst="H3", type="UDP", interval_mean=2.0, payload_mean=1024)
```

### 3.2 传输层协议限制

* **`type="UDP"`**：当前版本原生支持。流量遵循泊松流规律（指数分布间隔），尽力而为地发包。
* **`type="TCP"`**：**当前版本暂不支持**。若在配置中强行指定为 `"TCP"`，SDK 将在 `Traffic.__init__` 构造阶段立即抛出 `ValueError`，并在控制台弹出纯中文报错。

### 3.3 数据包波动 (Payload Variance)

真实的学术模拟中，数据包大小不应该是一成不变的。你可以通过 `payload_variance` 参数控制包大小的抖动：

* **恒定大小（默认）**：不配该参数或设为 0。所有生成的包大小严格等于 `payload_mean`（下限 64 bytes）。
* **正态分布抖动**：配置 `payload_variance` 大于 0。底层内核将采用 `MathNet.Numerics` 的正态分布模型，自动围绕均值和标准差（`√variance`）动态生成每个包的大小。

```python
# 模拟围绕 512 字节波动（方差为 256，标准差为 16）的正态分布数据包
sim.add_traffic(src="H1", dst="H2", type="UDP",
                interval_mean=1.0, payload_mean=512, payload_variance=256)
```

---

## 4. 运行控制与大规模防御 (Execution Control)

仿真运行的全局行为和输出细节完全由 `sim.run()` 的参数进行精准控制。

### 4.1 核心控制参数

| 参数 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `simulation_time` | `int` | — | 仿真虚拟时长（ms）。例如 `500` 代表虚拟网络运行 500ms，实际计算瞬间完成 |
| `seed` | `int \| None` | `None` | 随机数种子。不配则每次结果略有不同；固定值则 100% 可复现 |
| `summary_only` | `bool` | `False` | 大规模性能防御开关。`True` 时 `flows` 返回空字典 |

### 4.2 可复现性保证

`seed` 参数会同时固定底层两个随机源：
1. **指数分布**（Exponential Distribution）—— 控制包到达间隔
2. **正态分布**（Normal Distribution）—— 控制包载荷大小波动

这意味着固定 `seed=42` 后，无论运行多少次，所有流的发包时间序列和包大小序列将 **100% 完全一致**，满足学术论文的可复现性要求。

### 4.3 性能防御机制说明

当进行大规模网络压测时，上万条流细分数据会让右侧的 **Variable Explorer** 渲染承受极大压力。

* 强烈建议在大规模实验时开启：`sim.run(simulation_time=500, seed=42, summary_only=True)`
* 开启后，C# 内核返回的响应 JSON 中 `flows` 字段为空字典 `{}`，只保留全局宏观指标（`global_avg_delay_ms`、`global_std_dev_ms`、`cdf_data`），从而彻底免疫 IDE 界面卡死、假死现象。

---

## 5. 三层丢包屏障详解 (Three-Layer Loss Barrier) 【V6.0】

V6.0 为每条链路引入了可独立配置的三层丢包判定机制。当数据包到达链路时，引擎按以下顺序依次判定：

### 5.1 屏障 A：固定概率丢包 (Random Drop)

```python
sim.add_link(src="H1", dst="R1", bw=100, delay=1.0, drop_rate=0.05)
```

* 每个到达该链路的包有 5% 概率被随机丢弃。
* 判定依据：`Random.NextDouble() < drop_rate`（使用与 `seed` 绑定的确定性随机数源）。
* 适合模拟底层物理链路的比特错误率（BER）导致的随机丢包。

### 5.2 屏障 B：队列溢出丢包 (Queue Overflow)

```python
sim.add_link(src="H1", dst="R1", bw=100, delay=1.0, max_queue_depth=50)
```

* 当排队深度 `qDepth = queueDelay × 10`（即时延映射队列长度）超过 `max_queue_depth` 时触发。
* 仅在前置屏障（A）未命中时判定。
* 适合模拟路由器端口缓冲区溢出导致的拥塞丢包。

### 5.3 屏障 C：超时丢包 (Timeout Drop)

```python
sim.add_link(src="H1", dst="R1", bw=100, delay=1.0, max_timeout_ms=30.0)
```

* 当排队时延 `queueDelay` 超过 `max_timeout_ms` 时触发。
* 仅在前置屏障（A、B）均未命中时判定。
* 适合模拟实时应用（VoIP、在线游戏）对延迟上限的硬约束。

### 5.4 丢包统计

被三层屏障任意一层丢弃的包：
* 不计入 `_allPacketDelays` 和 `_flowDelays`（无有效时延）
* 不推进 `LinkBusyUntil`（不占用链路资源）
* 但流量源继续按分布调度下一包（事件链不中断）

最终丢包率 = `_flowDropped / _flowSent`，精确到小数点后 4 位。

---

## 6. 输出结果解析与数据导出 (Results & Export)

仿真结束后，IDE 将通过**终端（IPython Console）**、**变量区（Variable Explorer）**和**绘图区（Plot Viewer）**进行三通道自适应呈现。

### 6.1 终端精美摘要报告

无论是否开启 `summary_only`，你的 IPython Console 都会在运行结束的一瞬间打印出一份格式规范的 ASCII 报告：

```text
==================================================
  仿真完成
==================================================
  状态:              success
  仿真时长 (s):      500
  总发包数:          1513
  内核耗时 (s):      0.045
  平均时延 (ms):     3.835
  时延标准差 (ms):   0.523
  ------------------------------------------------
  [H1->H2]
    平均时延:  12.450 ms
    丢包率:    0.0132
    队列峰值:  42
==================================================
```

注意：`summary_only=True` 时不打印流量明细段。

### 6.2 CDF 累积分布函数图

每次仿真自动生成**全局时延累积分布函数（CDF）曲线**，显示在右侧 Plot Viewer 中：

* x 轴：时延（ms）
* y 轴：累积概率（0.0 ~ 1.0）
* 数据点 ≤ 100 时全量绘制；> 100 时自适应降维为 101 个等距分位点

你可以在 Plot Viewer 中通过工具栏按钮将图表保存为 PNG。

### 6.3 变量区字典呈现

如果你在代码中使用变量接收了返回值（如 `results = sim.run()`），右侧的 Variable Explorer 将展示一个中文字段名的 `dict` 类型变量，字段映射如下：

| 中文名 | 英文 Key（原始 JSON） | 说明 |
|--------|----------------------|------|
| 状态 | `status` | `"success"` 或 `"error"` |
| 总发包数 | `total_packets` | 所有到达链路的包总数（含被丢弃的） |
| 内核真实耗时（s） | `execution_time_sec` | C# 引擎实际计算耗时 |
| 全局平均时延（ms） | `global_avg_delay_ms` | 所有存活包的平均端到端时延 |
| 时延标准差（ms） | `global_std_dev_ms` | 总体标准差 |
| 流量明细 | `flows` | 逐流字典（`summary_only=True` 时为空） |

逐流明细（`flows` 内每个 key）：

| 中文名 | 英文 Key | 说明 |
|--------|----------|------|
| 平均时延（ms） | `avg_delay_ms` | 该流存活包的平均时延 |
| 丢包率 | `loss_rate` | 实际丢包率 = dropped / sent |
| 队列峰值 | `peak_queue` | 该流的历史最大队列深度 |

### 6.4 二次处理与导出

你可以直接在随后的 Python 代码里像操作普通字典一样操作 `results`：

```python
# 1. 提取特定流量流的平均延迟
flows = results.get('流量明细', {})
if 'H1->H2' in flows:
    print("H1 到 H2 的平均时延为：", flows['H1->H2']['平均时延（ms）'])

# 2. 将本次仿真的全部原始数据一键导出为外部 JSON 报告
import json
json.dump(results, open("my_sim_report.json", "w"), indent=2, ensure_ascii=False)
```

---

## 7. 完整示例 (Full Examples)

### 7.1 小规模教学演示

```python
from pytools_sim import Simulator, Host, Router

sim = Simulator()

# 拓扑
sim.add_node(Host("H1"))
sim.add_node(Host("H2"))
sim.add_node(Host("H3"))
sim.add_node(Router("R1"))

sim.add_link("H1", "R1", bw=100, delay=1.0)
sim.add_link("H2", "R1", bw=100, delay=1.0)
sim.add_link("H3", "R1", bw=100, delay=1.0)

# 三条并行流
sim.add_traffic("H1", "H2", type="UDP", interval_mean=1.0, payload_mean=512)
sim.add_traffic("H1", "H3", type="UDP", interval_mean=2.0, payload_mean=256)
sim.add_traffic("H2", "H3", type="UDP", interval_mean=0.5, payload_mean=1024)

results = sim.run(simulation_time=1000, seed=42)
```

### 7.2 大规模压测（开启性能防御）

```python
from pytools_sim import Simulator, Host, Router

sim = Simulator()

# 批量生成 200 个主机 + 1 个路由器
for i in range(1, 201):
    sim.add_node(Host(f"H{i}"))
sim.add_node(Router("R1"))

for i in range(1, 201):
    sim.add_link(f"H{i}", "R1", bw=100, delay=1.0,
                 drop_rate=0.001, max_queue_depth=1000)

# 批量生成 500 条并行业务流
for i in range(1, 201):
    for j in range(1, 4):
        if i + j <= 200:
            sim.add_traffic(f"H{i}", f"H{i+j}", type="UDP",
                          interval_mean=5.0, payload_mean=512, payload_variance=64)

# summary_only=True 避免变量区渲染海量数据
results = sim.run(simulation_time=5000, seed=123, summary_only=True)
print("全局平均时延:", results.get('全局平均时延（ms）'))
```

---

## 8. 常见问题排查 (Troubleshooting)

### 8.1 图表中文显示为方块（豆腐块）

V6.0 已从 C# 包装脚本底层注入 `SimHei` 中文字体。如果你仍看到方块：

* **Windows**：安装 SimHei（黑体）字体或 Microsoft YaHei（微软雅黑）
* **确认**：`matplotlib` 字体缓存已刷新。运行：
  ```python
  import matplotlib
  print(matplotlib.get_cachedir())
  ```
  删除该目录下的 `fontlist-v330.json` 后重新运行 Pytools

### 8.2 拓扑校验报错

错误：`拓扑校验失败：主机 'H1' 未连接或连接了多个路由器。`

* 原因：每个 Host 必须且只能通过一条 Link 连接到一个 Router
* 解决：检查你的 `add_link` 调用，确保每个 Host 在 Links 中恰好出现一次

错误：`拓扑校验失败：主机 'H1' 与 'H2' 之间不允许直连。`

* 原因：Host 之间不能直接建立 Link，必须通过 Router 中转
* 解决：增加 Router 节点，让两个 Host 都连接到该 Router

### 8.3 TCP 协议报错

错误：`ValueError: 当前版本暂不支持 TCP 协议。请使用 UDP。`

* 解决：将所有 `sim.add_traffic(type="TCP", ...)` 替换为 `type="UDP"`

---

## 9. 版本记录

| 版本 | 日期 | 主要更新 |
|------|------|----------|
| V1.0 | 2025 | 初始版本，基础拓扑/链路/流量配置 |
| V6.0 | 2026-05-20 | 新增三参数丢包屏障（`drop_rate`/`max_queue_depth`/`max_timeout_ms`）、真实丢包率统计、CDF 自动绘图、Plot Viewer 保存、中文字段变量区、大规模性能防御 |
