// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Native.IO;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Core.Tasks;

namespace BuildXL.Processes
{
    /// <summary>
    /// The output of a sandboxed process, stored either in memory or on disk.
    /// </summary>
    public sealed class SandboxedProcessOutput
    {
        /// <summary>
        /// An undefined file length
        /// </summary>
        public const long NoLength = -1;

        private readonly Encoding m_encoding;
        private readonly SandboxedProcessFile m_file;
        private readonly ISandboxedProcessFileStorage m_fileStorage;
        private readonly long m_length;
        private readonly string m_value;
        private string m_fileName;
        private Task m_saveTask;
        private readonly BuildXLException m_exception;

        /// <summary>
        /// Creates an instance of this class.
        /// </summary>
        public SandboxedProcessOutput(
            long length,
            string value,
            string fileName,
            Encoding encoding,
            ISandboxedProcessFileStorage fileStorage,
            SandboxedProcessFile file,
            BuildXLException exception)
        {
            requires(length >= NoLength || exception != null);
            requires(exception != null ^ (value != null ^ fileName != null));
            requires(exception != null || encoding != null);
            requires(encoding != null);

            m_length = length;
            m_value = value;
            m_fileName = fileName;
            m_encoding = encoding;
            m_fileStorage = fileStorage;
            m_file = file;
            m_saveTask = m_fileName != null ? Unit.VoidTask : null;
            m_exception = exception;

            void requires(bool condition)
            {
                if (!condition)
                {
                    throw Contract.AssertFailure($"length: {length}; value: '{value}'; filename: {fileName}; encoding: {encoding}; exception: {exception?.ToString()}");
                }
            }
        }

        /// <summary>
        /// Serializes this instance to a given <paramref name="writer"/>.
        /// </summary>
        public void Serialize(BuildXLWriter writer)
        {
            writer.Write(m_length);
            writer.WriteNullableString(m_value);
            writer.WriteNullableString(m_fileName);
            writer.Write(m_encoding);
            writer.Write(m_fileStorage, (w, v) => SandboxedProcessStandardFiles.From(v).Serialize(w));
            writer.WriteCompact((uint)m_file);
            writer.Write(m_exception, (w, v) =>
            {
                // There is no easy way to serialize an exception. So, in addition to serializing the message of the exception,
                // we also serialize the messages of its inner exceptions.
                // TODO: Consider serializing the exception as a JSON object; currently it only works with Newtonsoft.Json.
                w.WriteNullableString(v.LogEventMessage);
                w.WriteCompact((uint)v.RootCause);
            });
        }

        /// <summary>
        /// Deserializes an instance of <see cref="SandboxedProcessOutput"/>.
        /// </summary>
        public static SandboxedProcessOutput Deserialize(BuildXLReader reader)
        {
            long length = reader.ReadInt64();
            string value = reader.ReadNullableString();
            string fileName = reader.ReadNullableString();
            Encoding encoding = reader.ReadEncoding();
            SandboxedProcessStandardFiles standardFiles = reader.ReadNullable(SandboxedProcessStandardFiles.Deserialize);
            ISandboxedProcessFileStorage fileStorage = null;
            if (standardFiles != null)
            {
                fileStorage = new StandardFileStorage(standardFiles);
            }
            SandboxedProcessFile file = (SandboxedProcessFile)reader.ReadUInt32Compact();
            BuildXLException exception = reader.ReadNullable(r => new BuildXLException(r.ReadNullableString(), (ExceptionRootCause)r.ReadUInt32Compact()));

            return new SandboxedProcessOutput(
                length,
                value,
                fileName,
                encoding,
                fileStorage,
                file,
                exception);
        }

        /// <summary>
        /// The encoding used when saving the file
        /// </summary>
        public Encoding Encoding
        {
            get
            {
                Contract.Requires(!HasException);
                return m_encoding;
            }
        }

        /// <summary>
        /// Re-creates an instance from a saved file.
        /// </summary>
        public static SandboxedProcessOutput FromFile(string fileName, string encodingName, SandboxedProcessFile file)
        {
            Contract.Requires(fileName != null);
            Contract.Requires(encodingName != null);

            BuildXLException exception = null;
            Encoding encoding;

#if DISABLE_FEATURE_EXTENDED_ENCODING
            // Console encoding is forced to UTF-8 in CoreFx see: https://github.com/dotnet/corefx/issues/10054
            // Trying to parse anything else or in the Windows case 'Codepage - 437', fails. We default to UTF-8 which
            // is the standard console encoding on any Unix based system anyway.
            encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
#else
            try
            {
                try
                {
                    encoding = Encoding.GetEncoding(encodingName);
                }
                catch (ArgumentException ex)
                {
                    throw new BuildXLException("Unsupported encoding name", ex);
                }
            }
            catch (BuildXLException ex)
            {
                fileName = null;
                encoding = null;
                exception = ex;
            }
#endif

            return new SandboxedProcessOutput(NoLength, null, fileName, encoding, null, file, exception);
        }

