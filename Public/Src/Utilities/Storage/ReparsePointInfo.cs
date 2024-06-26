// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Native.IO;
using BuildXL.Utilities;
using BuildXL.Utilities.Core;
using static BuildXL.Utilities.Core.FormattableStringEx;

namespace BuildXL.Storage
{
    /// <summary>
    /// Represents a reparse point
    /// </summary>
    public readonly struct ReparsePointInfo : IEquatable<ReparsePointInfo>
    {
        private readonly string m_targetString;

        /// <summary>
        /// The reparse point type of the file
        /// </summary>
        public readonly ReparsePointType ReparsePointType;

        /// <summary>
        /// Checks whether the reparse point is actionable
        /// </summary>
        public bool IsActionableReparsePoint => ReparsePointType.IsActionable();

        private ReparsePointInfo(ReparsePointType reparsePointType, string targetPath)
        {
            ReparsePointType = reparsePointType;
            m_targetString = targetPath;
        }

        /// <summary>
        /// Creates ReparsePointInfo from a string representation of target path.
        /// </summary>
        public static ReparsePointInfo Create(ReparsePointType reparsePointType, string targetPath)
        {
            Contract.Requires(!reparsePointType.IsActionable() || !string.IsNullOrEmpty(targetPath));

            if (!reparsePointType.IsActionable())
            {
                return CreateNoneReparsePoint();
            }

            return new ReparsePointInfo(reparsePointType, targetPath);
        }

        /// <summary>
        /// Returns a string representation of the reparse point target.
        /// </summary>
        public string GetReparsePointTarget()
        {
            if (!IsActionableReparsePoint)
            {
                return null;
            }

            return m_targetString;
        }

        /// <summary>
        /// Creates an empty (ReparsePointType.None) reparse point.
        /// </summary>
        public static ReparsePointInfo CreateNoneReparsePoint()
        {
            return new ReparsePointInfo(ReparsePointType.None, null);
        }

        /// <inheritdoc />
        public bool Equals(ReparsePointInfo other)
        {
            return ReparsePointType == other.ReparsePointType
                   && string.Equals(other.m_targetString, m_targetString, OperatingSystemHelper.PathComparison);
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return HashCodeHelper.Combine(ReparsePointType.GetHashCode(), m_targetString.GetHashCode());
        }

        /// <nodoc />
        public static bool operator ==(ReparsePointInfo left, ReparsePointInfo right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(ReparsePointInfo left, ReparsePointInfo right)
        {
            return !left.Equals(right);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return I($"IsActionableReparsePoint: '{IsActionableReparsePoint}', Type: '{ReparsePointType}', Target: '{GetReparsePointTarget() ?? ""}'");
        }
    }
}
