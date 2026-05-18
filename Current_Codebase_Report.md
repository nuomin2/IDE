# Pytools IDE 当前代码库逆向执行报告 (Current Codebase Report)

> 生成日期: 2026-05-19 | 审计范围: `Pytools/` 目录下所有 `.cs` 与 `.py` 源码（不含 `obj/`、`.claude/`、`bin/` 生成物）

---

## 模块 1：IPC 通信与多通道数据流转

### 1.1 进程生命周期管理

**类**: `PythonExecutionService` (`Pytools/Services/PythonExecutionService.cs`)

```csharp
public class PythonExecutionService : IDisposable
{
    private Process? _process;
    public event Action<string>? LineReceived;
    public event Action? ExecutionFinished;
    public event Action<List<VariableEntry>>? VariablesReceived;
    public event Action<byte[]>? PlotReceived;
}
```

执行入口 `ExecuteAsync(string scriptPath)` (L25-91):
1. 调用 `FindPython()` —— 尝试执行 `python --version`，若 ExitCode=0 则返回 `"python"`，否则返回 `null`。
2. 调用 `BuildWrapperScript(scriptPath, scriptName, directory)` —— 动态生成一个临时 `.py` 包装脚本到 `Path.GetTempPath()/pytools_run_{Guid:N}.py`。
3. 启动进程: `RedirectStandardOutput = true`, `RedirectStandardError = true`, `RedirectStandardInput = true`, `UseShellExecute = false`。
4. 注册事件: `_process.OutputDataReceived += OnOutputLine`（stdout），`_process.ErrorDataReceived += ...`（stderr 直接转发为 LineReceived）。
5. 调用 `_process.BeginOutputReadLine()` 和 `_process.BeginErrorReadLine()` 启动异步行读取。
6. `await _process.WaitForExitAsync()` 等待进程结束，随后清理临时包装脚本，触发 `ExecutionFinished`。

`Stop()` (L215-224): 调用 `_process.Kill()` 强制终止。

### 1.2 Stdout 行拦截：三通道标记解析

**核心方法**: `OnOutputLine(object? sender, DataReceivedEventArgs e)` (L93-203)

使用 `string.IndexOf(marker, StringComparison.Ordinal)` —— **不使用正则表达式**，纯字符串索引匹配。三种标记按以下优先级顺序处理：

#### 通道 A: 仿真请求拦截 `##SIM_START##` ... `##SIM_END##` (L97-151)

```csharp
const string simMarker = "##SIM_START##";
int simIdx = e.Data.IndexOf(simMarker, StringComparison.Ordinal);
if (simIdx >= 0)
{
    string json = e.Data[(simIdx + simMarker.Length)..];
    int simEndIdx = json.IndexOf("##SIM_END##", StringComparison.Ordinal);
    if (simEndIdx >= 0)
    {
        json = json[..simEndIdx].Trim();
        // 反序列化 → 校验 → 后台运行 SimulatorEngine → 结果通过 stdin 写回
    }
    return;  // ★ 不再触发 LineReceived
}
```

匹配逻辑:
- 查找 `##SIM_START##` 的起始索引 `simIdx`
- 截取 `simIdx + 18` 之后的内容作为 JSON 候选
- 在候选串中查找 `##SIM_END##` 的索引 `simEndIdx`
- 截取 `[0..simEndIdx]` 并 Trim 得到纯 JSON

处理流程:
1. `JsonSerializer.Deserialize<SimulationRequest>(json)` 反序列化
2. `TopologyValidator.Validate(request)` 校验拓扑
3. 若校验通过，以 `Task.Run(...)` 启动 `SimulatorEngine.Run()`，结果通过 `WriteStdin(json)` 写回 Python 进程
4. 若失败，直接写回错误 JSON

`WriteStdin` (L205-213):
```csharp
private void WriteStdin(string json)
{
    _process?.StandardInput.WriteLine(json);
    _process?.StandardInput.Flush();
}
```

#### 通道 B: 图表拦截 `##PLOT_DATA_START##` ... `##PLOT_DATA_END##` (L153-170)

```csharp
const string plotMarker = "##PLOT_DATA_START##";
int plotIdx = e.Data.IndexOf(plotMarker, StringComparison.Ordinal);
if (plotIdx >= 0)
{
    string b64 = e.Data[(plotIdx + plotMarker.Length)..];
    int plotEndIdx = b64.IndexOf("##PLOT_DATA_END##", StringComparison.Ordinal);
    if (plotEndIdx >= 0)
    {
        b64 = b64[..plotEndIdx];
        byte[] data = Convert.FromBase64String(b64);
        PlotReceived?.Invoke(data);
    }
    return;  // ★ 不再触发 LineReceived
}
```

匹配逻辑同上：打头标记 → 截取 → 找结尾标记 → 截取 → `Convert.FromBase64String()` → 触发 `PlotReceived` 事件。

#### 通道 C: 变量字典拦截 `##VAR_DATA_START##` ... `##VAR_DATA_END##` (L172-200)

