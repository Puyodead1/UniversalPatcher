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

using System.CommandLine;
using PatchGenerator;

namespace Patcher
{
    public class Patcher
    {
        static async Task<int> Main(string[] args)
        {
            var installFolder = new Argument<string?>(
                name: "install folder",
                description: "Path to installation folder");

            var rootCommand = new RootCommand("UniversalPatcher - CommandLinePatcher")
            {
                installFolder,
            };

            rootCommand.SetHandler((installFolderValue) =>
            {
                Patch(installFolderValue!);
            },
            installFolder);

            return await rootCommand.InvokeAsync(args);
        }

        static int Patch(string installFolder)
        {
            if (!Directory.Exists(installFolder))
            {
                Console.WriteLine($"Installation folder does not exist!");
                return 1;
            }

            if (!Directory.Exists(Constants.PATCH_DATA_FOLDER_NAME))
            {
                Console.WriteLine($"PatchData is missing!");
                return 1;
            }

            if (!File.Exists(Constants.PATCH_MANIFEST_NAME))
            {
                Console.WriteLine($"Patch manifest is missing!");
                return 1;
            }

            Console.WriteLine("Loading patch manifest...");

            // load manifest
            PatchManifest patchManifest = PatchManifest.Deserialize(Constants.PATCH_MANIFEST_NAME);

            Console.WriteLine($"This will patch the installation at {installFolder}, press any key to continue.");
            Console.ReadKey();

            var patchResult = DoPatch(patchManifest, installFolder);
            if (patchResult != 0)
            {
                Console.WriteLine("[Patcher] Patching failed!");
                return patchResult;
            }

            DoVerify(patchManifest, installFolder);
            
            return 0;
        }

        private static int DoPatch(PatchManifest patchManifest, string installFolder)
        {
            // copy new files
            foreach (var file in patchManifest.AddedFiles)
            {
                Console.WriteLine($"[Patcher] Copying new file {file.Path}");

                var compressedPath = new FileInfo(Path.Combine(Constants.PATCH_DATA_FOLDER_NAME, file.Path + Constants.PATCH_FILE_SUFFIX));
                var decompressedPath = new FileInfo(Path.Combine(installFolder, file.Path));

                if (!compressedPath.Exists)
                {
                    Console.WriteLine($"[Patcher] Missing patch file {compressedPath.FullName}!");
                    return 1;
                }

                // check patch file integrity (aka compressed file)
                if (!compressedPath.Md5Hash().Equals(file.PatchHash))
                {
                    Console.WriteLine($"[Patcher] Patch file {compressedPath.FullName} is corrupted!");
                    return 1;
                }

                // ensure the parent directory exists
                Directory.CreateDirectory(Path.GetDirectoryName(decompressedPath.FullName)!);

                // decompress and copy
                ZipUtils.DecompressFile(compressedPath.FullName, decompressedPath.FullName, true);

                // check decompressed file integrity
                if (!decompressedPath.Md5Hash().Equals(file.NewHash))
                {
                    Console.WriteLine($"[Patcher] File {decompressedPath.FullName} is corrupted!");
                    return 1;
                }
            }

            // patch existing files
            foreach (var file in patchManifest.ModifiedFiles)
            {
                Console.WriteLine($"[Patcher] Patching file {file.Path}");

                var compressedPatchFileName = file.Path + Constants.PATCH_FILE_SUFFIX;
                var decompressedPatchFileName = file.Path + Constants.PATCH_FILE_TEMP_SUFFIX;
                var compressedPatchFile = new FileInfo(Path.Combine(Constants.PATCH_DATA_FOLDER_NAME, compressedPatchFileName));
                var decompressedPatchFile = new FileInfo(Path.Combine(Constants.PATCH_DATA_FOLDER_NAME, decompressedPatchFileName));
                var localFile = new FileInfo(Path.Combine(installFolder, file.Path));

                // check if the file already matches the new version
                if (localFile.Md5Hash().Equals(file.NewHash))
                {
                    // local file already matches the new version
                    Console.WriteLine($"[Patcher] File {file.Path} already patched.");
                    continue;
                }

                // check if the file matches the expected pre-patched hash
                if (!localFile.Md5Hash().Equals(file.OldHash))
                {
                    // local file does not match the expected hash
                    Console.WriteLine($"[Patcher] File {file.Path} doesn't match the expected hash, you might be trying to patch the wrong version!");
                    continue;
                }

                if (!compressedPatchFile.Exists)
                {
                    // compressed patch file is missing
                    Console.WriteLine($"[Patcher] Missing patch file {compressedPatchFileName}!");
                    return 1;
                }

                // check patch file integrity
                if (!compressedPatchFile.Md5Hash().Equals(file.PatchHash))
                {
                    // compressed patch file hash doesnt match
                    Console.WriteLine($"[Patcher] Patch file {compressedPatchFileName} is corrupted!");
                    return 1;
                }

                if (!localFile.Exists || !localFile.MatchesSignature(file.OldSize, file.OldHash))
                {
                    // existing local file is corrupt or wrong version
                    return 1;
                }

                // decompress and copy
                ZipUtils.DecompressFile(compressedPatchFile.FullName, decompressedPatchFile.FullName, true);

                // patch
                var tempOutputPath = new FileInfo(localFile.FullName + "_.tmp");
                OctoUtils.ApplyDelta(localFile.FullName, tempOutputPath.FullName, decompressedPatchFile.FullName);

                if (!tempOutputPath.MatchesSignature(file.NewSize, file.NewHash))
                {
                    // the patched file doesnt match the expected size and hash
                    Console.WriteLine($"[Patcher] Patched file {file.Path} failed integrity check!");
                    return 1;
                }

                // replace the local file with the patched one
                Directory.CreateDirectory(Path.GetDirectoryName(localFile.FullName));
                PatchUtils.CopyFile(tempOutputPath.FullName, localFile.FullName);

                // delete temp files
                tempOutputPath.Delete();
                decompressedPatchFile.Delete();

                Console.WriteLine($"[Patcher] Successfully patched {file.Path}");
            }

            return 0;
        }
        
        private static void DoVerify(PatchManifest patchManifest, string installFolder)
        {
            List<ChecksumEntry> failedEntries = new List<ChecksumEntry>();

            foreach (var file in patchManifest.Checksums)
            {
                var localFile = new FileInfo(Path.Combine(installFolder, file.Path));

                Console.Write($"[Integrity Check] Verifying {file.Path}...");

                if (!localFile.Exists)
                {
                    Console.WriteLine($"\r[Integrity Check] Verifying {file.Path}: Failed; File Missing");
                    failedEntries.Add(file);
                    continue;
                }

                if (!localFile.MatchesSignature(file.Size, file.Hash))
                {
                    Console.WriteLine($"\r[Integrity Check] Verifying {file.Path}: Failed");
                    failedEntries.Add(file);
                    continue;
                }

                Console.WriteLine($"\r[Integrity Check] Verifying {file.Path}: Success");
            }

            Console.WriteLine($"[Integrity Check] {failedEntries.Count} files failed validation!");
        }
    }
}