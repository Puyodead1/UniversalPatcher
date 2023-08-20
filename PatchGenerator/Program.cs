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

namespace PatchGenerator
{
    internal class Program
    {
        static async Task<int> Main(string[] args)
        {
            var oldDirPath = new Argument<string?>(
                name: "old",
                description: "Path to old version");

            var newDirPath = new Argument<string?>(
                name: "new",
                description: "Path to new version");

            var outDirPath = new Argument<string?>(
                name: "out",
                description: "Path to output patch files");

            var rootCommand = new RootCommand("UniversalPatcher - Patch Generator")
            {
                oldDirPath,
                newDirPath,
                outDirPath
            };

            rootCommand.SetHandler((oldDirPathValue, newDirPathValue, outDirPathValue) =>
            {
                GeneratePatch(oldDirPathValue!, newDirPathValue!, outDirPathValue!);
            },
            oldDirPath, newDirPath, outDirPath);

            return await rootCommand.InvokeAsync(args);
        }

        static int GeneratePatch(string oldDirPath, string newDirPath, string outDirPath)
        {
            // check if old and new folders exist
            if (!Directory.Exists(oldDirPath))
            {
                Console.WriteLine($"Old directory does not exist: {oldDirPath}");
                return 1;
            }

            if (!Directory.Exists(newDirPath))
            {
                Console.WriteLine($"New directory does not exist: {oldDirPath}");
                return 1;
            }

            PatchUtils.DeleteDirectory(outDirPath);
            Directory.CreateDirectory(outDirPath);

            PatchCreator patchCreator = new PatchCreator(oldDirPath, newDirPath, outDirPath);
            return patchCreator.CreateIncrementalPatch();
        }
    }
}