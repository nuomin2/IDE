from __future__ import annotations

import json
import math
import sys
from typing import Optional

import matplotlib.pyplot as plt
from matplotlib.ticker import PercentFormatter
import networkx as nx


class Node:
    """Base class for all network nodes."""

    def __init__(self, id: str, name: str = "") -> None:
        self.id = id
        self.name = name

    def __repr__(self) -> str:
        return f"{self.__class__.__name__}(id={self.id!r})"


class Host(Node):
    """Represents a host endpoint in the network topology."""
    pass


class Router(Node):
    """Represents a router/switch in the network topology."""
    pass


class Link:
    """A directed or undirected connection between two nodes."""

    def __init__(self, src: str, dst: str, bw: int, delay: float,
                 drop_rate: float = 0.0, max_queue_kb: float | None = None,
                 max_timeout_ms: float = 50.0) -> None:
        self.src = src
        self.dst = dst
        self.bw = bw          # Mbps
        self.delay = delay    # ms
        self.drop_rate = drop_rate
        self.max_timeout_ms = max_timeout_ms

        if max_queue_kb is None:
            rtt_ms = delay * 2.0
            bdp_bytes = (bw * 125.0) * rtt_ms
            calculated_kb = bdp_bytes / 1024.0
            self.max_queue_kb = max(15.0, calculated_kb)
        else:
            self.max_queue_kb = float(max_queue_kb)


class Traffic:
    """Traffic flow definition between a source and destination."""

    _ephemeral_port_pool = 49152

    def __init__(
        self,
        src: str,
        dst: str,
        type: str,
        interval_mean: float,
        payload_mean: int,
        payload_variance: float = 0.0,
        src_port: int | None = None,
        dst_port: int | None = None,
        qos_level: int = 0,
    ) -> None:
        if type.upper() == "TCP":
            raise ValueError("当前版本暂不支持 TCP 协议。请使用 UDP。")

        self.src = src
        self.dst = dst
        self.type = type
        self.interval_mean = interval_mean
        self.payload_mean = payload_mean
        self.payload_variance = payload_variance
        self.qos_level = qos_level

        if src_port is None:
            self.src_port = Traffic._ephemeral_port_pool
            Traffic._ephemeral_port_pool += 1
        else:
            self.src_port = src_port

        if dst_port is None:
            self.dst_port = Traffic._ephemeral_port_pool
            Traffic._ephemeral_port_pool += 1
        else:
            self.dst_port = dst_port