        /// <summary>
        /// The kind of output file
        /// </summary>
        public SandboxedProcessFile File => m_file;

        /// <summary>
        /// The file storage
        /// </summary>
        public ISandboxedProcessFileStorage FileStorage => m_fileStorage;

        /// <summary>
        /// The length of the output in characters
        /// </summary>
        public long Length
        {
            get
            {
                Contract.Requires(HasLength);
                return m_length;
            }
        }

        /// <summary>
        /// Reads the entire value; String will be truncated around 100k characters.
        /// </summary>
        /// <exception cref="BuildXLException">Thrown if a recoverable error occurs while opening the stream.</exception>
        public Task<string> ReadValueAsync() => ReadValueAsync(100_000_000);

        /// <summary>
        /// For testing
        /// </summary>
        internal async Task<string> ReadValueAsync(int maxLength)
        {
            if (m_exception != null)
            {
                ExceptionDispatchInfo.Capture(m_exception).Throw();
            }

            if (m_value != null)
            {
                return m_value;
            }

            return await ExceptionUtilities.HandleRecoverableIOException(
                async () =>
                {
                    using (TextReader reader = CreateFileReader())
                    {
                        if (m_length < maxLength)
                        {
                            return await reader.ReadToEndAsync();
                        }
                        else
                        {
                            char[] buffer = new char[maxLength];
                            await reader.ReadBlockAsync(buffer, 0, maxLength);
                            return new string(buffer);
                        }
                    }
                },
                e => throw new BuildXLException("Failed to read a value from a stream", e));
        }

        /// <summary>
        /// Checks whether the file has been saved to disk
        /// </summary>
        public bool IsSaved => Volatile.Read(ref m_fileName) != null;

        /// <summary>
        /// Checks whether this instance is in an exceptional state.
        /// </summary>
        public bool HasException => m_exception != null;

        /// <summary>
        /// Checks whether the length of this instance is known.
        /// </summary>
        public bool HasLength => m_length > NoLength;

        /// <summary>
        /// Saves the file to disk
        /// </summary>
        /// <exception cref="BuildXLException">Thrown if a recoverable error occurs while opening the stream.</exception>
        public Task SaveAsync()
        {
            if (m_exception != null)
            {
                ExceptionDispatchInfo.Capture(m_exception).Throw();
            }

            if (m_saveTask == null)
            {
                lock (this)
                {
                    if (m_saveTask == null)
                    {
                        m_saveTask = InternalSaveAsync();
                    }
                }
            }

            return m_saveTask;
        }

        private async Task InternalSaveAsync()
        {
            string fileName = m_fileStorage.GetFileName(m_file);
            FileUtilities.CreateDirectory(Path.GetDirectoryName(fileName));
            await FileUtilities.WriteAllTextAsync(fileName, m_value, m_encoding);

            string existingFileName = Interlocked.CompareExchange(ref m_fileName, fileName, comparand: null);
            Contract.Assume(existingFileName == null, "Sandboxed process output should only be saved once (via InternalSaveAsync)");

            m_saveTask = Unit.VoidTask;
        }

        /// <summary>
        /// Gets the name of a file that stores the output
        /// </summary>
        public string FileName
        {
            get
            {
                Contract.Requires(IsSaved);
                return m_fileName;
            }
        }

        /// <summary>
        /// Creates a reader for the output.
        /// </summary>
        /// <exception cref="BuildXLException">Thrown if a recoverable error occurs while opening the stream.</exception>
        public TextReader CreateReader()
        {
            if (m_exception != null)
            {
                throw m_exception;
            }

            return m_value != null ? new StringReader(m_value) : CreateFileReader();
        }

        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope",
            Justification = "The StreamReader will dispose the stream.")]
        private TextReader CreateFileReader()
        {
            // This FileStream is not asynchronous due to an intermittant crash we see on some machines when using
            // an asynchronous stream here
            FileStream stream = FileUtilities.CreateFileStream(
                m_fileName,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read | FileShare.Delete);
            return new StreamReader(stream);
        }
    }
}
