# Pytools IDE 项目全局定位与架构白皮书 (V6.0)

## 一、 项目核心定位
**Pytools IDE** 是一款面向网络工程教育、学术研究（毕设/论文）及快速原型验证的**轻量级、低门槛 UDP 协议离散事件仿真（DES）集成开发环境**。
它致力于打破 NS-3 / OMNeT++ 等传统工业级仿真器学习曲线陡峭的壁垒，通过"前端极简 Python 脚本 + 后端高性能 C# DES 内核"的双引擎架构，实现"编写极简、推演严谨、可视化直观、扩展性极强"的终极目标。

## 二、 双引擎桥接架构 (Dual-Engine Architecture)
系统采用前后端分离、跨进程通信（IPC）的架构设计：
* **前端（交互与表现层）：基于 Python 3.12**
    * 提供面向对象的高度封装 SDK (`pytools_sim.py`)，支持 `Host`, `Router`, `add_link`, `add_traffic` 等极简语义。
    * 内置底层的 Matplotlib 渲染劫持通道，自动将绘制的图表转码并投射到 IDE 的 Plot Viewer UI 中。
* **后端（仿真计算引擎）：基于 C# .NET 8 (WPF)**
    * 作为底层"黑盒"运行，负责拓扑校验、单跳路由计算和毫秒级精度的离散事件推演。
    * C# 框架底层通过运行时包装脚本动态注入 SimHei 中文字体配置与 matplotlib Agg 后端劫持，用户脚本零侵入。
    * 前后端通过标准输入/输出流 (Standard I/O) 使用严密的 JSON 契约进行全双工通信。

## 三、 核心统计算法与物理仿真机制（核心亮点）
为了在保障学术精确度的同时兼顾百万级并发压测的系统流畅性（防假死），内核实现了以下工业级算法：
1.  **自适应百分位降维采样算法 (Adaptive CDF)**：针对海量时延数据，当样本量 $N \le 100$ 时全量精确输出；当 $N > 100$ 时，启动百级分位数（Quantiles）抽样（101 个特征点），将空间复杂度从 $O(N)$ 极致压缩至常数级 $O(1)$，完美保留 P50、P99 等长尾时延关键指标。
2.  **基于 `LinkBusyUntil` 的排队时延推演**：摒弃物理队列的时钟死等，基于时间轴状态预测机制，利用底层二叉堆优先队列（`PriorityQueue<SimEvent, (double Time, long EventId)>`），将排队时延的计算复杂度控制在对数级 $O(\log M)$。
3.  **三层丢包屏障 (Three-Layer Loss Barrier)**：每条链路支持独立配置固定概率丢包 (`drop_rate`)、队列溢出丢包 (`max_queue_depth`)、超时丢包 (`max_timeout_ms`) 三个参数。引擎在 `PACKET_ARRIVAL` 事件中依次判定三层屏障，精确记录逐流发包数 (`_flowSent`) 和丢包数 (`_flowDropped`)，最终输出真实丢包率 = dropped / sent。
4.  **异构发送时延与载荷波动模拟**：接入 `MathNet.Numerics`，应用层依据 `payload_mean` 与方差动态生成正态分布的包大小，动态映射为毫秒级发送时延，真实复现大象流阻塞老鼠流的物理层拥塞现象。

## 四、 多维数据分析与可视化矩阵
系统具备纯中文化的"一源多端"数据展示能力：
* **IPython 控制台**：输出结构化 ASCII 表格，包含全局平均时延、时延标准差、逐流丢包率、队列峰值等。
* **变量区 (Variable Explorer)**：通过 `_translate_keys()` 将 C# 内核返回的英文键映射为中文字段，提供嵌套字典层级视图，展示每条数据流（Flows）的平均时延 (ms)、丢包率 (%) 和峰值队列深度。
* **绘图区 (Plot Viewer)**：自动化捕获底层引擎返回的 `cdf_data`，绘制具备出版级质量的全局时延累积分布函数 (CDF) 曲线。同时支持单图/批量保存为 PNG 文件。

## 五、 V6.0 新增：丢包率功能与性能防御
相较于 V5.2，V6.0 在仿真物理精度和系统健壮性方面做出了重大升级：
* **三层丢包屏障**：Python `Link` 类新增 `drop_rate`, `max_queue_depth`, `max_timeout_ms` 三个可选参数（均有合理默认值，向后兼容），全链路 JSON 序列化至 C# `LinkData` 类。
* **DES 断流致命 Bug 修复**：修复了丢包路径跳过 `ScheduleNextPacket` 导致流量源永久断流的关键漏洞。丢包时跳过 `_linkBusyUntil` 推进和时延记录，但仍调度下一包以维持事件链。
* **中文字体底层注入**：C# `BuildWrapperScript` 在包装脚本中 `import matplotlib.pyplot` 后立即注入 `rcParams['font.sans-serif'] = ['SimHei', 'Microsoft YaHei', 'sans-serif']`，从框架层面根治图表中文"豆腐块"问题，不侵入用户业务脚本。

## 六、 二次开发与生态扩展坞 (Extensibility)
系统具备工业级的可扩展性设计：
* **无缝 SDK 挂载**：C# 编译管线通过 MSBuild 通配符支持打包整个 `SDK/` 文件夹。并在运行时动态向 Python 环境最高优先级注入 `sys.path`。
* **第三方融合**：开发者可任意向 `SDK/` 拖入第三方 Python 优化算法库（如遗传算法、强化学习路由库）、高级流量生成库，用户脚本 `import` 即可原生调用，无需触碰 C# 底层架构。
* **V6.1 路线图 — 多目标策略优化**：基于当前参数化体系（每条链路 5 个独立参数 × N 条链路），规划开发 `Optimizer` 模块。通过网格搜索/启发式算法自动探索参数空间，在延迟与丢包率之间生成帕累托前沿，实现"在丢包率 < 1% 的约束下寻找最低延迟配置"等策略优化目标。该模块完全在 Python 侧实现，不需改动 C# 仿真内核。

## 七、 技术栈汇总

| 层次 | 技术 | 版本 |
|------|------|------|
| C# 运行时 | .NET 8.0 Windows (WPF) | net8.0-windows |
| 代码编辑器 | AvalonEdit | 6.3.1.120 |
| 停靠布局 | Dirkster.AvalonDock | 4.74.1 |
| UI 样式 | MaterialDesignThemes + ModernWpfUI | 5.3.2 / 0.9.6 |
| 科学计算 | MathNet.Numerics | 5.0.0 |
| Python 端 | matplotlib (Agg 后端, SimHei 字体) | Python 3.x |
| IPC 协议 | JSON over Standard I/O | — |
