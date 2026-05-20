from __future__ import annotations

import json
import math
import sys
from typing import Optional

import matplotlib.pyplot as plt


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

    def __init__(
        self,
        src: str,
        dst: str,
        type: str,
        interval_mean: float,
        payload_mean: int,
        payload_variance: float = 0.0,
    ) -> None:
        if type.upper() == "TCP":
            raise ValueError("当前版本暂不支持 TCP 协议。请使用 UDP。")

        self.src = src
        self.dst = dst
        self.type = type
        self.interval_mean = interval_mean
        self.payload_mean = payload_mean
        self.payload_variance = payload_variance


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
    ) -> None:
        self.traffics.append(
            Traffic(src, dst, type, interval_mean, payload_mean, payload_variance)
        )

    def draw_topology(self) -> None:
        """绘制当前拓扑结构的无向图（圆形布局）。"""
        if not self.nodes:
            return

        hosts = [n for n in self.nodes if isinstance(n, Host)]
        routers = [n for n in self.nodes if isinstance(n, Router)]
        pos: dict[str, tuple[float, float]] = {}

        # 内环 Router: 单节点原点，多节点均匀分布在半径 0.4
        nr = len(routers)
        if nr == 1:
            pos[routers[0].id] = (0.0, 0.0)
        else:
            for i, router in enumerate(routers):
                theta = 2 * math.pi * i / nr
                pos[router.id] = (0.4 * math.cos(theta), 0.4 * math.sin(theta))

        # 外环 Host: 均匀分布在半径 1.0，90° 相位偏移
        nh = len(hosts)
        if nh > 0:
            for i, host in enumerate(hosts):
                theta = 2 * math.pi * i / nh + math.pi / 2
                pos[host.id] = (1.0 * math.cos(theta), 1.0 * math.sin(theta))

        fig, ax = plt.subplots(figsize=(10, 10))

        # 标准直线 + 时延文本（仅 TrunkLink 绘制）
        drawn_labels: set[tuple[str, str]] = set()
        for link in self.links:
            if link.src not in pos or link.dst not in pos:
                continue
            x1, y1 = pos[link.src]
            x2, y2 = pos[link.dst]

            plt.plot([x1, x2], [y1, y2], color="#78909C", zorder=1)

            src_node = next((n for n in self.nodes if n.id == link.src), None)
            dst_node = next((n for n in self.nodes if n.id == link.dst), None)
            is_trunk = (isinstance(src_node, Router) and isinstance(dst_node, Router))
            if is_trunk:
                pair = tuple(sorted((link.src, link.dst)))
                if pair not in drawn_labels:
                    drawn_labels.add(pair)
                    mx, my = (x1 + x2) / 2, (y1 + y2) / 2
                    ax.text(mx, my, f"{link.delay}ms", fontsize=8,
                            color="#37474F", ha="center", va="bottom",
                            bbox=dict(boxstyle="round,pad=0.2", facecolor="white",
                                      edgecolor="none", alpha=0.85),
                            zorder=4)

        # 节点 + 标签
        for node in self.nodes:
            x, y = pos[node.id]
            if isinstance(node, Host):
                ax.scatter(x, y, s=400, c="#2196F3", edgecolors="#1565C0",
                           linewidths=2, marker="s", zorder=3)
            else:
                ax.scatter(x, y, s=600, c="#FF9800", edgecolors="#E65100",
                           linewidths=2, marker="o", zorder=3)
            ax.text(x, y, node.id, fontsize=10, fontweight="bold",
                    color="white", ha="center", va="center", zorder=5)

        ax.set_xlim(-1.5, 1.5)
        ax.set_ylim(-1.5, 1.5)
        ax.set_aspect("equal")
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
                plt.ylabel("累积概率 (Probability)", fontsize=10)
                plt.grid(True, linestyle="--", alpha=0.7)
                plt.tight_layout()
                plt.show()

        return _translate_keys(result)


def _translate_keys(result: dict) -> dict:
    """将 C# 内核返回的英文字段名映射为中文，供 Variable Explorer 显示。"""
    top_map = {
        "status": "状态",
        "total_packets": "总发包数",
        "execution_time_sec": "内核真实耗时（s）",
        "global_avg_delay_ms": "全局平均时延（ms）",
        "global_std_dev_ms": "时延标准差（ms）",
        "flows": "流量明细",
    }
    flow_map = {
        "avg_delay_ms": "平均时延（ms）",
        "loss_rate": "丢包率",
        "peak_queue": "峰值队列(KB)",
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
    print("=" * n)
    print("  仿真完成")
    print("=" * n)
    print(f"  状态:              {result.get('status', 'unknown')}")
    print(f"  仿真时长 (s):      {sim_time}")
    print(f"  总发包数:          {result.get('total_packets', 0)}")
    print(f"  内核耗时 (s):      {result.get('execution_time_sec', 0):.3f}")
    print(f"  平均时延 (ms):     {result.get('global_avg_delay_ms', 0):.3f}")
    print(f"  时延标准差 (ms):   {result.get('global_std_dev_ms', 0):.3f}")

    flows = result.get("flows", {})
    if not summary_only and flows:
        print("  " + "-" * (n - 4))
        for flow_key, flow_data in flows.items():
            print(f"  [{flow_key}]")
            print(f"    平均时延:  {flow_data['avg_delay_ms']:.3f} ms")
            print(f"    丢包率:    {flow_data['loss_rate']:.4f}")
            print(f"    队列峰值 (KB):  {flow_data['peak_queue'] / 1024:.3f}")
    print("=" * n)
