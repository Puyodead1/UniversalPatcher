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

using System.Diagnostics;

namespace PatchGenerator
{
    public class PatchCreator
    {
        private string previousVersionRoot;
        private string currentVersionRoot;
        private string outputRoot;
        private int diffQuality = 3;
        private PatchManifest patchManifest;
        private string patchDataPath;

        public PatchCreator(string previousVersionRoot, string currentVersionRoot, string outputRoot)
        {
            this.previousVersionRoot = PatchUtils.GetPathWithTrailingSeparatorChar(previousVersionRoot);
            this.currentVersionRoot = PatchUtils.GetPathWithTrailingSeparatorChar(currentVersionRoot);
            this.outputRoot = PatchUtils.GetPathWithTrailingSeparatorChar(outputRoot);
            this.patchManifest = new PatchManifest();
            this.patchDataPath = Path.Combine(outputRoot, Constants.PATCH_DATA_FOLDER_NAME);
        }

        public int CreateIncrementalPatch()
        {
            DirectoryInfo rootDirectory = new DirectoryInfo(currentVersionRoot);
            TraverseIncrementalPatchRecursively(rootDirectory, "");
            //CreatePatches(rootDirectory);

            CreateChecksumList();

            File.WriteAllBytes(Path.Combine(outputRoot, "manifest.json"), patchManifest.Serialize());
            return 0;
        }

        // Creates a list of all files in the current version and their checksums
        private void CreateChecksumList()
        {
            var files = Directory.GetFiles(currentVersionRoot, "*", SearchOption.AllDirectories);

            foreach (var file in files)
            {
                var fileInfo = new FileInfo(file);
                var relativePath = file.Replace(currentVersionRoot, "").TrimStart('\\');

                patchManifest.Checksums.Add(new ChecksumEntry
                {
                    Path = relativePath,
                    Size = fileInfo.Length,
                    Hash = fileInfo.Md5Hash()
                });
            }   
        }

        private int CreatePatches(DirectoryInfo root)
        {
            var oldFiles = Directory.GetFiles(previousVersionRoot, "*", SearchOption.AllDirectories);
            var newFiles = Directory.GetFiles(currentVersionRoot, "*", SearchOption.AllDirectories);

            var oldFilesRelative = oldFiles.Select(x => x.Replace(previousVersionRoot, "").TrimStart('\\'));
            var newFilesRelative = newFiles.Select(x => x.Replace(currentVersionRoot, "").TrimStart('\\'));

            var deletedFiles = oldFilesRelative.Except(newFilesRelative);
            var addedFiles = newFilesRelative.Except(oldFilesRelative);
            var modifiedFiles = newFilesRelative.Except(addedFiles).Except(deletedFiles);


            foreach (var file in addedFiles)
            {
                Console.Write($"Processing new file {file}");

                Stopwatch stopwatch = new Stopwatch();
                stopwatch.Start(); // Start timing

                var newFile = new FileInfo(Path.Combine(currentVersionRoot, file));
                var outFile = Path.Combine(outputRoot, file);
                string patchFilePath = outFile + Constants.PATCH_FILE_SUFFIX;

                // compress the new file
                ZipUtils.CompressFile(newFile.FullName, patchFilePath, true);

                stopwatch.Stop(); // Stop timing
                TimeSpan elapsed = stopwatch.Elapsed;

                Console.WriteLine($"\rProcessing new file {file}: 100% (Took {elapsed.TotalSeconds:F2} seconds)");

                // add file to manifest
                var patchFile = new PatchFile
                {
                    Path = file,
                    NewHash = newFile.Md5Hash(),
                    PatchHash = new FileInfo(patchFilePath).Md5Hash()
                };
                patchManifest.AddedFiles.Add(patchFile);
            }

            foreach (var file in deletedFiles)
            {
                Console.WriteLine($"Found Deleted File: {file}");
                patchManifest.DeletedFiles.Add(file);
            }

            foreach (var file in modifiedFiles)
            {
                var oldFile = new FileInfo(Path.Combine(previousVersionRoot, file));
                var newFile = new FileInfo(Path.Combine(currentVersionRoot, file));
                var diffFileTemp = Path.Combine(outputRoot, file + Constants.PATCH_FILE_TEMP_SUFFIX);
                var diffFileCompressed = Path.Combine(outputRoot, file + Constants.PATCH_FILE_SUFFIX);

                // compute the file hash of the old file
                var oldHash = oldFile.Md5Hash();

                // compute the file hash of the new file
                var newHash = newFile.Md5Hash();

                // check if the file has actually changed
                if (oldHash == newHash)
                    continue;

                Console.Write($"Processing modified file {file}");

                // make sure the directory exists
                Directory.CreateDirectory(Path.GetDirectoryName(diffFileTemp)!);

                Stopwatch stopwatch = new Stopwatch();
                stopwatch.Start(); // Start timing

                OctoUtils.CalculateDelta(oldFile.FullName, newFile.FullName, diffFileTemp, diffQuality);

                // compress the diff file
                ZipUtils.CompressFile(diffFileTemp, diffFileCompressed, true);

                // delete the temp diff file
                File.Delete(diffFileTemp);

                stopwatch.Stop(); // Stop timing
                TimeSpan elapsed = stopwatch.Elapsed;

                Console.WriteLine($"\rProcessing modified file {file}: 100% (Took {elapsed.TotalSeconds:F2} seconds)");

                var patchFile = new PatchFile { 
                    Path = file,
                    OldSize = oldFile.Length,
                    NewSize = newFile.Length,
                    OldHash = oldFile.Md5Hash(),
                    NewHash = newFile.Md5Hash(),
                    PatchHash = new FileInfo(diffFileCompressed).Md5Hash()
                };
                patchManifest.ModifiedFiles.Add(patchFile);
            }

            return 0;
        }

