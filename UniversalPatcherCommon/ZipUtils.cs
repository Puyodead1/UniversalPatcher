/**
 * UniversalPatcher
 * Copyright (C) 2023 Puyodead1
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program.  If not, see <https://www.gnu.org/licenses/>.
 */

using ICSharpCode.SharpZipLib.Tar;
using System.Text.RegularExpressions;
using Compression = SevenZip.Compression;

namespace PatchGenerator
{
    public static class ZipUtils
    {
        public static void CompressFile(string inFile, string outFile, bool compression)
        {
            using (FileStream input = new FileStream(inFile, FileMode.Open, FileAccess.Read))
            using (FileStream output = new FileStream(outFile, FileMode.Create))
            {
                if (compression)
                {
                    // Credit: http://stackoverflow.com/questions/7646328/how-to-use-the-7z-sdk-to-compress-and-decompress-a-file
                    Compression.LZMA.Encoder coder = new Compression.LZMA.Encoder();

                    // Write the encoder properties
                    coder.WriteCoderProperties(output);

                    // Write the decompressed file size.
                    output.Write(BitConverter.GetBytes(input.Length), 0, 8);

                    // Encode the file.
                    coder.Code(input, output, input.Length, -1, null);
                }
                else
                    input.CopyTo(output);
            }
        }

        public static void DecompressFile(string inFile, string outFile, bool compression)
        {
            using (FileStream input = new FileStream(inFile, FileMode.Open, FileAccess.Read))
            using (FileStream output = new FileStream(outFile, FileMode.Create))
            {
                if (compression)
                {
                    // Credit: http://stackoverflow.com/questions/7646328/how-to-use-the-7z-sdk-to-compress-and-decompress-a-file
                    Compression.LZMA.Decoder coder = new Compression.LZMA.Decoder();

                    // Read the decoder properties
                    byte[] properties = new byte[5];
                    input.Read(properties, 0, 5);

                    // Read in the decompress file size.
                    byte[] fileLengthBytes = new byte[8];
                    input.Read(fileLengthBytes, 0, 8);
                    long fileLength = BitConverter.ToInt64(fileLengthBytes, 0);

                    coder.SetDecoderProperties(properties);
                    coder.Code(input, output, input.Length, fileLength, null);
                }
                else
                    input.CopyTo(output);
            }
        }

        public static void CompressFolder(string inFolder, string outFile, bool compression)
        {
            CompressFolder(inFolder, outFile, compression, new List<Regex>(0));
        }

        internal static void CompressFolder(string inFolder, string outFile, bool compression, List<Regex>? ignoredPathsRegex)
        {
            string tarFilePath = outFile + "tmptar";

            // Source: https://github.com/icsharpcode/SharpZipLib/wiki/GZip-and-Tar-Samples#-create-a-tar-or-tgz-with-control-over-filenames-and-data-source
            using (FileStream outputStream = File.Create(tarFilePath))
            using (TarOutputStream tarOutputStream = new TarOutputStream(outputStream))
            {
                CreateTarRecursive(tarOutputStream, new byte[32 * 1024], new DirectoryInfo(inFolder), "", ignoredPathsRegex);
            }

            CompressFile(tarFilePath, outFile, compression);
            File.Delete(tarFilePath);
        }

        public static void DecompressFolder(string inFile, string outFolder, bool compression)
        {
            string tarFilePath = outFolder + "tmptar.tar";
            DecompressFile(inFile, tarFilePath, compression);

            // Source: https://github.com/icsharpcode/SharpZipLib/wiki/GZip-and-Tar-Samples#--simple-full-extract-from-a-tar-archive
            using (Stream inStream = File.OpenRead(tarFilePath))
            using (TarArchive tarArchive = TarArchive.CreateInputTarArchive(inStream))
            {
                tarArchive.ExtractContents(outFolder);
            }

            File.Delete(tarFilePath);
        }

        // Source: https://github.com/icsharpcode/SharpZipLib/wiki/GZip-and-Tar-Samples#-create-a-tar-or-tgz-with-control-over-filenames-and-data-source
        private static void CreateTarRecursive(TarOutputStream tarOutputStream, byte[] fileCopyBuffer, DirectoryInfo directory, string relativePath, List<Regex> ignoredPathsRegex)
        {
            FileInfo[] files = directory.GetFiles();
            for (int i = 0; i < files.Length; i++)
            {
                string fileRelativePath = relativePath + files[i].Name;
                if (!ignoredPathsRegex.PathMatchesPattern(fileRelativePath))
                {
                    using (Stream inputStream = File.OpenRead(files[i].FullName))
                    {
                        TarEntry tarEntry = TarEntry.CreateTarEntry(fileRelativePath.Replace('\\', '/'));
                        tarEntry.Size = inputStream.Length;
                        tarOutputStream.PutNextEntry(tarEntry);

                        int numRead;
                        while ((numRead = inputStream.Read(fileCopyBuffer, 0, fileCopyBuffer.Length)) > 0)
                            tarOutputStream.Write(fileCopyBuffer, 0, numRead);
                    }

                    tarOutputStream.CloseEntry();
                }
            }

            DirectoryInfo[] subDirectories = directory.GetDirectories();
            for (int i = 0; i < subDirectories.Length; i++)
            {
                string directoryRelativePath = relativePath + subDirectories[i].Name + Path.DirectorySeparatorChar;
                if (!ignoredPathsRegex.PathMatchesPattern(directoryRelativePath))
                    CreateTarRecursive(tarOutputStream, fileCopyBuffer, subDirectories[i], directoryRelativePath, ignoredPathsRegex);
            }
        }

        // Credit: https://stackoverflow.com/a/5730893/2373034
        private static void CopyTo(this Stream input, Stream output)
        {
            byte[] buffer = new byte[8 * 1024];
            int bytesRead;

            while ((bytesRead = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                output.Write(buffer, 0, bytesRead);
            }
        }
    }
}
