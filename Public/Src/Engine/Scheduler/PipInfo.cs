// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Utilities.Core;

namespace BuildXL.Scheduler
{
    /// <summary>
    /// Defines the information needed for caching of a pip.
    /// NOTE: No behavior should be defined in this class
    /// </summary>
    public class PipInfo
    {
        private string m_description;
        private readonly PipExecutionContext m_context;

        /// <nodoc />
        public PipInfo(
            Pip pip,
            PipExecutionContext context)
        {
            Contract.Requires(pip != null);
            m_context = context;
            UnderlyingPip = pip;
        }

        /// <summary>
        /// The underlying pip
        /// </summary>
        public Pip UnderlyingPip { get; }

        /// <summary>
        /// The semistable hash of the underlying pip
        /// </summary>
        public long SemiStableHash => UnderlyingPip.SemiStableHash;

        /// <summary>
        /// The pip id
        /// </summary>
        public PipId PipId => UnderlyingPip.PipId;

        /// <summary>
        /// Gets the description of the pip
        /// </summary>
        public string Description => m_description ?? (m_description = UnderlyingPip.GetDescription(m_context));
    }
}
