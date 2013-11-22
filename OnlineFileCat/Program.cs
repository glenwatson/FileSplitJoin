using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Security.Policy;
using System.Security.Cryptography;

namespace OnlineFileCat
{
    class Program
    {
        static void Main(string[] args)
        {
            string directory = "dir";
            string file1 = "f1.txt";
            string file2 = "f2.txt";
            string catFile = "f3.txt";

            FileStream fs1 = File.Open(directory+"\\"+file1, FileMode.Open, FileAccess.ReadWrite, FileShare.None); //need to lock input files from modification
            FileStream fs2 = File.Open(directory + "\\" + file2, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            FileStream catStream = File.Create(directory + "\\" + catFile, (int)(fs1.Length + fs2.Length));

            try
            {

                Console.WriteLine("Creating cat file...");
                byte[] buffer = new byte[1024];
                int bytesInBufferFilled = 0;
                while ((bytesInBufferFilled = ReadInto(fs1, buffer)) == buffer.Length)
                    WriteInto(catStream, buffer, bytesInBufferFilled);
                WriteInto(catStream, buffer, bytesInBufferFilled);
                while ((bytesInBufferFilled = ReadInto(fs2, buffer)) == buffer.Length)
                    WriteInto(catStream, buffer, bytesInBufferFilled);
                WriteInto(catStream, buffer, bytesInBufferFilled);
                //fs1.CopyTo(catStream, 1024);
                //fs2.CopyTo(catStream, 1024);
                catStream.Close();
                Console.WriteLine("Created cat file!");

                catStream = File.OpenRead(directory + "\\" + catFile);
                StreamChange changeTracker = new StreamChange(catStream, 4);
                catStream.Close();

                //Dump the hash lookup
                for (int i = 0; i < changeTracker.StreamPartitionHashLookup.Count; i++)
                    Console.WriteLine(i + " -> " + HashToString(changeTracker.StreamPartitionHashLookup[i]));

                FileWatcher outWatcher = new FileWatcher(directory + "\\" + catFile);
                outWatcher.watcher.Changed += (s, e) =>
                {
                    Console.WriteLine(e.ChangeType + " " + e.Name);
                    catStream = File.Open(directory + "\\" + catFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    foreach (Tuple<int, int> range in changeTracker.GetChangedPartitions(catStream, false))
                    {
                        Console.WriteLine(range.Item1 + " -> " + range.Item2);
                        if (range.Item2 < fs1.Length) //Write to fs1
                        {
                            fs1.Position = range.Item1;
                            catStream.Position = range.Item1;

                            int currentIndex = range.Item1;
                            while (currentIndex <= range.Item2)
                            {
                                //TODO: buffer
                                fs1.WriteByte((byte)catStream.ReadByte());
                                currentIndex++;
                            }
                        }
                        else if (range.Item1 >= fs1.Length) //Write to fs2
                        {
                            fs2.Position = range.Item1 - fs1.Length;
                            catStream.Position = range.Item1;

                            int currentIndex = range.Item1;
                            while (currentIndex <= range.Item2)
                            {
                                if (fs2.Position >= fs2.Length) //catstream is now longer than fs2
                                {
                                    fs2.SetLength(fs2.Position);
                                    break;
                                }
                                //TODO: buffer
                                byte b = (byte)catStream.ReadByte();
                                fs2.WriteByte(b);
                                currentIndex++;
                            }
                        }
                        else //range overlaps
                        {
                            fs1.Position = range.Item1;
                            catStream.Position = range.Item1;

                            int currentIndex = range.Item1;
                            while (currentIndex < fs1.Length)
                            {
                                //TODO: buffer
                                fs1.WriteByte((byte)catStream.ReadByte());
                                currentIndex++;
                            }
                            fs2.Position = 0;
                            while (currentIndex <= range.Item2)
                            {
                                if (fs2.Position >= fs2.Length) //catstream is now longer than fs2
                                {
                                    fs2.SetLength(fs2.Position);
                                    break;
                                }
                                //TODO: buffer
                                fs2.WriteByte((byte)catStream.ReadByte());
                                currentIndex++;
                            }
                        }

                    }

                    fs1.Flush();
                    fs2.Flush();

                    catStream.Close();
                };

                Console.ReadLine();
            }
            finally
            {
                if (fs1.CanWrite)
                {
                    fs1.Flush();
                    fs1.Close();
                }
                if (fs2.CanWrite)
                {
                    fs2.Flush();
                    fs2.Close();
                }
                if (catStream.CanWrite)
                {
                    catStream.Flush();
                    catStream.Close();
                }
            }
        }

        internal static IEnumerable<byte[]> Read(Stream stream, int bufferSize)
        {
            byte[] buffer = new byte[bufferSize];
            int bytesInBufferFilled = 0;
            while ((bytesInBufferFilled = Program.ReadInto(stream, buffer)) == buffer.Length)
                yield return buffer;
            if (bytesInBufferFilled != buffer.Length)
                yield return buffer;
        }

        /// <summary>
        /// Reads bytes into a buffer
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="buffer"></param>
        /// <returns>The number of bytes read into the buffer</returns>
        internal static int ReadInto(Stream stream, byte[] buffer)
        {
            if (stream.Position >= stream.Length)
                return 0;
            int min = (int)Math.Min(buffer.Length, stream.Length - stream.Position);
            stream.Read(buffer, 0, min);
            return min;
        }

        internal static void WriteInto(Stream stream, byte[] buffer, int bytesToWrite)
        {
            stream.Write(buffer, 0, bytesToWrite);
        }

        internal static byte[] StringToHash(String str)
        {
            return MD5.Create().ComputeHash(Encoding.UTF8.GetBytes(str));
        }

        internal static String HashToString(byte[] hashData)
        {
            StringBuilder sBuilder = new StringBuilder();

            // Loop through each byte of the hashed data  
            // and format each one as a hexadecimal string. 
            for (int i = 0; i < hashData.Length; i++)
            {
                sBuilder.Append(hashData[i].ToString("x2"));
            }

            // Return the hexadecimal string. 
            return sBuilder.ToString();
        }
    }

    class StreamChange
    {
        private int bufferSize;
        private MD5 md5 = MD5.Create();
        public List<byte[]> StreamPartitionHashLookup { get; private set; }

        /// <summary>
        /// Kepps track of a stream and can tell you what changed
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="bufferSize">The block size to use when calculating the hash
        /// Larger means more bytes are read into memory when calculating, but less memory is used to store the calculation, and more bandwidth is used.
        /// Smaller means less bytes are read into memory when calcualting, but more memory is used to store the calculation, and less bandwidth is used.</param>
        public StreamChange(Stream stream, int bufferSize)
        {
            this.bufferSize = bufferSize;
            StreamPartitionHashLookup = CalcHashLookup(stream);
        }

        public Tuple<int, int>[] GetChangedPartitions(Stream stream, bool update)
        {
            stream.Position = 0;
            List<Tuple<int, int>> byteRanges = new List<Tuple<int, int>>();
            List<byte[]> comparisonTable = CalcHashLookup(stream);
            foreach (int index in GetNonMatchingIndices(StreamPartitionHashLookup, comparisonTable))
            {
                byteRanges.Add(new Tuple<int, int>(index * bufferSize, (index+1)*bufferSize - 1));
            }
            if (update)
                StreamPartitionHashLookup = comparisonTable;
            return byteRanges.ToArray();
        }

        private List<byte[]> CalcHashLookup(Stream stream)
        {
            byte[] buffer = new byte[bufferSize];
            int bytesInBufferFilled = 0;
            List<byte[]> hashLookup = new List<byte[]>();
            while ((bytesInBufferFilled = Program.ReadInto(stream, buffer)) == buffer.Length)
                hashLookup.Add(md5.ComputeHash(buffer));
            if (bytesInBufferFilled != 0)
                hashLookup.Add(md5.ComputeHash(buffer));

            return hashLookup;
        }

        private static List<int> GetNonMatchingIndices(List<byte[]> b1, List<byte[]> b2)
        {
            List<int> changed = new List<int>();
            int minLength = Math.Min(b1.Count, b2.Count);
            for (int i = 0; i < minLength; i++)
                if (!AreEqual(b1[i], b2[i]))
                    changed.Add(i);

            int maxLength = Math.Max(b1.Count, b2.Count);
            for (int i = minLength; i < maxLength; i++)
                changed.Add(i);

            return changed;
        }

        private static Boolean AreEqual(byte[] arr1, byte[] arr2)
        {
            if (arr1.Length != arr2.Length)
                return false;
            for (int i = 0; i < arr1.Length; i++)
                if (arr1[i] != arr2[i])
                    return false;
            return true;
        }
    }

    class FileWatcher
    {
        public FileSystemWatcher watcher;

        public FileWatcher(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("Can't find file at: " + filePath, "filePath");

            watcher = new FileSystemWatcher(Path.GetDirectoryName(filePath), Path.GetFileName(filePath));
            watcher.EnableRaisingEvents = true;
        }
    }
}
