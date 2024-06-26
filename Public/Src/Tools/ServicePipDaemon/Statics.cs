// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Native.IO;

namespace Tool.ServicePipDaemon
{
    /// <summary>
    ///     Various helper method, typically to be imported with "using static".
    /// </summary>
    public static class Statics
    {
        /// <nodoc/>
        public const string MaterializationResultIsSymlinkErrorPrefix = "File materialization succeeded, but file found on disk is a symlink: ";
        
        /// <nodoc/>
        public const string MaterializationResultFileNotFoundErrorPrefix = "File materialization succeeded, but file is not found on disk: ";
        
        /// <nodoc/>
        public const string MaterializationResultMaterializationFailedErrorPrefix = "File materialization failed: ";

        /// <summary>
        ///     Logs an error as a line of text.  Currently prints out to <code>Console.Error</code>.
        ///     to use whatever other
        /// </summary>
        public static void Error(string error)
        {
            if (error != null)
            {
                Console.Error.WriteLine(error); 
            }
        }

        /// <summary>
        ///     Returns whether the file at a given location is a symlink.
        /// </summary>
        public static bool IsSymLinkOrMountPoint(string absoluteFilePath)
        {
            var reparsePointType = FileUtilities.TryGetReparsePointType(absoluteFilePath);
            if (!reparsePointType.Succeeded)
            {
                return false;
            }

            return FileUtilities.IsReparsePointActionable(reparsePointType.Result);
        }
    }
}
