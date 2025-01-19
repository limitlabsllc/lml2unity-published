using System;
using System.IO;
using System.IO.Compression;

namespace LML
{
    public static class TargzExtractor
    {
        public static void ExtractTarGz(string tarGzPath, string outputDir)
        {
            if (string.IsNullOrEmpty(tarGzPath) || string.IsNullOrEmpty(outputDir))
            {
                throw new ArgumentException("Input or output path is invalid.");
            }

            if (!File.Exists(tarGzPath))
            {
                throw new FileNotFoundException("The specified .tar.gz file does not exist.", tarGzPath);
            }

            Directory.CreateDirectory(outputDir);

            using (FileStream fs = new FileStream(tarGzPath, FileMode.Open, FileAccess.Read))
            using (GZipStream gzipStream = new GZipStream(fs, CompressionMode.Decompress))
            using (MemoryStream tarStream = new MemoryStream())
            {
                gzipStream.CopyTo(tarStream);
                tarStream.Seek(0, SeekOrigin.Begin);

                ExtractTar(tarStream, outputDir);
            }
        }

        private static void ExtractTar(Stream tarStream, string outputDir)
        {
            using (BinaryReader reader = new BinaryReader(tarStream))
            {
                while (tarStream.Position < tarStream.Length)
                {
                    byte[] header = reader.ReadBytes(512);
                    if (header.Length < 512) break;

                    string name = ReadString(header, 0, 100).Trim();
                    if (string.IsNullOrEmpty(name)) break;

                    long size = Convert.ToInt64(ReadString(header, 124, 12).Trim(), 8);

                    string fullPath = Path.Combine(outputDir, name);
                    Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? string.Empty);

                    if (size > 0)
                    {
                        using (FileStream outputFile = new FileStream(fullPath, FileMode.Create, FileAccess.Write))
                        {
                            byte[] fileContent = reader.ReadBytes((int)size);
                            outputFile.Write(fileContent, 0, fileContent.Length);
                        }

                        long padding = (512 - (size % 512)) % 512;
                        reader.BaseStream.Seek(padding, SeekOrigin.Current);
                    }
                }
            }
        }

        private static string ReadString(byte[] bytes, int offset, int length)
        {
            return System.Text.Encoding.ASCII.GetString(bytes, offset, length).Trim('\0');
        }
    }
}