        private void TraverseIncrementalPatchRecursively(DirectoryInfo directory, string relativePath)
        {
            FileInfo[] files = directory.GetFiles();
            for (int i = 0; i < files.Length; i++)
            {
                FileInfo targetFile = files[i];
                string newFileRelativePath = Path.Combine(relativePath, targetFile.Name);
                string oldFileAbsolutePath = Path.Combine(previousVersionRoot, newFileRelativePath);
                var diffFileTemp = Path.Combine(patchDataPath, newFileRelativePath + Constants.PATCH_FILE_TEMP_SUFFIX);
                var diffFileCompressed = Path.Combine(patchDataPath, newFileRelativePath + Constants.PATCH_FILE_SUFFIX);

                bool oldFileExists = File.Exists(oldFileAbsolutePath);

                if (!oldFileExists)
                {
                    Console.WriteLine($"Found New File: {newFileRelativePath}");
                    ZipUtils.CompressFile(targetFile.FullName, diffFileCompressed, true);
                    var patchFile = new PatchFile
                    {
                        Path = newFileRelativePath,
                        NewSize = targetFile.Length,
                        NewHash = targetFile.Md5Hash(),
                        PatchHash = new FileInfo(diffFileCompressed).Md5Hash()
                    };
                    patchManifest.AddedFiles.Add(patchFile);
                }
                else
                {
                    FileInfo prevVersion = new FileInfo(oldFileAbsolutePath);
                    if (!targetFile.MatchesSignature(prevVersion))
                    {
                        Directory.CreateDirectory(Path.Combine(patchDataPath, relativePath));

                        Console.WriteLine($"Processing Modified File: {newFileRelativePath}");

                        OctoUtils.CalculateDelta(oldFileAbsolutePath, targetFile.FullName, diffFileTemp, diffQuality);

                        // compress diff file
                        ZipUtils.CompressFile(diffFileTemp, diffFileCompressed, true);

                        // remove temp diff file
                        File.Delete(diffFileTemp);

                        var patchFile = new PatchFile
                        {
                            Path = newFileRelativePath,
                            OldSize = prevVersion.Length,
                            NewSize = targetFile.Length,
                            OldHash = prevVersion.Md5Hash(),
                            NewHash = targetFile.Md5Hash(),
                            PatchHash = new FileInfo(diffFileCompressed).Md5Hash() 
                        };
                        patchManifest.ModifiedFiles.Add(patchFile);
                    }
                }
            }

            DirectoryInfo[] subDirectories = directory.GetDirectories();
            for (int i = 0; i < subDirectories.Length; i++)
            {
                string directoryRelativePath = relativePath + subDirectories[i].Name + Path.DirectorySeparatorChar;
                TraverseIncrementalPatchRecursively(subDirectories[i], directoryRelativePath);
            }
        }
    }
}
