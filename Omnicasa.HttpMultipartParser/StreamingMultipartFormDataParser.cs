﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HttpMultipartParser
{
    /// <summary>
    ///     Provides methods to parse a
    ///     <see href="http://www.ietf.org/rfc/rfc2388.txt">
    ///         <c>multipart/form-data</c>
    ///     </see>
    ///     stream into it's parameters and file data.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A parameter is defined as any non-file data passed in the multipart stream. For example
    ///         any form fields would be considered a parameter.
    ///     </para>
    ///     <para>
    ///         The parser determines if a section is a file or not based on the presence or absence
    ///         of the filename argument for the Content-Type header. If filename is set then the section
    ///         is assumed to be a file, otherwise it is assumed to be parameter data.
    ///     </para>
    /// </remarks>
    /// <example>
    ///     <code lang="C#">
    ///       Stream multipartStream = GetTheMultipartStream();
    ///       string boundary = GetTheBoundary();
    ///       var parser = new StreamingMultipartFormDataParser(multipartStream, boundary, Encoding.UTF8);
    ///
    ///       // Set up our delegates for how we want to handle recieved data.
    ///       // In our case parameters will be written to a dictionary and files
    ///       // will be written to a filestream
    ///       parser.DataHandler+= parameter => AddToDictionary(parameter);
    ///       parser.FileHandler += (name, fileName, type, disposition, buffer, bytes) => WriteDataToFile(fileName, buffer, bytes);
    ///       parser.Run();
    ///   </code>
    /// </example>
#pragma warning disable SA1404
    [SuppressMessage("ReSharper", "InconsistentNaming")]
#pragma warning restore SA1404
    public class StreamingMultipartFormDataParser
    {
        /// <summary>
        ///     The default buffer size.
        /// </summary>
        private const int DefaultBufferSize = 4096;

        /// <summary>
        ///     The stream we are parsing.
        /// </summary>
        private readonly Stream stream;

        /// <summary>
        ///     The boundary of the multipart message  as a string.
        /// </summary>
        private string boundary;

        /// <summary>
        ///     The boundary of the multipart message as a byte string
        ///     encoded with CurrentEncoding
        /// </summary>
        private byte[] boundaryBinary;

        /// <summary>
        ///     The end boundary of the multipart message as a string.
        /// </summary>
        private string endBoundary;

        /// <summary>
        ///     The end boundary of the multipart message as a byte string
        ///     encoded with CurrentEncoding
        /// </summary>
        private byte[] endBoundaryBinary;

        /// <summary>
        ///     Determines if we have consumed the end boundary binary and determines
        ///     if we are done parsing.
        /// </summary>
        private bool readEndBoundary;

        /// <summary>
        ///     Initializes a new instance of the <see cref="StreamingMultipartFormDataParser" /> class
        ///     with an input stream. Boundary will be automatically detected based on the
        ///     first line of input.
        /// </summary>
        /// <param name="stream">
        ///     The stream containing the multipart data
        /// </param>
        public StreamingMultipartFormDataParser(Stream stream)
            : this(stream, null, Encoding.UTF8, DefaultBufferSize)
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="StreamingMultipartFormDataParser" /> class
        ///     with the boundary and input stream.
        /// </summary>
        /// <param name="stream">
        ///     The stream containing the multipart data
        /// </param>
        /// <param name="boundary">
        ///     The multipart/form-data boundary. This should be the value
        ///     returned by the request header.
        /// </param>
        public StreamingMultipartFormDataParser(Stream stream, string boundary)
            : this(stream, boundary, Encoding.UTF8, DefaultBufferSize)
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="StreamingMultipartFormDataParser" /> class
        ///     with the input stream and stream encoding. Boundary is automatically
        ///     detected.
        /// </summary>
        /// <param name="stream">
        ///     The stream containing the multipart data
        /// </param>
        /// <param name="encoding">
        ///     The encoding of the multipart data
        /// </param>
        public StreamingMultipartFormDataParser(Stream stream, Encoding encoding)
            : this(stream, null, encoding, DefaultBufferSize)
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="StreamingMultipartFormDataParser" /> class
        ///     with the boundary, input stream and stream encoding.
        /// </summary>
        /// <param name="stream">
        ///     The stream containing the multipart data
        /// </param>
        /// <param name="boundary">
        ///     The multipart/form-data boundary. This should be the value
        ///     returned by the request header.
        /// </param>
        /// <param name="encoding">
        ///     The encoding of the multipart data
        /// </param>
        public StreamingMultipartFormDataParser(Stream stream, string boundary, Encoding encoding)
            : this(stream, boundary, encoding, DefaultBufferSize)
        {
            // 4096 is the optimal buffer size as it matches the internal buffer of a StreamReader
            // See: http://stackoverflow.com/a/129318/203133
            // See: http://msdn.microsoft.com/en-us/library/9kstw824.aspx (under remarks)
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="StreamingMultipartFormDataParser" /> class
        ///     with the stream, input encoding and buffer size. Boundary is automatically
        ///     detected.
        /// </summary>
        /// <param name="stream">
        ///     The stream containing the multipart data
        /// </param>
        /// <param name="encoding">
        ///     The encoding of the multipart data
        /// </param>
        /// <param name="binaryBufferSize">
        ///     The size of the buffer to use for parsing the multipart form data. This must be larger
        ///     then (size of boundary + 4 + # bytes in newline).
        /// </param>
        public StreamingMultipartFormDataParser(Stream stream, Encoding encoding, int binaryBufferSize)
            : this(stream, null, encoding, binaryBufferSize)
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="StreamingMultipartFormDataParser" /> class
        ///     with the boundary, stream, input encoding and buffer size.
        /// </summary>
        /// <param name="stream">
        ///     The stream containing the multipart data
        /// </param>
        /// <param name="boundary">
        ///     The multipart/form-data boundary. This should be the value
        ///     returned by the request header.
        /// </param>
        /// <param name="encoding">
        ///     The encoding of the multipart data
        /// </param>
        /// <param name="binaryBufferSize">
        ///     The size of the buffer to use for parsing the multipart form data. This must be larger
        ///     then (size of boundary + 4 + # bytes in newline).
        /// </param>
        public StreamingMultipartFormDataParser(Stream stream, string boundary, Encoding encoding, int binaryBufferSize)
        {
            if (stream == null || stream == Stream.Null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            if (encoding == null)
            {
                throw new ArgumentNullException(nameof(encoding));
            }

            this.stream = stream;
            this.boundary = boundary;
            Encoding = encoding;
            BinaryBufferSize = binaryBufferSize;
            readEndBoundary = false;
        }

        /// <summary>
        ///     Begins executing the parser. This should be called after all handlers have been set.
        /// </summary>
        public void Run()
        {
            var reader = new RebufferableBinaryReader(stream, Encoding, BinaryBufferSize);

            // If we don't know the boundary now is the time to calculate it.
            if (boundary == null)
            {
                boundary = DetectBoundary(reader);
            }

            // It's important to remember that the boundary given in the header has a -- appended to the start
            // and the last one has a -- appended to the end
            boundary = "--" + boundary;
            endBoundary = boundary + "--";

            // We add newline here because unlike reader.ReadLine() binary reading
            // does not automatically consume the newline, we want to add it to our signature
            // so we can automatically detect and consume newlines after the boundary
            boundaryBinary = Encoding.GetBytes(boundary);
            endBoundaryBinary = Encoding.GetBytes(endBoundary);

            Debug.Assert(BinaryBufferSize >= endBoundaryBinary.Length, "binaryBufferSize must be bigger then the boundary");
            Parse(reader);
        }

        /// <summary>
        /// RunAsync
        /// </summary>
        /// <param name="cancellation"></param>
        /// <returns></returns>
        public Task RunAsync(CancellationToken cancellation)
        {
            return Task.Run(
                () =>
            {
                try
                {
                    var reader = new RebufferableBinaryReader(stream, Encoding, BinaryBufferSize);
                    cancellation.ThrowIfCancellationRequested();

                    // If we don't know the boundary now is the time to calculate it.
                    if (boundary == null)
                    {
                        boundary = DetectBoundary(reader);
                    }

                    cancellation.ThrowIfCancellationRequested();

                    // It's important to remember that the boundary given in the header has a -- appended to the start
                    // and the last one has a -- appended to the end
                    boundary = "--" + boundary;
                    endBoundary = boundary + "--";

                    // We add newline here because unlike reader.ReadLine() binary reading
                    // does not automatically consume the newline, we want to add it to our signature
                    // so we can automatically detect and consume newlines after the boundary
                    cancellation.ThrowIfCancellationRequested();
                    boundaryBinary = Encoding.GetBytes(boundary);
                    endBoundaryBinary = Encoding.GetBytes(endBoundary);
                    cancellation.ThrowIfCancellationRequested();
                    Debug.Assert(BinaryBufferSize >= endBoundaryBinary.Length, "binaryBufferSize must be bigger then the boundary");

                    Parse(reader, cancellation);
                }
                catch (OperationCanceledException cex)
                {
                    Debug.WriteLine(cex);
                    CancelTokenStreamHandler?.Invoke();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
                    StreamClosedHandler?.Invoke();
                }
            }, cancellation);
        }

        /// <summary>
        /// The FileStreamDelegate defining functions that can handle file stream data from this parser.
        ///
        /// Delegates can assume that the data is sequential i.e. the data recieved by any delegates will be
        /// the data immediately following any previously recieved data.
        /// </summary>
        /// <param name="name">The name of the multipart data</param>
        /// <param name="fileName">The name of the file</param>
        /// <param name="contentType">The content type of the multipart data</param>
        /// <param name="contentDisposition">The content disposition of the multipart data</param>
        /// <param name="buffer">Some of the data from the file (not neccecarily all of the data)</param>
        /// <param name="bytes">The length of data in buffer</param>
        public delegate void FileStreamDelegate(string name, string fileName, string contentType, string contentDisposition, Dictionary<string, string> parameters, byte[] buffer, int bytes);

        /// <summary>
        /// Stream closed delegate
        /// </summary>
        public delegate void StreamClosedDelegate();

        /// <summary>
        /// Cancel token delegate
        /// </summary>
        public delegate void CancelTokenStreamDelegate();

        /// <summary>
        /// The DataStreamDelegate defining functions that can handle multipart parameter data
        /// </summary>
        public delegate void DataStreamDelegate(string contentType, Dictionary<string, string> parameters, byte[] buffer, int bytes);

        /// <summary>
        ///     Gets or sets the binary buffer size.
        /// </summary>
        public int BinaryBufferSize { get; set; }

        /// <summary>
        ///     Gets the encoding.
        /// </summary>
        public Encoding Encoding { get; private set; }

        /// <summary>
        /// The FileHandler. Delegates attached to this property will recieve sequential file stream data from this parser.
        /// </summary>
        public FileStreamDelegate FileHandler { get; set; }

        /// <summary>
        /// The ParameterHandler. Delegates attached to this property will recieve parameter data.
        /// </summary>
        public DataStreamDelegate DataHandler { get; set; }

        /// <summary>
        /// The StreamClosedHandler. Delegates attached to this property will be notified when the source stream is exhausted.
        /// </summary>
        public StreamClosedDelegate StreamClosedHandler { get; set; }

        /// <summary>
        /// The CancelTokenStreamHandler. Delegates attached to this property will be notified when the source cancel stream.
        /// </summary>
        public CancelTokenStreamDelegate CancelTokenStreamHandler { get; set; }

        /// <summary>
        /// Dispose this object
        /// </summary>
        public void Dispose()
        {
            stream?.Close();
            stream?.Dispose();
            boundary = null;
            endBoundaryBinary = null;
        }

        /// <summary>
        ///     Detects the boundary from the input stream. Assumes that the
        ///     current position of the reader is the start of the file and therefore
        ///     the beginning of the boundary.
        /// </summary>
        /// <param name="reader">
        ///     The binary reader to parse
        /// </param>
        /// <returns>
        ///     The boundary string
        /// </returns>
        private static string DetectBoundary(RebufferableBinaryReader reader)
        {
            // Presumably the boundary is --|||||||||||||| where -- is the stuff added on to
            // the front as per the protocol and ||||||||||||| is the part we care about.
            string boundary = reader.ReadLine().Substring(2);
            reader.Buffer("--" + boundary + "\n");
            return boundary;
        }

        /// <summary>
        ///     Finds the next sequence of newlines in the input stream.
        /// </summary>
        /// <param name="data">The data to search</param>
        /// <param name="offset">The offset to start searching at</param>
        /// <param name="maxBytes">The maximum number of bytes (starting from offset) to search.</param>
        /// <returns>The offset of the next newline</returns>
        private int FindNextNewline(ref byte[] data, int offset, int maxBytes)
        {
            byte[][] newlinePatterns = { Encoding.GetBytes("\r\n"), Encoding.GetBytes("\n") };
            Array.Sort(newlinePatterns, (first, second) => second.Length.CompareTo(first.Length));

            byte[] dataRef = data;
            if (offset != 0)
            {
                dataRef = data.Skip(offset).ToArray();
            }

            foreach (var pattern in newlinePatterns)
            {
                int position = SubsequenceFinder.Search(dataRef, pattern, maxBytes);
                if (position != -1)
                {
                    return position + offset;
                }
            }

            return -1;
        }

        /// <summary>
        ///     Calculates the length of the next found newline.
        ///     data[offset] is the start of the space to search.
        /// </summary>
        /// <param name="data">
        ///     The data containing the newline
        /// </param>
        /// <param name="offset">
        ///     The offset of the start of the newline
        /// </param>
        /// <returns>
        ///     The length in bytes of the newline sequence
        /// </returns>
        private int CalculateNewlineLength(ref byte[] data, int offset)
        {
            byte[][] newlinePatterns = { Encoding.GetBytes("\r\n"), Encoding.GetBytes("\n") };

            // Go through each pattern and find which one matches.
            foreach (var pattern in newlinePatterns)
            {
                bool found = false;
                for (int i = 0; i < pattern.Length; ++i)
                {
                    int readPos = offset + i;
                    if (data.Length == readPos || pattern[i] != data[readPos])
                    {
                        found = false;
                        break;
                    }

                    found = true;
                }

                if (found)
                {
                    return pattern.Length;
                }
            }

            return 0;
        }

        /// <summary>
        ///     Begins the parsing of the stream into objects.
        /// </summary>
        /// <param name="reader">
        ///     The multipart/form-data binary reader to parse from.
        /// </param>
        /// <param name="token"></param>
        /// <exception cref="MultipartParseException">
        ///     thrown on finding unexpected data such as a boundary before we are ready for one.
        /// </exception>
        private void Parse(RebufferableBinaryReader reader, CancellationToken token = default(CancellationToken))
        {
            try
            {
                // Parsing references include:
                // RFC1341 section 7: http://www.w3.org/Protocols/rfc1341/7_2_Multipart.html
                // RFC2388: http://www.ietf.org/rfc/rfc2388.txt

                // First we need to read untill we find a boundary
                while (true)
                {
                    token.ThrowIfCancellationRequested();

                    string line = reader.ReadLine();
                    if (line == boundary)
                    {
                        break;
                    }

                    if (line == null)
                    {
                        throw new MultipartParseException("Could not find expected boundary");
                    }
                }

                // Now that we've found the initial boundary we know where to start
                // We need parse each individual section
                while (!readEndBoundary)
                {
                    token.ThrowIfCancellationRequested();

                    // ParseSection will parse up to and including
                    // the next boundary.
                    ParseSection(reader, token);
                }

                StreamClosedHandler?.Invoke();
            }
            catch (OperationCanceledException cex)
            {
                Debug.WriteLine(cex);
                CancelTokenStreamHandler?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                StreamClosedHandler?.Invoke();
            }
        }

        /// <summary>
        ///     Parses a section of the stream that is known to be file data.
        /// </summary>
        /// <param name="parameters">
        ///     The header parameters of this file, expects "name" and "filename" to be valid keys
        /// </param>
        /// <param name="reader">
        ///     The StreamReader to read the data from
        /// </param>
        /// <param name="token">Cancellation token.</param>
        private void ParseFilePart(Dictionary<string, string> parameters, RebufferableBinaryReader reader, CancellationToken token = default(CancellationToken))
        {
            string name = parameters["name"];
            string filename = parameters["filename"];
            string contentType = parameters.ContainsKey("content-type") ? parameters["content-type"] : "text/plain";
            string contentDisposition = parameters.ContainsKey("content-disposition")
                                            ? parameters["content-disposition"]
                                            : "form-data";

            // We want to create a stream and fill it with the data from the
            // file.
            var curBuffer = new byte[BinaryBufferSize];
            var prevBuffer = new byte[BinaryBufferSize];
            var fullBuffer = new byte[BinaryBufferSize * 2];

            var prevLength = reader.Read(prevBuffer, 0, prevBuffer.Length);
            do
            {
                token.ThrowIfCancellationRequested();

                var curLength = reader.Read(curBuffer, 0, curBuffer.Length);

                token.ThrowIfCancellationRequested();

                // Combine both buffers into the fullBuffer
                // See: http://stackoverflow.com/questions/415291/best-way-to-combine-two-or-more-byte-arrays-in-c-sharp
                Buffer.BlockCopy(prevBuffer, 0, fullBuffer, 0, prevLength);

                token.ThrowIfCancellationRequested();

                Buffer.BlockCopy(curBuffer, 0, fullBuffer, prevLength, curLength);

                token.ThrowIfCancellationRequested();

                var fullLength = prevLength + curLength;

                // Now we want to check for a substring within the current buffer.
                // We need to find the closest substring greedily. That is find the
                // closest boundary and don't miss the end --'s if it's an end boundary.
                int endBoundaryPos = SubsequenceFinder.Search(fullBuffer, endBoundaryBinary, fullLength);
                int endBoundaryLength = endBoundaryBinary.Length;

                token.ThrowIfCancellationRequested();

                int boundaryPos = SubsequenceFinder.Search(fullBuffer, boundaryBinary, fullLength);
                int boundaryLength = boundaryBinary.Length;

                token.ThrowIfCancellationRequested();

                // If the boundaryPos is exactly at the end of our full buffer then ignore it as it could
                // actually be a endBoundary that's had one or both of the '--' chopped off by the buffer.
                if (boundaryPos + boundaryLength == fullLength ||
                   boundaryPos + boundaryLength + 1 == fullLength)
                {
                    boundaryPos = -1;
                }

                // We need to select the appropriate position and length
                // based on the smallest non-negative position.
                int endPos = -1;
                int endPosLength = 0;

                if (endBoundaryPos >= 0 && boundaryPos >= 0)
                {
                    if (boundaryPos < endBoundaryPos)
                    {
                        // Select boundary
                        endPos = boundaryPos;
                        endPosLength = boundaryLength;
                    }
                    else
                    {
                        // Select end boundary
                        endPos = endBoundaryPos;
                        endPosLength = endBoundaryLength;
                        readEndBoundary = true;
                    }
                }
                else if (boundaryPos >= 0 && endBoundaryPos < 0)
                {
                    // Select boundary
                    endPos = boundaryPos;
                    endPosLength = boundaryLength;
                }
                else if (boundaryPos < 0 && endBoundaryPos >= 0)
                {
                    // Select end boundary
                    endPos = endBoundaryPos;
                    endPosLength = endBoundaryLength;
                    readEndBoundary = true;
                }

                token.ThrowIfCancellationRequested();

                if (endPos != -1)
                {
                    // Now we need to check if the endPos is followed by \r\n or just \n. HTTP
                    // specifies \r\n but some clients might encode with \n. Or we might get 0 if
                    // we are at the end of the file.
                    int boundaryNewlineOffset = CalculateNewlineLength(ref fullBuffer, Math.Min(fullLength - 1, endPos + endPosLength));

                    token.ThrowIfCancellationRequested();

                    // We also need to check if the last n characters of the buffer to write
                    // are a newline and if they are ignore them.
                    int maxNewlineBytes = Encoding.GetMaxByteCount(2);
                    int bufferNewlineOffset = FindNextNewline(
                        ref fullBuffer, Math.Max(0, endPos - maxNewlineBytes), maxNewlineBytes);
                    int bufferNewlineLength = CalculateNewlineLength(ref fullBuffer, bufferNewlineOffset);

                    token.ThrowIfCancellationRequested();

                    // We've found an end. We need to consume all the binary up to it
                    // and then write the remainder back to the original stream. Then we
                    // need to modify the original streams position to take into account
                    // the new data.
                    // We also want to chop off the newline that is inserted by the protocl.
                    // We can do this by reducing endPos by the length of newline in this environment
                    // and encoding
                    FileHandler?.Invoke(name, filename, contentType, contentDisposition, parameters, fullBuffer, endPos - bufferNewlineLength);

                    token.ThrowIfCancellationRequested();

                    int writeBackOffset = endPos + endPosLength + boundaryNewlineOffset;
                    int writeBackAmount = (prevLength + curLength) - writeBackOffset;
                    var writeBackBuffer = new byte[writeBackAmount];
                    Buffer.BlockCopy(fullBuffer, writeBackOffset, writeBackBuffer, 0, writeBackAmount);
                    reader.Buffer(writeBackBuffer);

                    break;
                }

                // No end, consume the entire previous buffer
                FileHandler?.Invoke(name, filename, contentType, contentDisposition, parameters, prevBuffer, prevLength);

                // Now we want to swap the two buffers, we don't care
                // what happens to the data from prevBuffer so we set
                // curBuffer to it so it gets overwrited.
                byte[] tempBuffer = curBuffer;
                curBuffer = prevBuffer;
                prevBuffer = tempBuffer;

                // We don't need to swap the lengths because
                // curLength will be overwritten in the next
                // iteration of the loop.
                prevLength = curLength;
            }
            while (prevLength != 0);
        }

        /// <summary>
        ///     Parses a section of the stream that is known to be parameter data.
        /// </summary>
        /// <param name="parameters">
        ///     The header parameters of this section. "name" must be a valid key.
        /// </param>
        /// <param name="reader">
        ///     The StreamReader to read the data from
        /// </param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="MultipartParseException">
        ///     thrown if unexpected data is found such as running out of stream before hitting the boundary.
        /// </exception>
        private void ParseParameterPart(Dictionary<string, string> parameters, RebufferableBinaryReader reader, CancellationToken token = default(CancellationToken))
        {
            string contentType = parameters.ContainsKey("content-type") ? parameters["content-type"] : "text/plain";

            // We want to create a stream and fill it with the data from the
            // file.
            var curBuffer = new byte[BinaryBufferSize];
            var prevBuffer = new byte[BinaryBufferSize];
            var fullBuffer = new byte[BinaryBufferSize * 2];

            var prevLength = reader.Read(prevBuffer, 0, prevBuffer.Length);
            do
            {
                token.ThrowIfCancellationRequested();

                var curLength = reader.Read(curBuffer, 0, curBuffer.Length);

                token.ThrowIfCancellationRequested();

                // Combine both buffers into the fullBuffer
                // See: http://stackoverflow.com/questions/415291/best-way-to-combine-two-or-more-byte-arrays-in-c-sharp
                Buffer.BlockCopy(prevBuffer, 0, fullBuffer, 0, prevLength);

                token.ThrowIfCancellationRequested();

                Buffer.BlockCopy(curBuffer, 0, fullBuffer, prevLength, curLength);

                token.ThrowIfCancellationRequested();

                var fullLength = prevLength + curLength;

                // Now we want to check for a substring within the current buffer.
                // We need to find the closest substring greedily. That is find the
                // closest boundary and don't miss the end --'s if it's an end boundary.
                int endBoundaryPos = SubsequenceFinder.Search(fullBuffer, endBoundaryBinary, fullLength);
                int endBoundaryLength = endBoundaryBinary.Length;

                token.ThrowIfCancellationRequested();

                int boundaryPos = SubsequenceFinder.Search(fullBuffer, boundaryBinary, fullLength);
                int boundaryLength = boundaryBinary.Length;

                token.ThrowIfCancellationRequested();

                // If the boundaryPos is exactly at the end of our full buffer then ignore it as it could
                // actually be a endBoundary that's had one or both of the '--' chopped off by the buffer.
                if (boundaryPos + boundaryLength == fullLength ||
                   boundaryPos + boundaryLength + 1 == fullLength)
                {
                    boundaryPos = -1;
                }

                // We need to select the appropriate position and length
                // based on the smallest non-negative position.
                int endPos = -1;
                int endPosLength = 0;

                if (endBoundaryPos >= 0 && boundaryPos >= 0)
                {
                    if (boundaryPos < endBoundaryPos)
                    {
                        // Select boundary
                        endPos = boundaryPos;
                        endPosLength = boundaryLength;
                    }
                    else
                    {
                        // Select end boundary
                        endPos = endBoundaryPos;
                        endPosLength = endBoundaryLength;
                        readEndBoundary = true;
                    }
                }
                else if (boundaryPos >= 0 && endBoundaryPos < 0)
                {
                    // Select boundary
                    endPos = boundaryPos;
                    endPosLength = boundaryLength;
                }
                else if (boundaryPos < 0 && endBoundaryPos >= 0)
                {
                    // Select end boundary
                    endPos = endBoundaryPos;
                    endPosLength = endBoundaryLength;
                    readEndBoundary = true;
                }

                token.ThrowIfCancellationRequested();

                if (endPos != -1)
                {
                    // Now we need to check if the endPos is followed by \r\n or just \n. HTTP
                    // specifies \r\n but some clients might encode with \n. Or we might get 0 if
                    // we are at the end of the file.
                    int boundaryNewlineOffset = CalculateNewlineLength(ref fullBuffer, Math.Min(fullLength - 1, endPos + endPosLength));

                    token.ThrowIfCancellationRequested();

                    // We also need to check if the last n characters of the buffer to write
                    // are a newline and if they are ignore them.
                    int maxNewlineBytes = Encoding.GetMaxByteCount(2);
                    int bufferNewlineOffset = FindNextNewline(
                        ref fullBuffer, Math.Max(0, endPos - maxNewlineBytes), maxNewlineBytes);
                    int bufferNewlineLength = CalculateNewlineLength(ref fullBuffer, bufferNewlineOffset);

                    token.ThrowIfCancellationRequested();

                    // We've found an end. We need to consume all the binary up to it
                    // and then write the remainder back to the original stream. Then we
                    // need to modify the original streams position to take into account
                    // the new data.
                    // We also want to chop off the newline that is inserted by the protocl.
                    // We can do this by reducing endPos by the length of newline in this environment
                    // and encoding
                    DataHandler?.Invoke(contentType, parameters, fullBuffer, endPos - bufferNewlineLength);

                    token.ThrowIfCancellationRequested();

                    int writeBackOffset = endPos + endPosLength + boundaryNewlineOffset;
                    int writeBackAmount = (prevLength + curLength) - writeBackOffset;
                    var writeBackBuffer = new byte[writeBackAmount];
                    Buffer.BlockCopy(fullBuffer, writeBackOffset, writeBackBuffer, 0, writeBackAmount);
                    reader.Buffer(writeBackBuffer);

                    break;
                }

                // No end, consume the entire previous buffer
                DataHandler?.Invoke(contentType, parameters, prevBuffer, prevLength);

                // Now we want to swap the two buffers, we don't care
                // what happens to the data from prevBuffer so we set
                // curBuffer to it so it gets overwrited.
                byte[] tempBuffer = curBuffer;
                curBuffer = prevBuffer;
                prevBuffer = tempBuffer;

                // We don't need to swap the lengths because
                // curLength will be overwritten in the next
                // iteration of the loop.
                prevLength = curLength;
            }
            while (prevLength != 0);
        }

        /// <summary>
        ///     Parses the header of the next section of the multipart stream and
        ///     determines if it contains file data or parameter data.
        /// </summary>
        /// <param name="reader">
        ///     The StreamReader to read data from.
        /// </param>
        /// <param name="token"></param>
        /// <exception cref="MultipartParseException">
        ///     thrown if unexpected data is hit such as end of stream.
        /// </exception>
        private void ParseSection(RebufferableBinaryReader reader, CancellationToken token = default(CancellationToken))
        {
            // Our first job is to determine what type of section this is: form data or file.
            // This is a bit tricky because files can still be encoded with Content-Disposition: form-data
            // in the case of single file uploads. Multi-file uploads have Content-Disposition: file according
            // to the spec however in practise it seems that multiple files will be represented by
            // multiple Content-Disposition: form-data files.
            var parameters = new Dictionary<string, string>();

            string line = reader.ReadLine();
            while (line != string.Empty)
            {
                token.ThrowIfCancellationRequested();

                if (line == null)
                {
                    throw new MultipartParseException("Unexpected end of stream");
                }

                if (line == boundary || line == endBoundary)
                {
                    throw new MultipartParseException("Unexpected end of section");
                }

                // This line parses the header values into a set of key/value pairs. For example:
                // Content-Disposition: form-data; name="textdata"
                // ["content-disposition"] = "form-data"
                // ["name"] = "textdata"
                // Content-Disposition: form-data; name="file"; filename="data.txt"
                // ["content-disposition"] = "form-data"
                // ["name"] = "file"
                // ["filename"] = "data.txt"
                // Content-Type: text/plain
                // ["content-type"] = "text/plain"
                Dictionary<string, string> values = SplitBySemicolonIgnoringSemicolonsInQuotes(line)
                    .Select(x => x.Split(new[] { ':', '=' }, 2))

                    // select where the length of the array is equal to two, that way if it is only one it will
                    // be ignored as it is invalid key-pair
                    .Where(x => x.Length == 2)

                    // Limit split to 2 splits so we don't accidently split characters in file paths.
                    .ToDictionary(
                        x => x[0].Trim().Replace("\"", string.Empty).ToLower(),
                        x => x[1].Trim().Replace("\"", string.Empty));

                token.ThrowIfCancellationRequested();

                // Here we just want to push all the values that we just retrieved into the
                // parameters dictionary.
                try
                {
                    foreach (var pair in values)
                    {
                        token.ThrowIfCancellationRequested();

                        parameters.Add(pair.Key, pair.Value);
                    }
                }
                catch (ArgumentException)
                {
                    throw new MultipartParseException("Duplicate field in section");
                }

                line = reader.ReadLine();
            }

            token.ThrowIfCancellationRequested();

            try
            {
                // Now that we've consumed all the parameters we're up to the body. We're going to do
                // different things depending on if we're parsing a, relatively small, form value or a
                // potentially large file.
                if (parameters.ContainsKey("filename"))
                {
                    // Right now we assume that if a section contains filename then it is a file.
                    // This assumption needs to be checked, it holds true in firefox but is untested for other
                    // browsers.
                    ParseFilePart(parameters, reader, token);
                }
                else
                {
                    ParseParameterPart(parameters, reader, token);
                }
            }
            catch (OperationCanceledException cex)
            {
                Debug.WriteLine(cex);
                CancelTokenStreamHandler?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                StreamClosedHandler?.Invoke();
            }
        }

        /// <summary>
        ///     Splits a line by semicolons but ignores semicolons in quotes.
        /// </summary>
        /// <param name="line">The line to split</param>
        /// <returns>The split strings</returns>
        private IEnumerable<string> SplitBySemicolonIgnoringSemicolonsInQuotes(string line)
        {
            // Loop over the line looking for a semicolon. Keep track of if we're currently inside quotes
            // and if we are don't treat a semicolon as a splitting character.
            bool inQuotes = false;
            string workingString = string.Empty;

            foreach (char c in line)
            {
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                }

                if (c == ';' && !inQuotes)
                {
                    yield return workingString;
                    workingString = string.Empty;
                }
                else
                {
                    workingString += c;
                }
            }

            yield return workingString;
        }
    }
}