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
                 drop_rate: float = 0.0, max_queue_depth: int = 100,
                 max_timeout_ms: float = 50.0) -> None:
        self.src = src
        self.dst = dst
        self.bw = bw          # Mbps
        self.delay = delay    # ms
        self.drop_rate = drop_rate
        self.max_queue_depth = max_queue_depth
        self.max_timeout_ms = max_timeout_ms


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

    def add_link(self, src: str, dst: str, bw: int, delay: float,
                 drop_rate: float = 0.0, max_queue_depth: int = 100,
                 max_timeout_ms: float = 50.0) -> None:
        self.links.append(Link(src, dst, bw, delay,
                               drop_rate, max_queue_depth, max_timeout_ms))

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

        n = len(self.nodes)
        pos: dict[str, tuple[float, float]] = {}

        # 圆形布局: 第 i 个节点角度 = 2π·i / N
        for i, node in enumerate(self.nodes):
            theta = 2 * math.pi * i / n
            pos[node.id] = (math.cos(theta), math.sin(theta))

        plt.figure(figsize=(8, 8))

        # 绘制链路（连线 + 中点标注时延）
        for link in self.links:
            if link.src not in pos or link.dst not in pos:
                continue
            x1, y1 = pos[link.src]
            x2, y2 = pos[link.dst]
            plt.plot([x1, x2], [y1, y2], color="#90A4AE", linewidth=2, zorder=1)
            mx, my = (x1 + x2) / 2, (y1 + y2) / 2
            plt.text(mx, my, f"{link.delay}ms", fontsize=8,
                     color="#546E7A", ha="center", va="bottom",
                     bbox=dict(boxstyle="round,pad=0.2", facecolor="white",
                               edgecolor="none", alpha=0.8))

        # 绘制节点（Host=蓝色圆形, Router=橙色方形）
        for node in self.nodes:
            x, y = pos[node.id]
            if node.__class__.__name__ == "Host":
                plt.scatter(x, y, s=300, c="#42A5F5", edgecolors="#1E88E5",
                           linewidths=2, marker="s", zorder=2)
            else:
                plt.scatter(x, y, s=500, c="#FFA726", edgecolors="#EF6C00",
                           linewidths=2, marker="o", zorder=2)
            plt.text(x, y, node.id, fontsize=10, fontweight="bold",
                     color="white", ha="center", va="center", zorder=3)

        plt.axis("off")
        plt.tight_layout()
        plt.show()

    def run(
        self,
        simulation_time: int,
        seed: Optional[int] = None,
        summary_only: bool = False,
    ) -> dict:
        payload = {
            "simulation_time": simulation_time,
            "seed": seed,
            "summary_only": summary_only,
            "nodes": [
                {"id": n.id, "type": n.__class__.__name__} for n in self.nodes
            ],
            "links": [
                {"src": l.src, "dst": l.dst, "bw": l.bw, "delay": l.delay,
                 "drop_rate": l.drop_rate, "max_queue_depth": l.max_queue_depth,
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
        "peak_queue": "队列峰值",
    }

    translated: dict = {}
    for k, v in result.items():
        new_key = top_map.get(k, k)
        if k == "flows" and isinstance(v, dict) and v:
            nested: dict = {}
            for flow_name, flow_data in v.items():
                if isinstance(flow_data, dict):
                    nested[flow_name] = {
                        flow_map.get(fk, fk): fv for fk, fv in flow_data.items()
                    }
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
            print(f"    队列峰值:  {flow_data['peak_queue']}")
    print("=" * n)