```csharp
const string varMarker = "##VAR_DATA_START##";
int varIdx = e.Data.IndexOf(varMarker, StringComparison.Ordinal);
if (varIdx >= 0)
{
    string json = e.Data[(varIdx + varMarker.Length)..];
    int endIdx = json.IndexOf("##VAR_DATA_END##", StringComparison.Ordinal);
    if (endIdx >= 0)
    {
        json = json[..endIdx];
        var entries = JsonSerializer.Deserialize<List<JsonElement>>(json);
        // 逐字段提取 n/t/s/v/c → 构造 VariableEntry 列表
        VariablesReceived?.Invoke(vars);
    }
    return;
}
```

JSON 元素结构（由 Python 端产生）:
| JSON Key | C# 字段 | 含义 |
|----------|---------|------|
| `n` | `VariableEntry.Name` | 变量名 |
| `t` | `VariableEntry.Type` | 类型名 (`__name__`) |
| `s` | `VariableEntry.Size` | 长度/大小 |
| `v` | `VariableEntry.Value` | 值的 `repr()`，>80字符截断 |
| `c` | `VariableEntry.TypeColor` | 类型色标 (#455a64 默认) |

#### 通道 D: 普通文本透传 (L202)

```csharp
LineReceived?.Invoke(e.Data);  // 未命中任何标记的普通行
```

### 1.3 线程调度 (Dispatcher 机制)

**订阅位置**: `MainViewModel` 构造函数 (`Pytools/ViewModels/MainViewModel.cs`, L124-153)

所有四个事件回调均通过 `System.Windows.Application.Current.Dispatcher.Invoke(...)` 封送到 UI 线程：

```csharp
// 文本行 (L125-126)
_pythonService.LineReceived += line =>
    System.Windows.Application.Current.Dispatcher.Invoke(() => ConsoleLines.Add(line));

// 执行完成 (L127-128)
_pythonService.ExecutionFinished += () =>
    System.Windows.Application.Current.Dispatcher.Invoke(() => _executionCount++);

// 变量字典 (L129-134)
_pythonService.VariablesReceived += vars =>
    System.Windows.Application.Current.Dispatcher.Invoke(() =>
    {
        foreach (var v in vars)
            VariableList.Add(v);
    });

// Base64 图表 (L136-153)
_pythonService.PlotReceived += data =>
    System.Windows.Application.Current.Dispatcher.Invoke(() =>
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.StreamSource = new MemoryStream(data);
        bmp.EndInit();
        bmp.Freeze();
        var plot = new PlotModel
        {
            Title = $"Plot {PlotList.Count + 1}",
            ImageSource = bmp,
            RawImageData = data
        };
        PlotList.Add(plot);
        SelectedPlot = plot;
    });
```

**关键特性**:
- 无显式防抖（debounce）：每条 stdout 行被 `OutputDataReceived` 触发后立即通过 Dispatcher 排队到 UI 线程。
- `BitmapImage` 解码后立即 `Freeze()` 以允许跨线程访问。
- `RawImageData` 保留原始 `byte[]` 用于磁盘保存（`SavePlotCommand` / `SaveAllPlotsCommand` 直接 `File.WriteAllBytes`）。

---

## 模块 2：C# 离散事件仿真内核

### 2.1 类定义与核心数据结构

**文件**: `Pytools/Services/SimulatorEngine.cs`

```csharp
public class SimulatorEngine
{
    private readonly SimulationRequest _request;
    private readonly Random _rng;
    private double _currentTime;
    private long _eventIdCounter;
    private readonly PriorityQueue<SimEvent, (double Time, long EventId)> _eventQueue = new();

    private int _totalPackets;
    private readonly List<double> _allPacketDelays = new();
    private readonly Dictionary<string, List<double>> _flowDelays = new();
    private readonly Dictionary<string, int> _flowPeakQueues = new();
    private readonly Dictionary<string, double> _linkBusyUntil = new();
}
```

**构造函数** (L24-29):
```csharp
public SimulatorEngine(SimulationRequest request)
{
    _request = request;
    _rng = request.Seed.HasValue
        ? new Random(request.Seed.Value)
        : new Random();
}
```
- 若 Python 端传入 `seed` 则使用指定种子（可复现性），否则使用系统时间种子。

### 2.2 事件队列：PriorityQueue 泛型签名与排序

```csharp
PriorityQueue<SimEvent, (double Time, long EventId)>
```

- **元素类型**: `SimEvent`（包含 Type, SrcId, DstId, CurrentNodeId, PayloadSize, IntervalMean 等字段）
- **优先级类型**: `(double Time, long EventId)` —— C# 的 ValueTuple
- **排序规则**: .NET `PriorityQueue` 默认是最小堆（min-heap）。ValueTuple 的比较规则是**先比较 Time, Time 相同再比较 EventId**。因此：
  - 首先按 `scheduleTime` 升序排列（最早事件优先出队）
  - 当多个事件具有相同的 `scheduleTime` 时，按 `EventId` 升序排列（先创建的先出队），保证严格 FIFO

**入队代码** (L202):
```csharp
double scheduleTime = _currentTime + interval;
_eventQueue.Enqueue(evt, (scheduleTime, ++_eventIdCounter));
```
- `++_eventIdCounter` 是前置递增，保证每次入队的 EventId 严格单调递增。

**出队逻辑** (L43-47):
```csharp
while (_eventQueue.Count > 0 && _currentTime <= _request.SimulationTime)
{
    if (!_eventQueue.TryDequeue(out var evt, out var priority) || evt == null)
        continue;
    _currentTime = priority.Time;
    ...
}
```
- `TryDequeue` 同时获取事件和优先级元组，将 `_currentTime` 推进到出队事件的 `priority.Time`。

### 2.3 随机分布接入：MathNet.Numerics.Distributions

**NuGet 引用**: `MathNet.Numerics 5.0.0` (`Pytools.csproj` L16)

**方法**: `ScheduleNextPacket()` (L165-203)

#### Exponential 分布 —— 包间隔时间生成 (L169-170)

```csharp
double rate = 1.0 / traffic.IntervalMean;
var expDist = new Exponential(rate, _rng);
double interval = expDist.Sample();
```

- `rate` = 1 / `IntervalMean`（即 λ 参数）
- `traffic.IntervalMean` 来自用户定义的流量间隔均值（ms）
- 例如：若 `IntervalMean = 10.0`，则 `rate = 0.1`，生成均值为 10ms 的指数分布间隔

#### Normal 分布 —— 包载荷大小生成 (L173-178)

```csharp
if (traffic.PayloadVariance > 0 && traffic.PayloadMean > 0)
{
    double stddev = Math.Sqrt(traffic.PayloadVariance);
    var normDist = new Normal(traffic.PayloadMean, stddev, _rng);
    payloadSize = (int)Math.Round(normDist.Sample());
    payloadSize = Math.Max(64, payloadSize);
}
else
{
    payloadSize = Math.Max(64, traffic.PayloadMean);
}
```

- **Normal 参数**: 均值 = `traffic.PayloadMean`，标准差 = `sqrt(traffic.PayloadVariance)`
- **触发条件**: `PayloadVariance > 0 && PayloadMean > 0`，两者必须同时满足
- **下限保护**: `Math.Max(64, ...)` 确保包长不小于 64 bytes
- **退路逻辑**: 若不满足条件，直接使用 `Max(64, PayloadMean)` 作为固定大小

### 2.4 PACKET_ARRIVAL 事件：排队与时延推演

**位置**: `Run()` 方法 L49-94

#### 完整时延公式

```csharp
// Step 1: 查找链路参数 (L58-64)
string linkKey = $"{evt.SrcId}->{evt.CurrentNodeId}";
var link = _request.Links.FirstOrDefault(l =>
    (l.Src == evt.SrcId && l.Dst == evt.CurrentNodeId)
 || (l.Src == evt.CurrentNodeId && l.Dst == evt.SrcId));

double linkDelay = link?.Delay ?? 1.0;    // 传播时延 (ms), 默认 1.0
double linkBwMbps = link?.Bw ?? 100;       // 链路带宽 (Mbps), 默认 100

// Step 2: 发送时延计算 (L67-69)
double packetBits = evt.PayloadSize * 8.0;
double bandwidthBps = linkBwMbps * 1_000_000.0;
double transmitDelay = packetBits / bandwidthBps * 1000.0;

// 物理公式: transmitDelay (ms) = (PayloadSize × 8 bits) / (Bw × 10^6 bps) × 1000

// Step 3: 排队时延基于 LinkBusyUntil 推演 (L72-74)
if (!_linkBusyUntil.ContainsKey(linkKey))
    _linkBusyUntil[linkKey] = 0.0;
double queueDelay = Math.Max(0.0, _linkBusyUntil[linkKey] - _currentTime);

// Step 4: 推进链路忙碌时间轴 (L77)
_linkBusyUntil[linkKey] = _currentTime + queueDelay + transmitDelay;

// Step 5: 包总时延 = 传播延迟 + 排队时延 + 发送时延 (L80)
double totalDelay = linkDelay + queueDelay + transmitDelay;
```

**LinkBusyUntil 时间轴推进语义**:
- `_linkBusyUntil[linkKey]` 记录该链路"上次传输结束"的时间点
- 当前包到达时，若 `_linkBusyUntil > _currentTime`，差值为排队时延
- 发送完毕后，`_linkBusyUntil` 被推到 `_currentTime + queueDelay + transmitDelay`
- 下一个包到达时以此判断是否需要排队

#### 峰值队列计算 (L85-89)

```csharp
int q = (int)(queueDelay * 10);
if (!_flowPeakQueues.ContainsKey(flowKey))
    _flowPeakQueues[flowKey] = 0;
if (q > _flowPeakQueues[flowKey])
    _flowPeakQueues[flowKey] = q;
```

- `q = queueDelay × 10`：将排队时延映射为队列深度（时延每 0.1ms 对应 1 个单位）
- 取每个 `flowKey`（src→dst）的历史最大值
- 最终报告为 `peak_queue` 字段

#### 后续包调度 (L91-93)

```csharp
var traffic = _request.Traffic.First(t =>
    t.Src == evt.SrcId && t.Dst == evt.DstId);
ScheduleNextPacket(traffic, routes);
```
- 每个包到达处理后立即调度下一个包，形成自驱动的 DES 闭环。

### 2.5 路由计算：ComputeRoutes()

**方法** (L205-231):

```csharp
private Dictionary<string, Dictionary<string, string>> ComputeRoutes()
{
    // Step 1: 从 Links 中识别 Host→Router 的邻接关系
    var hostToRouter = new Dictionary<string, string>();
    foreach (var link in _request.Links)
    {
        bool srcIsHost = _request.Nodes.Any(n => n.Id == link.Src && n.Type == "Host");
        bool dstIsHost = _request.Nodes.Any(n => n.Id == link.Dst && n.Type == "Host");
        if (srcIsHost) hostToRouter[link.Src] = link.Dst;
        if (dstIsHost) hostToRouter[link.Dst] = link.Src;
    }

    // Step 2: 每个 Host 到每个目标节点的下一跳都是其唯一网关 Router
    foreach (var hostId in hostToRouter.Keys)
    {
        routes[hostId] = new Dictionary<string, string>();
        foreach (var dst in nodeIds)
        {
            if (dst == hostId) continue;
            routes[hostId][dst] = hostToRouter[hostId];
        }
    }
    return routes;
}
```

- 当前路由是**单跳模型**：每个 Host 只绑定一个 Router 作为默认网关
- 包直接发往对端 Router（`CurrentNodeId = nextHop`），不模拟多跳转发

### 2.6 最终返回结构

**`Run()` 返回值** (L153-162):

```csharp
return new Dictionary<string, object>
{
    ["status"] = "success",
    ["total_packets"] = _totalPackets,
    ["execution_time_sec"] = Math.Round(sw.Elapsed.TotalSeconds, 3),
    ["global_avg_delay_ms"] = Math.Round(globalAvg, 3),
    ["global_std_dev_ms"] = Math.Round(globalStdDev, 3),
    ["cdf_data"] = cdf_data,      // { x_delays, y_probabilities }
    ["flows"] = flows,             // Dict<flowKey, {avg_delay_ms, loss_rate, peak_queue}>
};
```

- `flows` 在 `SummaryOnly == true` 时为空字典（不输出逐流统计）
- `loss_rate` 当前恒为 0.0（未实现丢包逻辑）

---

## 模块 3：自适应 CDF 降维统计算法

**位置**: `SimulatorEngine.Run()` L97-133

### 3.1 全局统计量

```csharp
sw.Stop();                          // 停表
_allPacketDelays.Sort();            // 升序排序
int N = _allPacketDelays.Count;     // 总样本数

double globalAvg = 0.0;
double globalStdDev = 0.0;

if (N > 0)
{
    globalAvg = _allPacketDelays.Average();
    if (N > 1)
        globalStdDev = Math.Sqrt(
            _allPacketDelays.Average(d => Math.Pow(d - globalAvg, 2))
        );
}
```
- 标准差使用**总体标准差**公式（除以 N，非 N-1）

### 3.2 N ≤ 100：全量模式

```csharp
if (N <= 100)
{
    x_delays.AddRange(_allPacketDelays);       // 全部排序后的时延值
    for (int i = 0; i < N; i++)
        y_probabilities.Add((double)(i + 1) / N);  // CDF = (i+1)/N
}
```

- x 轴：所有原始时延数据点（升序）
- y 轴：`(i+1)/N`（经验累积概率，范围 ~(1/N) 到 1.0）
- 点数 = N 个

### 3.3 N > 100：101 分位点降维模式

```csharp
else
{
    for (int p = 0; p <= 100; p++)
    {
        double quantile = p / 100.0;              // 0.00, 0.01, 0.02, ..., 1.00
        int idx = (int)Math.Round(quantile * (N - 1));
        if (idx < 0) idx = 0;                     // 下界保护
        if (idx >= N) idx = N - 1;                // 上界保护
        x_delays.Add(_allPacketDelays[idx]);
        y_probabilities.Add(quantile);
    }
}
```

- **步长**: `p / 100.0` → 101 个等距分位点 (0%, 1%, 2%, ..., 100%)
- **索引公式**: `idx = Round(quantile × (N - 1))` —— 使用 `Math.Round` 做四舍五入取整
- **防越界**: `idx < 0 → 0`, `idx >= N → N-1`
- **x 轴映射**: `_allPacketDelays[idx]`（取自已排序数组的对应分位值）
- **y 轴映射**: `quantile`（0.0 到 1.0 的累积概率）

### 3.4 CDF 结果组装

```csharp
var cdf_data = new { x_delays, y_probabilities };
```
- 匿名类型，序列化后变为 JSON: `{"x_delays": [...], "y_probabilities": [...]}`
- 这是传给 Python 端 `_print_report` 和 CDF 绘图的数据源

---

## 模块 4：Python 端 SDK 与绘图接管

### 4.1 Python SDK: pytools_sim.py

**文件**: `Pytools/SDK/pytools_sim.py`

**类层次**:
```
Node (base) → Host, Router
Link
Traffic (构造函数拦截 TCP)
Simulator (编排器)
```

#### Traffic 类 TCP 禁令 (L53-54)

```python
if type.upper() == "TCP":
    raise ValueError("当前版本暂不支持 TCP 协议。请使用 UDP。")
```

#### Simulator.run() 构造的真实 JSON 结构 (L91-119)

```python
def run(self, simulation_time: int, seed: Optional[int] = None,
        summary_only: bool = False) -> dict:

    payload = {
        "simulation_time": simulation_time,
        "seed": seed,
        "summary_only": summary_only,
        "nodes": [
            {"id": n.id, "type": n.__class__.__name__} for n in self.nodes
        ],
        "links": [
            {"src": l.src, "dst": l.dst, "bw": l.bw, "delay": l.delay}
            for l in self.links
        ],
        "traffic": [
            {
                "src": t.src,
                "dst": t.dst,
                "type": t.type,
                "interval_mean": t.interval_mean,
                "payload_mean": t.payload_mean,
                "payload_variance": t.payload_variance,
            }
            for t in self.traffics
        ],
    }

    json_string = json.dumps(payload, ensure_ascii=False)
    print(f"##SIM_START## {json_string} ##SIM_END##", flush=True)

    response_str = sys.stdin.readline()
    result = json.loads(response_str)

    _print_report(result, simulation_time, summary_only)

    if "cdf_data" in result and result["cdf_data"]:
        cdf = result["cdf_data"]
        x_delays = cdf.get("x_delays", [])
        y_probs = cdf.get("y_probabilities", [])
        if x_delays and y_probs:
            plt.figure(figsize=(8, 5))
            plt.plot(x_delays, y_probs, marker=".", linestyle="-",
                     color="#1f77b4", linewidth=2)
            plt.title("全局时延累积分布函数 (CDF)", fontsize=12, fontweight="bold")
            plt.xlabel("时延 (ms)", fontsize=10)
            plt.ylabel("累积概率 (Probability)", fontsize=10)
            plt.grid(True, linestyle="--", alpha=0.7)
            plt.tight_layout()
            plt.show()   → 触发被劫持的 _pytools_show

    return _translate_keys(result)
```

**实际传输的 JSON 字段一览**:

| 顶层 Key | 类型 | 来源 |
|----------|------|------|
| `simulation_time` | `int` | `run()` 参数 |
| `seed` | `int \| null` | `run()` 参数 |
| `summary_only` | `bool` | `run()` 参数 |
| `nodes` | `list[{"id","type"}]` | `self.nodes` 列表 |
| `links` | `list[{"src","dst","bw","delay"}]` | `self.links` 列表 |
| `traffic` | `list[{"src","dst","type","interval_mean","payload_mean","payload_variance"}]` | `self.traffics` 列表 |

**字段名已发生变更**（与旧设计文档对比）:
- `nodes` 的 `type` 值是 `n.__class__.__name__`，即 `"Host"` 或 `"Router"`
- `traffic` 不含 `packet_size`，改为 `payload_mean` + `payload_variance`
- `traffic` 不含 `interval`，改为 `interval_mean`
- 新增 `summary_only` 控制开关

#### _translate_keys 中文字段映射 (L159-189)

C# 英文 key → 中文显示名:
| 英文 Key | 中文名 |
|----------|--------|
| `status` | 状态 |
| `total_packets` | 总发包数 |
| `execution_time_sec` | 内核真实耗时（s） |
| `global_avg_delay_ms` | 全局平均时延（ms） |
| `global_std_dev_ms` | 时延标准差（ms） |
| `flows` | 流量明细 |
| `avg_delay_ms` (flows内) | 平均时延（ms） |
| `loss_rate` (flows内) | 丢包率 |
| `peak_queue` (flows内) | 队列峰值 |

### 4.2 Python Wrapper 脚本（运行时动态生成）

**生成位置**: `PythonExecutionService.BuildWrapperScript()` (L257-355)

**文件生命周期**: 创建于 `Path.GetTempPath()/pytools_run_{Guid:N}.py`，执行完毕后 `File.Delete(wrapperPath)` 清理。

#### matplotlib 后端接管 (L288-303)

```python
import base64 as _base64, io as _io
import matplotlib
matplotlib.use('Agg')           # ★ 强制非交互式 Agg 后端
import matplotlib.pyplot as _plt

def _pytools_show(*_args, **_kwargs):
    for _fignum in _plt.get_fignums():
        _fig = _plt.figure(_fignum)
        _buf = _io.BytesIO()
        _fig.savefig(_buf, format='png', dpi=100, bbox_inches='tight')
        _buf.seek(0)
        _b64 = _base64.b64encode(_buf.read()).decode('ascii')
        print('##PLOT_DATA_START##' + _b64 + '##PLOT_DATA_END##')
        _buf.close()
        _plt.close(_fig)
_plt.show = _pytools_show          # ★ 猴子补丁替换 plt.show()
```

**实际行为**:
- `matplotlib.use('Agg')` 在 `import pyplot` 之前调用，禁止任何 GUI 窗口
- 将所有 `plt.show()` 调用劫持为 `_pytools_show()`
- 每个 Figure 走 `savefig(BytesIO, format='png', dpi=100)` → Base64 编码 → 打印到 stdout

#### 中文"豆腐块"字体处理 —— 当前代码状态

**实际代码中不存在任何中文字体配置。** 在 `BuildWrapperScript` 的整个字符串生成中，没有：
- `matplotlib.rcParams['font.sans-serif'] = ['SimHei']` 
- `matplotlib.rcParams['axes.unicode_minus'] = False`
- 任何 `FontProperties` 或 `font_manager` 调用

这意味着 CDF 图中的中文标题/轴标签在当前实现下依赖系统默认字体。在 Windows 上使用 `Agg` 后端时，如无中文字体配置，matplotlib 会使用 DejaVu Sans 等西文字体，中文部分将显示为"豆腐块"。这是**当前代码的一个已知间隙**。

#### 百分比纵轴处理 —— 当前代码状态

**实际代码中不存在百分比纵轴处理。** `pytools_sim.py` 的 CDF 绘图代码 (L146-154) 使用：
```python
plt.ylabel("累积概率 (Probability)", fontsize=10)
```
y 值范围为 0.0 到 1.0，没有 `PercentFormatter` 或 `plt.gca().yaxis.set_major_formatter(...)` 调用。纵轴显示的是原始小数（0.0 ~ 1.0），而非百分比（0% ~ 100%）。

#### 变量区数据收集 (L311-348)

```python
_result = []
for _name, _val in list(globals().items()):
    # 过滤：__dunder__ 变量、框架注入变量、模块/函数/内置函数类型
    if _name.startswith('__') and _name.endswith('__'): continue
    if _name in ('sys', 'os', 'json', 'types', 'traceback',
                 '_f', '_user_script', '_result', '_name', '_val'): continue
    if _name in _ide_boot_snapshots: continue
    _t = type(_val)
    if _t in (types.ModuleType, types.FunctionType, types.BuiltinFunctionType): continue

    # 类型映射到色标
    _color = '#455a64'                              # 默认色
    if isinstance(_val, bool): _color = '#7b1fa2'   # 紫色
    elif isinstance(_val, (int, float)): _color = '#b56a24'   # 棕色
    elif isinstance(_val, str): _color = '#388e3c'   # 绿色
    elif isinstance(_val, (list, dict, tuple, set)): _color = '#1565c0'  # 蓝色

    # 大小：序列类型取 len()，标量取 '1'
    if isinstance(_val, (list, tuple, set, dict, str)): _size = str(len(_val))
    else: _size = '1'

    _vstr = repr(_val)
    if len(_vstr) > 80: _vstr = _vstr[:77] + '...'  # 截断

    _result.append({'n': _name, 't': _tname, 's': _size, 'v': _vstr, 'c': _color})

print('##VAR_DATA_START##' + json.dumps(_result) + '##VAR_DATA_END##')
```

- 编码声明清除 (L280-283): 用户脚本中的 `coding:` 行被替换为换行，避免 `exec` 时语法错误
- `_ide_boot_snapshots`: 记录包装脚本注入后、用户代码执行前的全局快照，用于排除框架注入变量
- 用户代码通过 `exec(compile(_cleaned, _user_script, 'exec'), globals())` 在同一个全局命名空间执行

---

## 模块 5：WPF 宿主与 IDE 基础架构

### 5.1 MVVM 数据流核心

**ViewModel**: `MainViewModel` (`Pytools/ViewModels/MainViewModel.cs`)  
**实现接口**: `INotifyPropertyChanged`

#### 关键 ObservableCollection

```csharp
public ObservableCollection<string> ConsoleLines { get; } = new();        // 控制台输出行
public ObservableCollection<VariableEntry> VariableList { get; } = new();  // 变量视图
public ObservableCollection<PlotModel> PlotList { get; } = new();          // 图表列表
public ObservableCollection<DocumentModel> Documents { get; } = new();     // 文档标签页
```

#### Model 核心字段

**`DocumentModel`** (`Pytools/Models/DocumentModel.cs`):
```csharp
public class DocumentModel : INotifyPropertyChanged
{
    private string _title = "";       // 标签页标题 → WindowTitle 依赖
    private string _content = "";     // 编辑器文本内容 ↔ AvalonEdit.Text
    private string _filePath = "";    // 磁盘路径 → ActiveFilePath 依赖
}
```

**`VariableEntry`** (`Pytools/Models/VariableEntry.cs`):
```csharp
public class VariableEntry
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string Size { get; set; } = "";
    public string Value { get; set; } = "";
    public string TypeColor { get; set; } = "#455a64";
}
```

**`PlotModel`** (`Pytools/Models/PlotModel.cs`):
```csharp
public class PlotModel
{
    public BitmapImage ImageSource { get; set; } = new();   // WPF 图像源
    public string Title { get; set; } = "";
    public byte[] RawImageData { get; set; } = Array.Empty<byte>();  // 原始 PNG 字节
}
```

#### ActiveDocument 选择逻辑 (L38-58)

```csharp
public DocumentModel? ActiveDocument
{
    get => _activeDocument;
    set
    {
        if (_activeDocument == value) return;          // 防重复
        if (_activeDocument != null)
            _activeDocument.PropertyChanged -= OnActiveDocumentPropertyChanged;
        _activeDocument = value;
        if (_activeDocument != null)
            _activeDocument.PropertyChanged += OnActiveDocumentPropertyChanged;
        OnPropertyChanged();
        OnPropertyChanged(nameof(WindowTitle));
        OnPropertyChanged(nameof(CodeContent));
        OnPropertyChanged(nameof(ActiveFilePath));
    }
}
```

- 切换时先退订旧文档的 `PropertyChanged`，再订阅新文档
- 同时触发三个派生属性的变更通知

#### Run 命令执行流程 (L277-313)

```csharp
private void ExecuteRun()
{
    // 1. 若未保存 → 先保存
    // 2. File.WriteAllText 落盘
    // 3. 打印 Console header
    // 4. VariableList.Clear(); PlotList.Clear(); SelectedPlot = null;
    // 5. _ = _pythonService.ExecuteAsync(ActiveDocument.FilePath);  // 发射后不管
}
```

### 5.2 文件系统监听：FileWatcherService

**文件**: `Pytools/Services/FileWatcherService.cs`

```csharp
public class FileWatcherService : IDisposable
{
    private FileSystemWatcher? _watcher;
    private System.Timers.Timer? _debounceTimer;
    private readonly string _filter;      // "*.py"
    private readonly int _debounceMs;     // 200

    public event Action? FilesChanged;
}
```

**构造参数** (MainViewModel L121):
```csharp
_fileWatcher = new FileWatcherService("*.py", 200);
```

**防抖机制** (L61-73):
```csharp
private void OnFileChanged(object sender, FileSystemEventArgs e)
{
    _debounceTimer?.Stop();    // ★ 重置计时器
    _debounceTimer?.Start();   // ★ 重新开始 200ms 倒计时
}

private void OnDebounceElapsed(object? sender, System.Timers.ElapsedEventArgs e)
{
    var dispatcher = System.Windows.Application.Current?.Dispatcher;
    if (dispatcher == null) return;
    dispatcher.Invoke(() => FilesChanged?.Invoke());   // ★ UI 线程触发刷新
}
```

- `FileSystemWatcher` 监听: `FileName | LastWrite`，递归子目录
- 每次文件变更事件会**重置** Timer（防抖）
- Timer 到期后通过 `Dispatcher.Invoke` 回到 UI 线程触发 `TreeRefreshRequested`
- `MainViewModel` 订阅该事件调用 `LoadDirectory(CurrentRootPath)` 重建完整目录树

### 5.3 AvalonEdit 编辑器双向同步

**文件**: `Pytools/Views/MainWindow.xaml.cs`

#### 编辑器创建 (L62-73)

```csharp
var editor = new TextEditor
{
    FontFamily = new FontFamily("Consolas"),
    FontSize = 13,
    ShowLineNumbers = true,
    SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("Python"),
    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
    Text = doc.Content       // 初始文本来自 DocumentModel
};
```

#### 防死循环双向同步

**方向 1: AvalonEdit → DocumentModel** (L74)
```csharp
editor.TextChanged += (s, e) => doc.Content = editor.Text;
```
用户打字时实时写入 Model。

**方向 2: DocumentModel → AvalonEdit** (L76-80)
```csharp
doc.PropertyChanged += (s, e) =>
{
    if (e.PropertyName == nameof(DocumentModel.Content) && editor.Text != doc.Content)
        editor.Text = doc.Content;
};
```
Model 变化时回写编辑器——关键防护：`editor.Text != doc.Content` 检查避免循环触发。

**注意**: 这仍然存在竞争窗口——若 `doc.Content` 被外部多次快速修改，`editor.Text != doc.Content` 在前一次 `TextChanged` 尚未完成时可能失效。但对于 IDE 场景的单用户编辑已足够。

### 5.4 AvalonDock LayoutDocument 生命周期

**文件**: `Pytools/Views/MainWindow.xaml.cs`

#### 新建文档 (L62-115: AddTab)

```csharp
private void AddTab(DocumentModel doc)
{
    // 1. 创建 AvalonEdit TextEditor + 语法高亮
    // 2. 建立双向绑定（见 5.3）
    // 3. 创建 LayoutDocument { Title = doc.Title, Content = editor }
    // 4. 绑定 Title 同步事件
    // 5. 绑定 IsActiveChanged → _viewModel.ActiveDocument = doc (带 _isSyncingActive 防护)
    // 6. 绑定 Closed → _docToTab.Remove + Documents.Remove
    // 7. _docToTab[doc] = layoutDoc; DocumentPane.Children.Add(layoutDoc);
    // 8. layoutDoc.IsActive = true
}
```

#### IsActive 同步防护 (L94-103, L126-143)

**双重 `_isSyncingActive` 防护**:

方向 Tab→VM (L94-103):
```csharp
layoutDoc.IsActiveChanged += (s, e) =>
{
    if (_isSyncingActive) return;      // ★ 防循环
    if (layoutDoc.IsActive)
    {
        _isSyncingActive = true;
        _viewModel.ActiveDocument = doc;
        _isSyncingActive = false;
    }
};
```

方向 VM→Tab (L126-143):
```csharp
// OnViewModelPropertyChanged
if (e.PropertyName == "ActiveDocument")
{
    if (_isSyncingActive) return;      // ★ 防循环
    if (_viewModel.ActiveDocument != null
        && _docToTab.TryGetValue(_viewModel.ActiveDocument, out var target))
    {
        _isSyncingActive = true;
        target.IsActive = true;
        _isSyncingActive = false;
    }
}
```

#### 关闭文档 (L105-109)
```csharp
layoutDoc.Closed += (s, e) =>
{
    _docToTab.Remove(doc);
    _viewModel.Documents.Remove(doc);
};
```

#### 文档集合变更 (L51-60)
```csharp
private void OnDocumentsCollectionChanged(...)
{
    // e.NewItems → 逐个 AddTab
    // e.OldItems → 逐个 RemoveTab
}
```

### 5.5 控制台自动滚动 (L34-41)

```csharp
_viewModel.ConsoleLines.CollectionChanged += (s, e) =>
{
    Dispatcher.BeginInvoke(new Action(() =>
    {
        if (ConsoleListBox.Items.Count > 0)
            ConsoleListBox.ScrollIntoView(ConsoleListBox.Items[^1]);
    }));
};
```
- 使用 `Dispatcher.BeginInvoke`（异步，优先级低于同步 Invoke）
- 每次 ConsoleLines 新增行后，自动滚动到最后一个元素

### 5.6 项目树双击打开 (L193-203)

```csharp
private void ProjectTreeView_SelectedItemChanged(...)
{
    var selectedItem = ProjectTreeView.SelectedItem as TreeViewItem;
    string? fullPath = selectedItem.Tag as string;
    if (File.Exists(fullPath))
        _viewModel.OpenOrActivateDocument(fullPath);  // 已存在则激活，否则新建
}
```

### 5.7 图表保存功能 (MainViewModel L315-357)

**SavePlotCommand** (L315-336): 将 `SelectedPlot.RawImageData` 写入用户选择的 `.png` 文件。

**SaveAllPlotsCommand** (L338-357): 将 `PlotList` 中所有图表批量写入用户选择的文件夹，命名 `Plot_1.png`, `Plot_2.png`, ...。

### 5.8 技术栈总结

| 组件 | 技术 | 版本 |
|------|------|------|
| 运行时 | .NET 8.0 Windows (WPF) | net8.0-windows |
| 代码编辑器 | AvalonEdit | 6.3.1.120 |
| 停靠布局 | AvalonDock (Dirkster) | 4.74.1 |
| 主题 | AvalonDock.Themes.VS2013 | 4.74.1 |
| UI 样式 | MaterialDesignThemes | 5.3.2 |
| UI 框架 | ModernWpfUI | 0.9.6 |
| 数学库 | MathNet.Numerics | 5.0.0 |

---

## 附录: 完整文件索引

| 文件 | 所属模块 | 核心职责 |
|------|----------|----------|
| `Pytools/Services/PythonExecutionService.cs` | IPC | Python 进程管理 + stdout 三通道拦截 + 包装脚本生成 |
| `Pytools/Services/SimulatorEngine.cs` | DES 内核 | 离散事件仿真 + 排队推演 + CDF 统计 |
| `Pytools/Services/TopologyValidator.cs` | 校验 | 拓扑规则校验（Host 直连禁令 + 单网关约束） |
| `Pytools/Services/FileWatcherService.cs` | 文件系统 | 防抖文件监听 + 触发目录树刷新 |
| `Pytools/ViewModels/MainViewModel.cs` | MVVM | 主视图模型 + Dispatcher 调度 + 命令绑定 |
| `Pytools/Views/MainWindow.xaml.cs` | View | AvalonDock 布局 + AvalonEdit 同步 + 目录树 |
| `Pytools/Models/Simulation/SimulationRequest.cs` | Model | 仿真请求 DTO (nodes, links, traffic) |
| `Pytools/Models/Simulation/NodeData.cs` | Model | 节点 DTO (id, type) |
| `Pytools/Models/Simulation/LinkData.cs` | Model | 链路 DTO (src, dst, bw, delay) |
| `Pytools/Models/Simulation/TrafficData.cs` | Model | 流量 DTO (src, dst, type, interval_mean, payload_mean, payload_variance) |
| `Pytools/Models/Simulation/SimEvent.cs` | Model | 仿真事件 DTO |
| `Pytools/Models/DocumentModel.cs` | Model | 文档模型 (title, content, filePath) |
| `Pytools/Models/VariableEntry.cs` | Model | 变量条目 (name, type, size, value, color) |
| `Pytools/Models/PlotModel.cs` | Model | 图表模型 (ImageSource, title, RawImageData) |
| `Pytools/Commands/RelayCommand.cs` | 基础设施 | ICommand 实现 |
| `Pytools/SDK/pytools_sim.py` | Python SDK | Simulator.run() JSON 构造 + CDF 绘图 + 键名中英翻译 |
