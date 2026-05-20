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

            // Rule b: Each Host must connect to at least one Router (multi-homing allowed)
            var hostHasLink = new System.Collections.Generic.Dictionary<string, bool>();
            foreach (var hostId in hostIds)
                hostHasLink[hostId] = false;

            foreach (var link in request.Links)
            {
                if (hostIds.Contains(link.Src)) hostHasLink[link.Src] = true;
                if (hostIds.Contains(link.Dst)) hostHasLink[link.Dst] = true;
            }

            foreach (var kv in hostHasLink)
            {
                if (!kv.Value)
                    return $"拓扑校验失败：主机 '{kv.Key}' 未连接任何节点。主机必须至少连接一个路由器。";
            }

            return null; // Passed
        }
    }
}