class Simulator:
    """Orchestrates the network topology, traffic, and IPC with the C# backend."""

    def __init__(self) -> None:
        self.nodes: list[Node] = []
        self.links: list[Link] = []
        self.traffics: list[Traffic] = []

    def add_node(self, node: Node) -> None:
        self.nodes.append(node)

    def TrunkLink(self, src: str, dst: str, bw: int, delay: float,
                  drop_rate: float = 0.0, max_queue_kb: float | None = None,
                  max_timeout_ms: float = 50.0) -> None:
        src_node = next((n for n in self.nodes if n.id == src), None)
        dst_node = next((n for n in self.nodes if n.id == dst), None)
        if isinstance(src_node, Host) or isinstance(dst_node, Host):
            raise ValueError(
                "TrunkLink 专用于路由器之间的骨干网连接，"
                "边缘接入请使用 AccessLink 方法！"
            )
        self.links.append(Link(src, dst, bw, delay,
                               drop_rate, max_queue_kb, max_timeout_ms))

    def AccessLink(self, host: str, router: str,
                   uplink_bw: int, uplink_delay: float,
                   downlink_bw: int, downlink_delay: float,
                   max_timeout_ms: float = 50.0) -> None:
        host_node = next((n for n in self.nodes if n.id == host), None)
        router_node = next((n for n in self.nodes if n.id == router), None)
        if not isinstance(host_node, Host) or not isinstance(router_node, Router):
            raise ValueError(
                "AccessLink 必须精确连接一个 Host 节点和一个 Router 节点！"
            )

        rtt_ms = uplink_delay + downlink_delay
        down_bdp_bytes = (downlink_bw * 125.0) * rtt_ms
        down_q_kb = max(15.0, down_bdp_bytes / 1024.0)
        up_bdp_bytes = (uplink_bw * 125.0) * rtt_ms
        up_q_kb = max(15.0, up_bdp_bytes / 1024.0)

        # 下行：Router → Host
        self.links.append(Link(router, host, downlink_bw, downlink_delay,
                               0.0, down_q_kb, max_timeout_ms))
        # 上行：Host → Router
        self.links.append(Link(host, router, uplink_bw, uplink_delay,
                               0.0, up_q_kb, max_timeout_ms))

    def add_traffic(
        self,
        src: str,
        dst: str,
        type: str,
        interval_mean: float,
        payload_mean: int,
        payload_variance: float = 0.0,
        src_port: int | None = None,
        dst_port: int | None = None,
        qos_level: int = 0,
    ) -> None:
        self.traffics.append(
            Traffic(src, dst, type, interval_mean, payload_mean,
                    payload_variance, src_port, dst_port, qos_level)
        )

    def draw_topology(self) -> None:
        """使用 networkx 力导向布局绘制拓扑结构。"""
        if not self.nodes:
            return

        G = nx.Graph()

        for node in self.nodes:
            node_type = "Host" if isinstance(node, Host) else "Router"
            G.add_node(node.id, type=node_type)

        for link in self.links:
            G.add_edge(link.src, link.dst)

        pos = nx.spring_layout(G, seed=42)

        hosts = [n.id for n in self.nodes if isinstance(n, Host)]
        routers = [n.id for n in self.nodes if isinstance(n, Router)]

        fig, ax = plt.subplots(figsize=(10, 10))

        nx.draw_networkx_edges(G, pos, ax=ax, edge_color="#78909C", width=1.5)

        nx.draw_networkx_nodes(G, pos, nodelist=hosts, ax=ax,
                               node_size=400, node_color="#2196F3",
                               edgecolors="#1565C0", linewidths=2, node_shape="s")
        nx.draw_networkx_nodes(G, pos, nodelist=routers, ax=ax,
                               node_size=600, node_color="#FF9800",
                               edgecolors="#E65100", linewidths=2, node_shape="o")

        nx.draw_networkx_labels(G, pos, ax=ax, font_size=10,
                                font_weight="bold", font_color="white")

        ax.axis("off")
        fig.tight_layout()
        plt.show()

    def run(
        self,
        simulation_time: int,
        seed: Optional[int] = None,
        summary_only: bool = False,
        draw_topo: bool = False,
        draw_cdf: bool = False,
    ) -> dict:
        if draw_topo:
            self.draw_topology()

        payload = {
            "simulation_time": simulation_time,
            "seed": seed,
            "summary_only": summary_only,
            "nodes": [
                {"id": n.id, "type": n.__class__.__name__} for n in self.nodes
            ],
            "links": [
                {"src": l.src, "dst": l.dst, "bw": l.bw, "delay": l.delay,
                 "drop_rate": l.drop_rate,
                 "max_queue_bytes": int(l.max_queue_kb * 1024),
                 "max_timeout_ms": l.max_timeout_ms}
                for l in self.links
            ],
            "traffic": [
                {
                    "src": t.src,
                    "dst": t.dst,
                    "src_port": t.src_port,
                    "dst_port": t.dst_port,
                    "qos_level": t.qos_level,
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
        try:
            result = json.loads(response_str)
        except (json.JSONDecodeError, Exception):
            result = {}

        _print_report(result, simulation_time, summary_only)

        if draw_cdf and "cdf_data" in result and result["cdf_data"]:
            cdf = result["cdf_data"]
            x_delays = cdf.get("x_delays", [])
            y_probs = cdf.get("y_probabilities", [])
            if x_delays and y_probs:
                plt.figure(figsize=(8, 5))
                plt.plot(x_delays, y_probs, marker=".", linestyle="-",
                         color="#1f77b4", linewidth=2)
                plt.title("全局时延累积分布函数 (CDF)", fontsize=12, fontweight="bold")
                plt.xlabel("时延 (ms)", fontsize=10)
                plt.ylabel("累积概率 (%)", fontsize=10)
                plt.gca().yaxis.set_major_formatter(PercentFormatter(1.0))
                plt.grid(True, linestyle="--", alpha=0.7)
                plt.tight_layout()
                plt.show()

        return _translate_keys(result)


def _translate_keys(result: dict) -> dict:
    """将 C# 内核返回的英文字段名映射为中文，供 Variable Explorer 显示。"""
    top_map = {
        "status": "状态",
        "total_packets": "总发包数",
        "execution_time_ms": "内核耗时 (ms)",
        "global_avg_delay_ms": "全局平均时延 (ms)",
        "global_std_dev_ms": "时延标准差 (ms)",
        "flows": "流量明细",
    }
    flow_map = {
        "avg_delay_ms": "平均时延 (ms)",
        "loss_rate": "丢包率",
        "peak_queue": "峰值队列(KB)",
        "throughput_kbps": "流吞吐量",
        "jitter_ms": "流平均抖动",
        "peak_capacity": "峰值产生时容量(Bytes)",
    }

    translated: dict = {}
    for k, v in result.items():
        new_key = top_map.get(k, k)
        if k == "flows" and isinstance(v, dict) and v:
            nested: dict = {}
            for flow_name, flow_data in v.items():
                if isinstance(flow_data, dict):
                    translated_flow: dict = {}
                    for fk, fv in flow_data.items():
                        cn_key = flow_map.get(fk, fk)
                        if fk == "peak_queue" and isinstance(fv, (int, float)):
                            translated_flow[cn_key] = round(fv / 1024.0, 3)
                        else:
                            translated_flow[cn_key] = fv
                    nested[flow_name] = translated_flow
            translated[new_key] = nested
        else:
            translated[new_key] = v

    return translated


def _print_report(result: dict, sim_time: int, summary_only: bool) -> None:
    """Print a formatted ASCII report to the console (Spyder / IDE)."""
    n = 50
    print("==================== 仿真完成 ====================")
    print(f"  仿真时长:        {sim_time} s")
    print(f"  总发包数量:      {result.get('total_packets', 0)}")
    print(f"  内核耗时:        {result.get('execution_time_ms', 0):.0f} ms")
    print(f"  全局平均时延:    {result.get('global_avg_delay_ms', 0):.3f} ms")
    print(f"  全局时延标准差:  {result.get('global_std_dev_ms', 0):.3f} ms")

    flows = result.get("flows", {})
    if not summary_only and flows:
        print("  " + "-" * (n - 4))
        import re
        # Pass 1: 统计每条 src->dst 的静默流（自动端口 >= 49152）数量
        silent_flow_count: dict = {}
        for fk in flows:
            m = re.match(r'^(.+):(\d+)->(.+):(\d+)$', fk)
            if m and int(m.group(2)) >= 49152 and int(m.group(4)) >= 49152:
                base = f"{m.group(1)}->{m.group(3)}"
                silent_flow_count[base] = silent_flow_count.get(base, 0) + 1
        # Pass 2: 渲染，静默多流加序号、单流隐藏端口
        silent_seq: dict = {}
        for flow_key, flow_data in flows.items():
            m = re.match(r'^(.+):(\d+)->(.+):(\d+)$', flow_key)
            if m and int(m.group(2)) >= 49152 and int(m.group(4)) >= 49152:
                base = f"{m.group(1)}->{m.group(3)}"
                cnt = silent_flow_count.get(base, 0)
                silent_seq[base] = silent_seq.get(base, 0) + 1
                if cnt == 1:
                    display_key = base
                else:
                    display_key = f"{base} (Flow {silent_seq[base]})"
            else:
                display_key = flow_key
            print(f"  [{display_key}]")

            peak_kb = flow_data.get('peak_queue', 0) / 1024.0
            cap_kb = flow_data.get('peak_capacity', 15360) / 1024.0
            ratio = (peak_kb / cap_kb * 100) if cap_kb > 0 else 0.0

            print(f"    流吞吐量:      {flow_data.get('throughput_kbps', 0):.2f} Kbps")
            print(f"    流丢包率:      {flow_data.get('loss_rate', 0):.4f}")
            print(f"    流平均延迟:    {flow_data.get('avg_delay_ms', 0):.3f} ms")
            print(f"    流平均抖动:    {flow_data.get('jitter_ms', 0):.3f} ms")
            print(f"    队列峰值/容量: {peak_kb:.2f} / {cap_kb:.2f} KB ({ratio:.1f}%)")
