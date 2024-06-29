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

using Octodiff.Core;
using Octodiff.Diagnostics;

namespace PatchGenerator
{
    public static class OctoUtils
    {
        public static void CalculateDelta(string sourcePath, string targetPath, string deltaPath, int quality = 3)
        {
            // Try different chunk sizes to find the smallest diff file
            if (quality < 1)
                quality = 1;

            int[] chunkSizes = new int[quality * 2 - 1];
            chunkSizes[0] = SignatureBuilder.DefaultChunkSize;

            int validChunkSizes = 1;
            int currentChunkSize = chunkSizes[0];
            for (int i = 1; i < quality; i++)
            {
                currentChunkSize /= 2;
                if (currentChunkSize < SignatureBuilder.MinimumChunkSize)
                    break;

                chunkSizes[validChunkSizes++] = currentChunkSize;
            }

            currentChunkSize = chunkSizes[0];
            for (int i = 1; i < quality; i++)
            {
                currentChunkSize *= 2;
                if (currentChunkSize > SignatureBuilder.MaximumChunkSize)
                    break;

                chunkSizes[validChunkSizes++] = currentChunkSize;
            }

            string deltaPathTemp = deltaPath + ".detmp";
            string signaturePathTemp = deltaPath + ".sgtmp";
            long deltaSize = 0L;
            for (int i = 0; i < validChunkSizes; i++)
            {
                if (i == 0)
                {
                    CalculateDeltaInternal(sourcePath, targetPath, deltaPath, signaturePathTemp, chunkSizes[i]);
                    deltaSize = new FileInfo(deltaPath).Length;
                }
                else
                {
                    CalculateDeltaInternal(sourcePath, targetPath, deltaPathTemp, signaturePathTemp, chunkSizes[i]);

                    long newDeltaSize = new FileInfo(deltaPathTemp).Length;
                    if (newDeltaSize < deltaSize)
                    {
                        PatchUtils.MoveFile(deltaPathTemp, deltaPath);
                        deltaSize = newDeltaSize;
                    }
                }
            }

            File.Delete(deltaPathTemp);
            File.Delete(signaturePathTemp);
        }

        private static void CalculateDeltaInternal(string sourcePath, string targetPath, string deltaPath, string signaturePath, int chunkSize)
        {
            using (var signatureStream = new FileStream(signaturePath, FileMode.Create, FileAccess.ReadWrite, FileShare.Read))
            {
                using (var basisStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    SignatureBuilder sb = new SignatureBuilder { ChunkSize = (short)chunkSize };
                    sb.Build(basisStream, new SignatureWriter(signatureStream));
                }

                signatureStream.Position = 0L;

                using (var newFileStream = new FileStream(targetPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var deltaStream = new FileStream(deltaPath, FileMode.Create, FileAccess.Write, FileShare.Read))
                {
                    IProgressReporter reporter = new ConsoleProgressReporter();
                    //IProgressReporter reporter = new NullProgressReporter();
                    var builder = new DeltaBuilder();
                    builder.ProgressReporter = reporter;
                    builder.BuildDelta(newFileStream, new SignatureReader(signatureStream, reporter), new AggregateCopyOperationsDecorator(new BinaryDeltaWriter(deltaStream)));
                }
            }
        }

        public static void ApplyDelta(string sourcePath, string targetPath, string deltaPath)
        {
            using (var basisStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var deltaStream = new FileStream(deltaPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var newFileStream = new FileStream(targetPath, FileMode.Create, FileAccess.ReadWrite, FileShare.Read))
            {
                //IProgressReporter reporter = new ConsoleProgressReporter();
                IProgressReporter reporter = new NullProgressReporter();
                new DeltaApplier().Apply(basisStream, new BinaryDeltaReader(deltaStream, reporter), newFileStream);
            }
        }
    }

    public class ConsoleProgressReporter : IProgressReporter
    {
        private string currentOperation;
        private int progressPercentage;

        public void ReportProgress(string operation, long currentPosition, long total)
        {
            var percent = (int)((double)currentPosition / total * 100d + 0.5);
            if (currentOperation != operation)
            {
                progressPercentage = -1;
                currentOperation = operation;
            }

            if (progressPercentage != percent)
            {
                progressPercentage = percent;
                Console.Write($"\r{currentOperation}: {percent}%");
            } 
            
            if(currentPosition == total)
            {
                Console.WriteLine();
            }
        }
    }
}
