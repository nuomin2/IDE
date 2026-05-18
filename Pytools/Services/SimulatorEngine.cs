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
        private readonly Dictionary<string, double> _linkBusyUntil = new();
        private readonly Dictionary<string, int> _flowSent = new();
        private readonly Dictionary<string, int> _flowDropped = new();

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
                _linkBusyUntil[$"{link.Src}->{link.Dst}"] = 0.0;

            foreach (var t in _request.Traffic)
                ScheduleNextPacket(t, routes);

            while (_eventQueue.Count > 0 && _currentTime <= _request.SimulationTime)
            {
                if (!_eventQueue.TryDequeue(out var evt, out var priority) || evt == null)
                    continue;
                _currentTime = priority.Time;

                if (evt.Type == "PACKET_ARRIVAL")
                {
                    _totalPackets++;

                    string flowKey = $"{evt.SrcId}->{evt.DstId}";
                    if (!_flowDelays.ContainsKey(flowKey))
                        _flowDelays[flowKey] = new List<double>();

                    // 找到本包经过的链路: SrcId → CurrentNodeId
                    string linkKey = $"{evt.SrcId}->{evt.CurrentNodeId}";
                    var link = _request.Links.FirstOrDefault(l =>
                        (l.Src == evt.SrcId && l.Dst == evt.CurrentNodeId)
                     || (l.Src == evt.CurrentNodeId && l.Dst == evt.SrcId));

                    double linkDelay = link?.Delay ?? 1.0;
                    double linkBwMbps = link?.Bw ?? 100;

                    // 发送时延 (ms): packet bits / link bps * 1000
                    double packetBits = evt.PayloadSize * 8.0;
                    double bandwidthBps = linkBwMbps * 1_000_000.0;
                    double transmitDelay = packetBits / bandwidthBps * 1000.0;

                    // 排队时延: 基于 LinkBusyUntil 状态推演
                    if (!_linkBusyUntil.ContainsKey(linkKey))
                        _linkBusyUntil[linkKey] = 0.0;
                    double queueDelay = Math.Max(0.0, _linkBusyUntil[linkKey] - _currentTime);

                    // 统计发包
                    _flowSent[flowKey] = _flowSent.GetValueOrDefault(flowKey, 0) + 1;

                    // 三层丢包屏障
                    double dropRate = link?.DropRate ?? 0.0;
                    int maxQ = link?.MaxQueueDepth ?? 100;
                    double maxTime = link?.MaxTimeoutMs ?? 50.0;

                    bool isDropped = _rng.NextDouble() < dropRate;
                    if (!isDropped)
                    {
                        int qDepth = (int)(queueDelay * 10);
                        if (qDepth > maxQ)
                            isDropped = true;
                    }
                    if (!isDropped && queueDelay > maxTime)
                        isDropped = true;

                    if (isDropped)
                    {
                        _flowDropped[flowKey] = _flowDropped.GetValueOrDefault(flowKey, 0) + 1;

                        // 即使当前包被丢弃，也必须调度该流的下一个包，否则会导致永久断流
                        var t = _request.Traffic.First(x =>
                            x.Src == evt.SrcId && x.Dst == evt.DstId);
                        ScheduleNextPacket(t, routes);

                        continue;
                    }

                    // 推进链路忙碌状态
                    _linkBusyUntil[linkKey] = _currentTime + queueDelay + transmitDelay;

                    // 包总时延 = 传播延迟 + 排队时延 + 发送时延
                    double totalDelay = linkDelay + queueDelay + transmitDelay;
                    _allPacketDelays.Add(totalDelay);
                    _flowDelays[flowKey].Add(totalDelay);

                    // 峰值队列
                    int q = (int)(queueDelay * 10);
                    if (!_flowPeakQueues.ContainsKey(flowKey))
                        _flowPeakQueues[flowKey] = 0;
                    if (q > _flowPeakQueues[flowKey])
                        _flowPeakQueues[flowKey] = q;

                    var traffic = _request.Traffic.First(t =>
                        t.Src == evt.SrcId && t.Dst == evt.DstId);
                    ScheduleNextPacket(traffic, routes);
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
                    flows[kv.Key] = new
                    {
                        avg_delay_ms = Math.Round(avgDelay, 3),
                        loss_rate = Math.Round(actualLossRate, 4),
                        peak_queue = peakQueue,
                    };
                }
            }

            return new Dictionary<string, object>
            {
                ["status"] = "success",
                ["total_packets"] = _totalPackets,
                ["execution_time_sec"] = Math.Round(sw.Elapsed.TotalSeconds, 3),
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
            };

            double scheduleTime = _currentTime + interval;
            _eventQueue.Enqueue(evt, (scheduleTime, ++_eventIdCounter));
        }

        private Dictionary<string, Dictionary<string, string>> ComputeRoutes()
        {
            var routes = new Dictionary<string, Dictionary<string, string>>();
            var nodeIds = _request.Nodes.Select(n => n.Id).ToHashSet();

            var hostToRouter = new Dictionary<string, string>();
            foreach (var link in _request.Links)
            {
                bool srcIsHost = _request.Nodes.Any(n => n.Id == link.Src && n.Type == "Host");
                bool dstIsHost = _request.Nodes.Any(n => n.Id == link.Dst && n.Type == "Host");

                if (srcIsHost) hostToRouter[link.Src] = link.Dst;
                if (dstIsHost) hostToRouter[link.Dst] = link.Src;
            }

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
    }
}
