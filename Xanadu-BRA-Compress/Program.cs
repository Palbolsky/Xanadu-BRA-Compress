using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Xanadu_BRA_Compress
{
    /// <summary>
    /// Compresses .BRA files from Tokyo Xanadu, PC Version, Written 17th of March 2021
    /// </summary>
    class Program
    {
        /// <summary>
        /// Stores the archive in memory as a list of bytes.
        /// </summary>
        static List<byte> xanaduArchive;

        /// <summary>
        /// Path to the .BRA Archive
        /// </summary>
        static string filePath;

        /// <summary>
        /// Path to the folder contains the uncompressed files of .BRA Archive
        /// </summary>
        static string folderPath;

        /// <summary>
        /// Defines Xanadu's file header.
        /// </summary>
        static XanaduStructs.XanaduHeader fileHeader;

        /// <summary>
        /// Defines a list of files stored within the archive.
        /// </summary>
        static List<XanaduStructs.XanaduFileEntry> fileList;

        /// <summary>
        /// Compress or not all files.
        /// </summary>
        static bool compressAllFiles;

        /// <summary>
        /// January 1st 1970
        /// </summary>
        static readonly DateTime originDate = new DateTime(1970, 1, 1);

        /// <summary>
        /// Length difference between old length of xanadu archive and the newer.
        /// </summary>
        static int diffLength;

        /// <summary>
        /// Entry Point!
        /// </summary>
        static void Main(string[] args)
        {
            Console.WriteLine("Xanadu .BRA Archive Importer by Palbolsky (based on Xanadu .BRA Archive Exporter by Sewer56lol)");
            Console.WriteLine("For personal use.");
            Console.WriteLine("Usage: Xanadu_BRA_Compress <file.bra> <folder>");
            Console.WriteLine("Compress all files, even unmodified ones: Xanadu_BRA_Compress <file.bra> <folder> -all");
            if (args?.Length < 2)
                return;

            // Set file and folder path.
            filePath = args[0];
            folderPath = args[1];

            foreach (string argument in args) { if (argument == "-all") { compressAllFiles = true; } }

            // Read in the archive file from 1st argument.
            BenchmarkMethod(ReadArchive, "Reading Archive");

            // Parse file Header.
            BenchmarkMethod(ParseHeader, "Parsing Archive");

            // Read File Details
            BenchmarkMethod(PopulateFileEntries, "Parsing File Details");

            // Compress files in .BRA Archive
            BenchmarkMethod(CompressFiles, "Compress Files");
        }

        /// <summary>
        /// Compress files in .BRA Archive
        /// </summary>
        private static void CompressFiles()
        {
            Console.WriteLine("Work in progress...");

            int countCompressedFiles = 0;
            int countIgnoredFiles = 0;

            for(int i = 0; i < fileList.Count; i++) 
            {               
                // Check if the file exists
                String fileNamePath = Path.Combine(folderPath, fileList[i].fileName);     
                if (!File.Exists(fileNamePath))
                    continue;

                // Check if the file have been modified
                DateTime fileDateTime = File.GetLastWriteTime(fileNamePath);
                DateTime archiveTime = originDate.AddSeconds(fileList[i].filePackedTime);                
                if (!compressAllFiles && (UInt32)new TimeSpan(fileDateTime.Ticks).TotalSeconds == (UInt32)new TimeSpan(archiveTime.Ticks).TotalSeconds)
                {
                    countIgnoredFiles++;
                    continue;
                }

                // Shows filename path
                Console.WriteLine(fileNamePath);              

                // Compress a file
                using FileStream fileUncompressed = File.OpenRead(fileNamePath);
                MemoryStream compressStream = new MemoryStream();
                using DeflateStream compressor = new DeflateStream(compressStream, CompressionMode.Compress, false);

                fileUncompressed.CopyTo(compressor);
                compressor.Close();
                byte[] fileCompressed = compressStream.ToArray();

                if (fileCompressed.Length > (fileList[i].compressedSize - 0x10))
                {
                    xanaduArchive.RemoveRange((int)fileList[i].fileOffset + 0x10, (int) fileList[i].compressedSize - 0x10);
                    xanaduArchive.InsertRange((int)fileList[i].fileOffset + 0x10, fileCompressed);

                    // update fileoffset of fileEntry
                    for (int x = i + 1; x < fileList.Count; x++)
                    {
                        XanaduStructs.XanaduFileEntry fileEntry = fileList[x];
                        fileEntry.fileOffset += (UInt32) (fileCompressed.Length - fileList[i].compressedSize + 0x10);
                        fileList[x] = fileEntry;
                    }

                    // update zsize and size                   
                    diffLength += (int)(fileCompressed.Length - fileList[i].compressedSize + 0x10);
                    fileList[i] = Update(fileUncompressed, fileCompressed, fileList[i]);    
                }
                else if (fileCompressed.Length == (fileList[i].compressedSize - 0x10))
                {
                    // write file compressed in xanadu archive
                    Utilities.Replace(xanaduArchive, (int)fileList[i].fileOffset + 0x10, fileCompressed);                   
                    fileList[i] = Update(fileUncompressed, fileCompressed, fileList[i]);
                }
                else
                {
                    // write file compressed in xanadu archive
                    Utilities.Replace(xanaduArchive, (int)fileList[i].fileOffset + 0x10, fileCompressed);
                    for (int x = 0; x < (fileList[i].compressedSize - fileCompressed.Length - 0x10); x++)
                    {
                        xanaduArchive[(int) fileList[i].fileOffset + 0x10 + fileCompressed.Length + x] = 0;
                    }
                    fileList[i] = Update(fileUncompressed, fileCompressed, fileList[i]);
                }

                // update packedTime               
                fileList[i] = UpdatePackedTime(fileList[i], fileDateTime);
                countCompressedFiles++;
            }

            // update offset archive xanadu
            if (diffLength > 0)
            {
                for (int i = 0; i < fileList.Count; i++)
                {
                   Utilities.Replace(xanaduArchive, (int) fileList[i].offset + diffLength + 0x14, BitConverter.GetBytes((UInt32)fileList[i].fileOffset));                  
                }
                Utilities.Replace(xanaduArchive, 0x08, BitConverter.GetBytes((UInt32)(fileHeader.fileEntryOffset + diffLength)));               
            }            

            // Show number of files compressed added and ignored
            Console.Write("Files compressed added: " + countCompressedFiles + " | " + "Files ignored: " + countIgnoredFiles + " | ");

            // ignore write and backup if no file has been added
            if (countCompressedFiles == 0)
                return;

            // create backup to xanadu archive
            String fileBakPath = filePath + ".bak";
            if (File.Exists(fileBakPath))
                File.Delete(fileBakPath);
            File.Copy(filePath, fileBakPath);

            // write in xanadu archive
            File.Create(filePath).Write(xanaduArchive.ToArray());
        }

        /// <summary>
        /// Benchmarks an individual method call.
        /// </summary>
        private static void BenchmarkMethod(Action method, String actionText)
        {
            // Stopwatch to benchmark every action.
            Stopwatch performanceWatch = new Stopwatch();

            // Print out the action
            Console.Write(actionText + " | ");

            // Start the stopwatch.
            performanceWatch.Start();

            // Run the method.
            method();

            // Stop the stopwatch
            performanceWatch.Stop();

            // Print the results.
            long elapsed = performanceWatch.ElapsedMilliseconds;
            if (elapsed >= 1000)
                Console.WriteLine(((double)elapsed / 1000) + "s");
            else
                Console.WriteLine(elapsed + "ms");
        }

        /// <summary>
        /// Parses the header of Tokyo Xanadu's .BRA Archive.
        /// </summary>
        private static void ParseHeader()
        {
            // Allocate Memory
            fileHeader = new XanaduStructs.XanaduHeader();

            // Convert xanaduArchive list to array
            byte[] xanaduArchiveArray = xanaduArchive.ToArray();

            // Read Header Contents
            fileHeader.fileHeader = Encoding.ASCII.GetString(xanaduArchiveArray.SubArrayToNullTerminator(0));
            fileHeader.compressionType = BitConverter.ToUInt32(xanaduArchiveArray, 4);
            fileHeader.fileEntryOffset = BitConverter.ToUInt32(xanaduArchiveArray, 8);
            fileHeader.fileCount = BitConverter.ToUInt32(xanaduArchiveArray, 12);

            Console.Write(fileHeader + " | ");
        }

        /// <summary>
        /// Populates the details of each file entry within the archive.
        /// </summary>
        private static void PopulateFileEntries()
        {
            // Allocate Memory
            fileList = new List<XanaduStructs.XanaduFileEntry>((int) fileHeader.fileCount);

            // Convert xanaduArchive list to array
            byte[] xanaduArchiveArray = xanaduArchive.ToArray();

            // Create file pointer & set to first entry.
            UInt32 filePointer = fileHeader.fileEntryOffset;

            // Read each file entry
            for (int x = 0; x < fileHeader.fileCount; x++)
            {
                // Generate file entry.
                XanaduStructs.XanaduFileEntry xanaduFileEntry = new XanaduStructs.XanaduFileEntry();

                // Save start pointer of entry details
                xanaduFileEntry.offset = filePointer;

                // Read file entry details & increment pointer.
                xanaduFileEntry.filePackedTime = BitConverter.ToUInt32(xanaduArchiveArray, (int) filePointer);
                filePointer += sizeof(UInt32);

                xanaduFileEntry.unknown = BitConverter.ToUInt32(xanaduArchiveArray, (int) filePointer);
                filePointer += sizeof(UInt32);

                xanaduFileEntry.compressedSize = BitConverter.ToUInt32(xanaduArchiveArray, (int) filePointer);
                filePointer += sizeof(UInt32);

                xanaduFileEntry.uncompressedSize = BitConverter.ToUInt32(xanaduArchiveArray, (int) filePointer);
                filePointer += sizeof(UInt32);

                xanaduFileEntry.fileNameLength = BitConverter.ToUInt16(xanaduArchiveArray, (int) filePointer);
                filePointer += sizeof(UInt16);

                xanaduFileEntry.fileFlags = BitConverter.ToUInt16(xanaduArchiveArray, (int) filePointer);
                filePointer += sizeof(UInt16);

                xanaduFileEntry.fileOffset = BitConverter.ToUInt32(xanaduArchiveArray, (int) filePointer);
                filePointer += sizeof(UInt32);
               
                xanaduFileEntry.fileName = Encoding.ASCII.GetString(xanaduArchiveArray.SubArray((int)filePointer, (int)xanaduFileEntry.fileNameLength)); 
                filePointer += (uint) xanaduFileEntry.fileName.Length;

                // Sanitize File name
                xanaduFileEntry.fileName = xanaduFileEntry.fileName.ForceValidFilePath();

                // Trim file extension
                xanaduFileEntry.fileName = xanaduFileEntry.fileName.Substring(0, xanaduFileEntry.fileName.IndexOf(".") + 4);
                
                // Add onto list
                fileList.Add(xanaduFileEntry);
            }
        }

        /// <summary>
        /// Reads the supplied archive file.
        /// </summary>
        private static void ReadArchive() { 
            xanaduArchive = new List<byte>(File.ReadAllBytes(filePath)); 
        }

        /// <summary>
        /// Update the compressed and uncompressed values of xanadu archive and xanadu file entry.
        /// </summary>
        /// <param name="fileUncompressed">File no compressed</param>
        /// <param name="fileCompressed">File compressed</param>
        /// <param name="file">Xanadu file entry</param>
        /// <returns>The xanadu file entry updated</returns>  
        private static XanaduStructs.XanaduFileEntry Update(FileStream fileUncompressed, byte[] fileCompressed, XanaduStructs.XanaduFileEntry file)
        {
            // update zsize and size of xanaduArchive (fileData)
            Utilities.Replace(xanaduArchive, (int)file.fileOffset, BitConverter.GetBytes((UInt32)fileUncompressed.Length)); // size
            Utilities.Replace(xanaduArchive, (int)file.fileOffset + sizeof(UInt32), BitConverter.GetBytes((UInt32)fileCompressed.Length)); // zsize          

            // update zsize and size of xanaduArchive (fileEntry)           
            Utilities.Replace(xanaduArchive, (int)file.offset + diffLength + sizeof(UInt32) * 2, BitConverter.GetBytes((UInt32)fileCompressed.Length + 0x10)); // zsize
            Utilities.Replace(xanaduArchive, (int)file.offset + diffLength + sizeof(UInt32) * 3, BitConverter.GetBytes((UInt32)fileUncompressed.Length)); // size           

            // update zsize and size of fileEntry
            file.compressedSize = (UInt32)fileCompressed.Length + 0x10;
            file.uncompressedSize = (UInt32)fileUncompressed.Length;
            return file;
        }

        /// <summary>
        /// Update the packedtime value of xanadu archive and xanadu file entry.
        /// </summary>
        /// <param name="file">Xanadu file entry</param>
        /// <param name="fileDateTime">Date of new compressed file</param>
        /// <returns>The xanadu file entry updated</returns>
        private static XanaduStructs.XanaduFileEntry UpdatePackedTime(XanaduStructs.XanaduFileEntry file, DateTime fileDateTime)
        {
            long elapsedTicks = fileDateTime.Ticks - originDate.Ticks;
            TimeSpan elapsedSpan = new TimeSpan(elapsedTicks);
            Utilities.Replace(xanaduArchive, (int)file.offset + diffLength, BitConverter.GetBytes((UInt32)elapsedSpan.TotalSeconds));
            file.filePackedTime = (UInt32)elapsedSpan.TotalSeconds;
            return file;
        }
    }
}
