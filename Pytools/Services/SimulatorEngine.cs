using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using MathNet.Numerics.Distributions;
using Pytools.Models.Simulation;

namespace Pytools.Services
{
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
        private readonly Dictionary<string, double> _linkBusyUntil_High = new();
        private readonly Dictionary<string, double> _linkBusyUntil_Low = new();
        private readonly Dictionary<string, int> _flowSent = new();
        private readonly Dictionary<string, int> _flowDropped = new();
        private readonly Dictionary<string, long> _flowReceivedBytes = new();
        private readonly Dictionary<string, double> _flowLastArrivalTime = new();
        private readonly Dictionary<string, double> _flowJitterSum = new();
        private readonly Dictionary<string, int> _flowReceivedCount = new();
        private readonly Dictionary<string, int> _flowPeakCapacityBytes = new();

        public SimulatorEngine(SimulationRequest request)
        {
            _request = request;
            _rng = request.Seed.HasValue
                ? new Random(request.Seed.Value)
                : new Random();
        }

        public Dictionary<string, object> Run()
        {
            var sw = Stopwatch.StartNew();
            var routes = ComputeRoutes();

            foreach (var link in _request.Links)
            {
                _linkBusyUntil_High[$"{link.Src}->{link.Dst}"] = 0.0;
                _linkBusyUntil_High[$"{link.Dst}->{link.Src}"] = 0.0;
                _linkBusyUntil_Low[$"{link.Src}->{link.Dst}"] = 0.0;
                _linkBusyUntil_Low[$"{link.Dst}->{link.Src}"] = 0.0;
            }

            foreach (var t in _request.Traffic)
                ScheduleNextPacket(t, routes);

            while (_eventQueue.Count > 0 && _currentTime <= _request.SimulationTime)
            {
                if (!_eventQueue.TryDequeue(out var evt, out var priority) || evt == null)
                    continue;
                _currentTime = priority.Time;

                if (evt.Type == "PACKET_ARRIVAL")
                {
                    string flowKey = $"{evt.SrcId}->{evt.DstId}";
                    if (!_flowDelays.ContainsKey(flowKey))
                        _flowDelays[flowKey] = new List<double>();

                    // 有向发送端口：FromNodeId→CurrentNodeId，与反向端口物理隔离
                    string txPortKey = $"{evt.FromNodeId}->{evt.CurrentNodeId}";
                    // 优先严格有向匹配（AccessLink 非对称参数），未命中则反向兼容（TrunkLink）
                    var link = _request.Links.FirstOrDefault(l =>
                        l.Src == evt.FromNodeId && l.Dst == evt.CurrentNodeId);
                    if (link == null)
                    {
                        link = _request.Links.FirstOrDefault(l =>
                            l.Src == evt.CurrentNodeId && l.Dst == evt.FromNodeId);
                    }

                    double linkDelay = link?.Delay ?? 1.0;
                    double linkBwMbps = link?.Bw ?? 100;

                    // 发送时延 (ms): packet bits / link bps * 1000
                    double packetBits = evt.PayloadSize * 8.0;
                    double bandwidthBps = linkBwMbps * 1_000_000.0;
                    double transmitDelay = packetBits / bandwidthBps * 1000.0;

                    // ============ Step 5 & 6: 二维虚拟排队与双轴推进 ============
                    // 兜底初始化（防备动态生成的未知端口）
                    if (!_linkBusyUntil_High.ContainsKey(txPortKey))
                    {
                        _linkBusyUntil_High[txPortKey] = 0.0;
                        _linkBusyUntil_Low[txPortKey] = 0.0;
                    }

                    double queueDelay = 0.0;
                    if (evt.QosLevel == 1) // ★ 高优先级 (High)
                    {
                        queueDelay = Math.Max(0.0, _linkBusyUntil_High[txPortKey] - _currentTime);
                        double completionTime = _currentTime + queueDelay + transmitDelay;

                        // 1. 推进高优时间轴
                        _linkBusyUntil_High[txPortKey] = completionTime;

                        // 2. ★ 核心魔法：高优包霸权插队，强制顺延低优时间轴
                        _linkBusyUntil_Low[txPortKey] = Math.Max(_linkBusyUntil_Low[txPortKey], completionTime);
                    }
                    else // ★ 低优先级 (Low - 默认)
                    {
                        queueDelay = Math.Max(0.0, _linkBusyUntil_Low[txPortKey] - _currentTime);
                        double completionTime = _currentTime + queueDelay + transmitDelay;

                        // 仅推进低优时间轴
                        _linkBusyUntil_Low[txPortKey] = completionTime;
                    }

                    // ★ 只有从流量源发出的第一跳才调度下一个新包，中间路由器转发不触发
                    bool isFirstHop = evt.FromNodeId == evt.SrcId;
                    if (isFirstHop)
                    {
                        _totalPackets++;
                        _flowSent[flowKey] = _flowSent.GetValueOrDefault(flowKey, 0) + 1;
                        var traffic = _request.Traffic.First(t =>
                            t.Src == evt.SrcId && t.Dst == evt.DstId);
                        ScheduleNextPacket(traffic, routes);
                    }

                    // 三层丢包屏障（每跳独立判定）
                    double dropRate = link?.DropRate ?? 0.0;
                    int maxQBytes = link?.MaxQueueBytes ?? 15360;
                    double maxTime = link?.MaxTimeoutMs ?? 50.0;

                    // 反推历史积压物理字节数 + 当前包形成瞬时队列深度
                    double backlogBytes = queueDelay * linkBwMbps * 125.0;
                    int currentQueueBytes = (int)backlogBytes + evt.PayloadSize;

                    bool isDropped = _rng.NextDouble() < dropRate;
                    if (!isDropped && currentQueueBytes > maxQBytes)
                        isDropped = true;
                    if (!isDropped && queueDelay > maxTime)
                        isDropped = true;

                    if (isDropped)
                    {
                        _flowDropped[flowKey] = _flowDropped.GetValueOrDefault(flowKey, 0) + 1;
                        continue;
                    }

                    double hopDelay = linkDelay + queueDelay + transmitDelay;

                    // 峰值队列（每跳参与比较，使用真实物理字节数）
                    if (!_flowPeakQueues.ContainsKey(flowKey))
                        _flowPeakQueues[flowKey] = 0;
                    if (currentQueueBytes > _flowPeakQueues[flowKey])
                    {
                        _flowPeakQueues[flowKey] = currentQueueBytes;
                        _flowPeakCapacityBytes[flowKey] = maxQBytes;
                    }

                    if (evt.CurrentNodeId == evt.DstId)
                    {
                        double endToEndDelay = (_currentTime + hopDelay) - evt.CreationTime;
                        _allPacketDelays.Add(endToEndDelay);
                        _flowDelays[flowKey].Add(endToEndDelay);

                        _flowReceivedBytes[flowKey] = _flowReceivedBytes.GetValueOrDefault(flowKey, 0) + evt.PayloadSize;
                        int rcvd = _flowReceivedCount.GetValueOrDefault(flowKey, 0);
                        if (rcvd > 0)
                        {
                            double jitter = Math.Abs(_currentTime - _flowLastArrivalTime[flowKey]);
                            _flowJitterSum[flowKey] = _flowJitterSum.GetValueOrDefault(flowKey, 0) + jitter;
                        }
                        _flowLastArrivalTime[flowKey] = _currentTime;
                        _flowReceivedCount[flowKey] = rcvd + 1;
                    }
                    else
                    {
                        // ★ 需要继续转发：查路由表找下一跳
                        if (routes.TryGetValue(evt.CurrentNodeId, out var nextHops)
                            && nextHops.TryGetValue(evt.DstId, out string? nextHopId)
                            && nextHopId != null)
                        {
                            var forwardEvent = new SimEvent
                            {
                                Type = "PACKET_ARRIVAL",
                                SrcId = evt.SrcId,
                                DstId = evt.DstId,
                                CurrentNodeId = nextHopId,
                                PayloadSize = evt.PayloadSize,
                                IntervalMean = evt.IntervalMean,
                                PayloadMean = evt.PayloadMean,
                                PayloadVariance = evt.PayloadVariance,
                                CreationTime = evt.CreationTime,
                                FromNodeId = evt.CurrentNodeId,
                            };
                            _eventQueue.Enqueue(forwardEvent,
                                (_currentTime + hopDelay, ++_eventIdCounter));
                        }
                        // else: 路由不可达，包被静默丢弃
                    }
                }
            }

            sw.Stop();

            _allPacketDelays.Sort();
            int N = _allPacketDelays.Count;

            double globalAvg = 0.0;
            double globalStdDev = 0.0;
            var x_delays = new List<double>();
            var y_probabilities = new List<double>();

            if (N > 0)
            {
                globalAvg = _allPacketDelays.Average();
                if (N > 1)
                    globalStdDev = Math.Sqrt(_allPacketDelays.Average(d => Math.Pow(d - globalAvg, 2)));

                if (N <= 100)
                {
                    x_delays.AddRange(_allPacketDelays);
                    for (int i = 0; i < N; i++)
                        y_probabilities.Add((double)(i + 1) / N);
                }
                else
                {
                    for (int p = 0; p <= 100; p++)
                    {
                        double quantile = p / 100.0;
                        int idx = (int)Math.Round(quantile * (N - 1));
                        if (idx < 0) idx = 0;
                        if (idx >= N) idx = N - 1;
                        x_delays.Add(_allPacketDelays[idx]);
                        y_probabilities.Add(quantile);
                    }
                }
            }

            var cdf_data = new { x_delays, y_probabilities };

            // summary_only 分支: 空 flows vs 详细 flows
            var flows = new Dictionary<string, object>();
            if (!_request.SummaryOnly)
            {
                foreach (var kv in _flowDelays)
                {
                    double avgDelay = kv.Value.Count > 0 ? kv.Value.Average() : 0.0;
                    int sent = _flowSent.GetValueOrDefault(kv.Key, 0);
                    int dropped = _flowDropped.GetValueOrDefault(kv.Key, 0);
                    double actualLossRate = sent > 0 ? (double)dropped / sent : 0.0;
                    int peakQueue = _flowPeakQueues.GetValueOrDefault(kv.Key, 0);
                    int peakCapacity = _flowPeakCapacityBytes.GetValueOrDefault(kv.Key, 15360);
                    long receivedBytes = _flowReceivedBytes.GetValueOrDefault(kv.Key, 0);
                    int count = _flowReceivedCount.GetValueOrDefault(kv.Key, 0);
                    double jitterMs = count > 1
                        ? _flowJitterSum.GetValueOrDefault(kv.Key, 0) / (count - 1)
                        : 0.0;
                    double throughputKbps = (receivedBytes * 8.0)
                        / (_request.SimulationTime / 1000.0) / 1000.0;

                    flows[kv.Key] = new
                    {
                        avg_delay_ms = Math.Round(avgDelay, 3),
                        loss_rate = Math.Round(actualLossRate, 4),
                        peak_queue = peakQueue,
                        throughput_kbps = Math.Round(throughputKbps, 2),
                        jitter_ms = Math.Round(jitterMs, 3),
                        peak_capacity = peakCapacity,
                    };
                }
            }

            return new Dictionary<string, object>
            {
                ["status"] = "success",
                ["total_packets"] = _totalPackets,
                ["execution_time_ms"] = sw.ElapsedMilliseconds,
                ["global_avg_delay_ms"] = Math.Round(globalAvg, 3),
                ["global_std_dev_ms"] = Math.Round(globalStdDev, 3),
                ["cdf_data"] = cdf_data,
                ["flows"] = flows,
            };
        }

