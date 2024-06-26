﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using BuildXL.Pips.Graph;
using BuildXL.Pips.Operations;
using BuildXL.Utilities.Core;
using static BuildXL.Utilities.Core.FormattableStringEx;

namespace BuildXL.Pips.DirectedGraph
{
    /// <summary>
    /// Debugger display class for NodeId which provides information about the node's
    /// dependencies and dependents along with the corresponding pip.)
    /// </summary>
    internal sealed class NodeIdDebugView
    {
        // Take weak references to data structures to avoid keeping them alive
        private static readonly WeakReference<IQueryablePipDependencyGraph> s_weakDebugPipGraph = new WeakReference<IQueryablePipDependencyGraph>(null);
        private static readonly WeakReference<IReadonlyDirectedGraph> s_weakAlternateGraph = new WeakReference<IReadonlyDirectedGraph>(null);
        private static readonly WeakReference<PipExecutionContext> s_weakPipExecutionContext = new WeakReference<PipExecutionContext>(null);
        private static readonly WeakReference<PipRuntimeInfo[]> s_runtimeInfos = new WeakReference<PipRuntimeInfo[]>(null);
        private readonly WeakReference<IReadonlyDirectedGraph> m_weakGraph = new WeakReference<IReadonlyDirectedGraph>(null);

        private readonly NodeId m_node;

        private IReadonlyDirectedGraph Graph
        {
            get
            {
                return GetTarget(m_weakGraph);
            }

            set
            {
                m_weakGraph.SetTarget(value);
            }
        }

        /// <summary>
        /// The pip graph to use when displaying the node information
        /// </summary>
        internal static IQueryablePipDependencyGraph DebugPipGraph
        {
            get
            {
                return GetTarget(s_weakDebugPipGraph);
            }

            set
            {
                s_weakDebugPipGraph.SetTarget(value);
            }
        }

        /// <summary>
        /// An alternative graph to use when creating alternate node views (namely a filtered graph)
        /// </summary>
        internal static IReadonlyDirectedGraph AlternateGraph
        {
            get
            {
                return GetTarget(s_weakAlternateGraph);
            }

            set
            {
                s_weakAlternateGraph.SetTarget(value);
            }
        }

        /// <summary>
        /// The pip execution context used for expanding paths and strings
        /// </summary>
        internal static PipExecutionContext DebugContext
        {
            get
            {
                return GetTarget(s_weakPipExecutionContext);
            }

            set
            {
                s_weakPipExecutionContext.SetTarget(value);
            }
        }

        /// <summary>
        /// The pip runtime infos if available
        /// </summary>
        internal static PipRuntimeInfo[] RuntimeInfos
        {
            get
            {
                return GetTarget(s_runtimeInfos);
            }

            set
            {
                s_runtimeInfos.SetTarget(value);
            }
        }

        public NodeIdDebugView(NodeId node)
        {
            m_node = node;
            Graph = DebugPipGraph.DirectedGraph;
        }

        public NodeIdDebugView AlternateNode => AlternateGraph == null ? this : new NodeIdDebugView(m_node) { Graph = AlternateGraph };

        public Pip Pip => DebugPipGraph?.HydratePip(m_node.ToPipId(), PipQueryContext.VisualStudioDebugView);

        public string ValueName => Pip?.Provenance?.OutputValueSymbol.ToString(DebugContext.SymbolTable);

        public List<NodeIdDebugView> Dependencies => Graph?.GetIncomingEdges(m_node).Select(e => new NodeIdDebugView(e.OtherNode) { Graph = Graph }).ToList();

        public List<NodeIdDebugView> Dependents => Graph?.GetOutgoingEdges(m_node).Select(e => new NodeIdDebugView(e.OtherNode) { Graph = Graph }).ToList();

        public PipRuntimeInfo RuntimeInfo => RuntimeInfos?[m_node.ToPipId().Value];

        public int RefCount => RuntimeInfo?.RefCount ?? -1;

        public override string ToString()
        {
            return I($"({m_node.Value}) {Pip?.PipType}: {ValueName}");
        }

        private static T GetTarget<T>(WeakReference<T> weak) where T : class
        {
            T target;
            weak.TryGetTarget(out target);
            return target;
        }
    }
}
