using System;
using System.Linq;
using Pytools.Models.Simulation;

namespace Pytools.Services
{
    public static class TopologyValidator
    {
        /// <returns>null if valid; otherwise a Chinese error message.</returns>
        public static string? Validate(SimulationRequest request)
        {
            if (request.Nodes.Count == 0)
                return "拓扑校验失败：节点列表为空。";

            var nodeIds = request.Nodes.Select(n => n.Id).ToHashSet();
            var hostIds = request.Nodes.Where(n => n.Type == "Host").Select(n => n.Id).ToHashSet();
            var routerIds = request.Nodes.Where(n => n.Type == "Router").Select(n => n.Id).ToHashSet();

            // Rule a: Host ↔ Host is forbidden
            foreach (var link in request.Links)
            {
                if (hostIds.Contains(link.Src) && hostIds.Contains(link.Dst))
                    return $"拓扑校验失败：主机 '{link.Src}' 与 '{link.Dst}' 之间不允许直连。";
            }

            // Gather adjacency count from Host → Router
            var hostLinkCount = new System.Collections.Generic.Dictionary<string, int>();
            foreach (var hostId in hostIds)
                hostLinkCount[hostId] = 0;

            foreach (var link in request.Links)
            {
                if (hostIds.Contains(link.Src) && routerIds.Contains(link.Dst))
                    hostLinkCount[link.Src]++;

                if (hostIds.Contains(link.Dst) && routerIds.Contains(link.Src))
                    hostLinkCount[link.Dst]++;
            }

            // Rule b: Each Host must connect to EXACTLY one Router
            foreach (var hostId in hostIds)
            {
                int count = hostLinkCount[hostId];
                if (count != 1)
                    return $"拓扑校验失败：主机 '{hostId}' 未连接或连接了多个路由器。它必须且只能连接一个默认网关。";
            }

            return null; // Passed
        }
    }
}
