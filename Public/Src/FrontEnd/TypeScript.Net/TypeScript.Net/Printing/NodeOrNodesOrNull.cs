// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using TypeScript.Net.Types;

namespace TypeScript.Net.Printing
{
    /// <summary>
    /// Discriminator for <see cref="NodeOrNodesOrNull"/> union type.
    /// </summary>
    public enum NodeOrNodesOrNullType
    {
        /// <nodoc />
        Node,

        /// <nodoc />
        Nodes,

        /// <nodoc />
        Null,
    }

    /// <nodoc/>
    public readonly struct NodeOrNodesOrNull
    {
        /// <nodoc/>
        public INode Node { get; }

        /// <nodoc/>
        public INodeArray<INode> Nodes { get; }

        /// <nodoc/>
        public NodeOrNodesOrNull([NotNull] INode node)
        {
            Contract.Requires(node != null);

            Node = node;
            Nodes = null;
        }

        /// <nodoc/>
        public NodeOrNodesOrNull([NotNull] INodeArray<INode> nodes)
        {
            Contract.Requires(nodes != null);
            Node = null;
            Nodes = nodes;
        }

        /// <nodoc/>
        [SuppressMessage("Microsoft.Naming", "CA1721:PropertyNamesShouldNotMatchGetMethods", Justification = "Type nomenclature is necessary within a compiler.")]
        public NodeOrNodesOrNullType Type
        {
            get
            {
                return Node != null
                    ? NodeOrNodesOrNullType.Node
                    : (Nodes != null ? NodeOrNodesOrNullType.Nodes : NodeOrNodesOrNullType.Null);
            }
        }

        /// <nodoc/>
        public object Value => (object)Node ?? Nodes;
    }
}
