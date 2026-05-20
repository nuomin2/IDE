# Pytools IDE 项目全局定位与架构白皮书 (V6.1)

## 一、 项目核心定位
**Pytools IDE** 是一款面向网络工程教育、学术研究（毕设/论文）及快速原型验证的**轻量级、低门槛 UDP 协议离散事件仿真（DES）集成开发环境**。
它致力于打破 NS-3 / OMNeT++ 等传统工业级仿真器学习曲线陡峭的壁垒，通过"前端极简 Python 脚本 + 后端高性能 C# DES 内核"的双引擎架构，实现"编写极简、推演严谨、可视化直观、扩展性极强"的终极目标。

## 二、 双引擎桥接架构 (Dual-Engine Architecture)
系统采用前后端分离、跨进程通信（IPC）的架构设计：
* **前端（交互与表现层）：基于 Python 3.12**
    * 提供面向对象的高度封装 SDK (`pytools_sim.py`)，支持 `Host`, `Router`, `add_link`, `add_traffic`, `draw_topology` 等极简语义。
    * 内置底层的 Matplotlib 渲染劫持通道，自动将仿真的 CDF 图表和拓扑可视化投射到 IDE 的 Plot Viewer UI 中。
* **后端（仿真计算引擎）：基于 C# .NET 8 (WPF)**
    * 负责拓扑校验、**Dijkstra 多跳路由计算**、毫秒级精度离散事件推演和**事件接力转发**。
    * C# 框架底层通过运行时包装脚本动态注入 SimHei 中文字体配置与 matplotlib Agg 后端劫持，用户脚本零侵入。
    * 前后端通过标准输入/输出流使用严密的 JSON 契约进行全双工通信。

## 三、 核心统计算法与物理仿真机制
1.  **Dijkstra 多跳路由与事件接力转发 (Multi-hop with Relay Forwarding)**：T=0 时刻以 link.Delay 为边权构建无向加权图，运行 Dijkstra 计算全局最短路径，回溯 prev 链提取 NextHop。PACKET_ARRIVAL 事件中，非终点则查路由表创建转发事件（继承 CreationTime，更新 FromNodeId），沿路径逐跳推进；到达终点时用 `(最终离开时间 - CreationTime)` 结算端到端时延。
2.  **自适应百分位降维采样算法 (Adaptive CDF)**：N ≤ 100 全量精确输出；N > 100 时 101 分位点抽样，空间复杂度 O(1)。
3.  **基于 LinkBusyUntil 的排队时延推演**：利用 `PriorityQueue<SimEvent, (double Time, long EventId)>` 最小堆，O(log M) 复杂度。
4.  **三层丢包屏障 (Three-Layer Loss Barrier)**：每跳独立判定固定概率丢包→队列溢出丢包→超时丢包，逐流统计 `_flowSent`/`_flowDropped`，真实丢包率=dropped/sent，覆盖中间路由器。
5.  **异构发送时延与载荷波动模拟**：MathNet.Numerics Exponential(间隔) + Normal(包大小)，复现大象流阻塞老鼠流现象。

## 四、 多维数据分析与可视化矩阵
* **IPython 控制台**：结构化 ASCII 表格（全局平均时延、时延标准差、逐流丢包率、队列峰值）。
* **变量区 (Variable Explorer)**：`_translate_keys()` 中英字段映射，嵌套字典层级视图。
* **绘图区 (Plot Viewer)**：CDF 累积分布函数图 + **拓扑结构可视化**（`draw_topology()` 圆形布局，Host 蓝色方形 / Router 橙色圆形，链路中点标注时延）。支持单图/批量 PNG 保存。

## 五、 V6.1 新增：多跳路由重构与拓扑可视化

| 模块 | 变更内容 |
|------|----------|
| `SimEvent` | 新增 `CreationTime`(包出生时间) 和 `FromNodeId`(当前跳起点)，支持接力转发与端到端记账 |
| `ComputeRoutes` | 单跳硬编码 → **Dijkstra 全局最短路径**（无向加权图，回溯 prev 链提取 NextHop） |
| `PACKET_ARRIVAL` | 引入接力转发机制：非终点查路由表生成转发事件，终点用 CreationTime 结算端到端时延 |
| 流量调度 | `isFirstHop` 守卫确保 `ScheduleNextPacket` 仅在源发第一跳触发，中间转发不产生新包 |
| 丢包/峰值统计 | 移除 `isFirstHop` 限制，覆盖每跳（中间路由器拥塞可见） |
| `draw_topology` | 圆形布局拓扑可视化，经 C# 图像劫持通道投射至 Plot Viewer |
| `add_link` | 签名补全 V6.0 丢包参数透传 |

## 六、 V6.2 路线图 — 多目标策略优化
基于当前参数化体系（每条链路 5 个独立参数 × N 条链路 + 多跳拓扑），规划开发 `Optimizer` 模块。通过网格搜索/启发式算法自动探索参数空间，在延迟与丢包率之间生成帕累托前沿。该模块完全在 Python 侧实现，不需改动 C# 仿真内核。

## 七、 技术栈汇总

| 层次 | 技术 | 版本 |
|------|------|------|
| C# 运行时 | .NET 8.0 Windows (WPF) | net8.0-windows |
| 代码编辑器 | AvalonEdit | 6.3.1 |
| 停靠布局 | Dirkster.AvalonDock | 4.74.1 |
| UI 样式 | MaterialDesignThemes + ModernWpfUI | 5.3.2 / 0.9.6 |
| 科学计算 | MathNet.Numerics | 5.0.0 |
| Python 端 | matplotlib (Agg, SimHei) + math | Python 3.x |
| IPC 协议 | JSON over Standard I/O | — |