        private void ScheduleNextPacket(TrafficData traffic,
            Dictionary<string, Dictionary<string, string>> routes)
        {
            double rate = 1.0 / traffic.IntervalMean;
            var expDist = new Exponential(rate, _rng);
            double interval = expDist.Sample();

            int payloadSize;
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

            string nextHop = routes.ContainsKey(traffic.Src)
                ? routes[traffic.Src][traffic.Dst]
                : traffic.Dst;

            double scheduleTime = _currentTime + interval;

            var evt = new SimEvent
            {
                Type = "PACKET_ARRIVAL",
                SrcId = traffic.Src,
                DstId = traffic.Dst,
                CurrentNodeId = nextHop,
                PayloadSize = payloadSize,
                IntervalMean = traffic.IntervalMean,
                PayloadMean = traffic.PayloadMean,
                PayloadVariance = traffic.PayloadVariance,
                CreationTime = scheduleTime,
                FromNodeId = traffic.Src,
            };

            _eventQueue.Enqueue(evt, (scheduleTime, ++_eventIdCounter));
        }

        private Dictionary<string, Dictionary<string, string>> ComputeRoutes()
        {
            var routes = new Dictionary<string, Dictionary<string, string>>();
            var nodeIds = _request.Nodes.Select(n => n.Id).ToHashSet();
            var hostIds = _request.Nodes.Where(n => n.Type == "Host").Select(n => n.Id).ToHashSet();

            // 构建无向加权邻接表: 权重 = link.Delay (最小 1.0 防错)
            var adj = new Dictionary<string, List<(string Neighbor, double Weight)>>();
            foreach (var nodeId in nodeIds)
                adj[nodeId] = new List<(string, double)>();

            foreach (var link in _request.Links)
            {
                double w = link.Delay > 0 ? link.Delay : 1.0;
                adj[link.Src].Add((link.Dst, w));
                adj[link.Dst].Add((link.Src, w));
            }

            // Dijkstra: 对每个源节点计算到全图的最短路径
            foreach (var source in nodeIds)
            {
                var dist = new Dictionary<string, double>();
                var prev = new Dictionary<string, string>();

                foreach (var v in nodeIds)
                {
                    dist[v] = double.PositiveInfinity;
                    prev[v] = "";
                }
                dist[source] = 0;

                var pq = new PriorityQueue<string, double>();
                pq.Enqueue(source, 0);

                while (pq.Count > 0)
                {
                    if (!pq.TryDequeue(out var u, out var d))
                        continue;
                    if (d > dist[u])
                        continue;

                    // Host 不能作为中间转发节点（但作为 source 时可向外松弛）
                    if (u != source && hostIds.Contains(u))
                        continue;

                    foreach (var (v, w) in adj[u])
                    {
                        double alt = dist[u] + w;
                        if (alt < dist[v])
                        {
                            dist[v] = alt;
                            prev[v] = u;
                            pq.Enqueue(v, alt);
                        }
                    }
                }

                routes[source] = new Dictionary<string, string>();
                foreach (var dest in nodeIds)
                {
                    if (dest == source || double.IsInfinity(dist[dest]))
                        continue;

                    // 回溯: 从 dest 沿 prev 链走到 source 的前驱，即下一跳
                    string curr = dest;
                    while (prev[curr] != source && prev[curr] != "")
                        curr = prev[curr];

                    if (prev[curr] == source)
                        routes[source][dest] = curr;
                }
            }

            return routes;
        }
    }
}
