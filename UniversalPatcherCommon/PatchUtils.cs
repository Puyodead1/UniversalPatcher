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

using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PatchGenerator
{
    public static class PatchUtils
    {
        public static void DeleteDirectory(string path)
        {
            if (Directory.Exists(path))
            {
                // Deleting a directory immediately after deleting a file inside it can sometimes
                // throw IOException; in such cases, waiting for a short time should resolve the issue
                for (int i = 4; i >= 0; i--)
                {
                    if (i > 0)
                    {
                        try
                        {
                            DeleteDirectoryRecursive(new DirectoryInfo(path));
                            break;
                        }
                        catch (IOException)
                        {
                            Thread.Sleep(500);
                        }
                    }
                    else
                        DeleteDirectoryRecursive(new DirectoryInfo(path));
                }

                while (Directory.Exists(path))
                    Thread.Sleep(100);
            }
        }

        // Avoids occasional UnauthorizedAccessException
        // Credit: https://stackoverflow.com/a/8521573/2373034
        private static void DeleteDirectoryRecursive(DirectoryInfo directory)
        {
            directory.Attributes = FileAttributes.Normal;

            FileInfo[] files = directory.GetFiles();
            for (int i = 0; i < files.Length; i++)
            {
                files[i].Attributes = FileAttributes.Normal;
                files[i].Delete();
            }

            DirectoryInfo[] subDirectories = directory.GetDirectories();
            for (int i = 0; i < subDirectories.Length; i++)
                DeleteDirectoryRecursive(subDirectories[i]);

            directory.Delete(true);
        }

        public static void CopyFile(string from, string to)
        {
            // Replacing a file that is in use can throw IOException; in such cases,
            // waiting for a short time might resolve the issue
            for (int i = 8; i >= 0; i--)
            {
                if (i > 0)
                {
                    try
                    {
                        File.Copy(from, to, true);
                        break;
                    }
                    catch (IOException)
                    {
                        Thread.Sleep(500);
                    }
                }
                else
                    File.Copy(from, to, true);
            }
        }

        public static void MoveFile(string fromAbsolutePath, string toAbsolutePath)
        {
            if (File.Exists(toAbsolutePath))
            {
                CopyFile(fromAbsolutePath, toAbsolutePath);
                File.Delete(fromAbsolutePath);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(toAbsolutePath));
                File.Move(fromAbsolutePath, toAbsolutePath);
            }
        }

        #region Extensions
        public static string Md5Hash(this FileInfo fileInfo)
        {
            using (var md5 = MD5.Create())
            {
                using (var stream = File.OpenRead(fileInfo.FullName))
                {
                    return BitConverter.ToString(md5.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                }
            }
        }

        public static bool MatchesSignature(this FileInfo fileInfo, long fileSize, string md5)
        {
            if (fileInfo.Length == fileSize)
                return fileSize > long.MaxValue || fileInfo.Md5Hash() == md5;

            return false;
        }

        public static bool MatchesSignature(this FileInfo fileInfo, FileInfo other)
        {
            long fileSize = other.Length;
            if (fileInfo.Length == fileSize)
                return fileSize > long.MaxValue || fileInfo.Md5Hash() == other.Md5Hash();

            return false;
        }

        public static bool PathMatchesPattern(this List<Regex> patterns, string path)
        {
            for (int i = 0; i < patterns.Count; i++)
            {
                if (patterns[i].IsMatch(path))
                    return true;
            }

            return false;
        }
        #endregion Extensions

        public static string GetPathWithTrailingSeparatorChar(string path)
        {
            char trailingChar = path[path.Length - 1];
            if (trailingChar != Path.DirectorySeparatorChar && trailingChar != Path.AltDirectorySeparatorChar)
                path += Path.DirectorySeparatorChar;

            return path;
        }
    }

    public struct PatchFile
    {
        public string Path { get; set; }
        public long OldSize { get; set; }
        public long NewSize { get; set; }
        public string OldHash { get; set; }
        public string NewHash { get; set; }
        public string PatchHash { get; set; }
    }

    public struct ChecksumEntry
    {
        public string Path { get; set; }
        public long Size { get; set; }
        public string Hash { get; set; }
    }

    public struct PatchManifest
    {
        //public string FromVersion { get; set; }
        //public string ToVersion { get; set; }
        public List<PatchFile> AddedFiles { get; set; }
        public List<string> DeletedFiles { get; set; }
        public List<PatchFile> ModifiedFiles { get; set; }
        public List<ChecksumEntry> Checksums { get; set; }

        public PatchManifest()
        {
            //FromVersion = from;
            //ToVersion = to;
            AddedFiles = new List<PatchFile>();
            DeletedFiles = new List<string>();
            ModifiedFiles = new List<PatchFile>();
            Checksums = new List<ChecksumEntry>();
        }

        public byte[] Serialize()
        {
            return JsonSerializer.SerializeToUtf8Bytes(this);
        }

        public static PatchManifest Deserialize(string path)
        {
            return JsonSerializer.Deserialize<PatchManifest>(File.ReadAllText(path));
        }
    }
}
