/*--------------------------------------------------------------------------
* ZipFileEx
* ver 0.1.0.0
*
* created and maintained by Masanori Onoue (@ugaya40) <ugaya40@hotmail.com>
* licensed under Microsoft Public License(Ms-PL)
* http://ugaya40.net/
* http://zipfileex.codeplex.com/
*--------------------------------------------------------------------------*/
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace System.IO.Compression
{
    public static class ZipFileEx
    {
        /// <summary>
        /// As an asynchronous operation, Creates a zip archive that contains the files and directories from the specified directory.
        /// </summary>
        /// <param name="sourceDirectoryName">The path to the directory to be archived, specified as a relative or absolute path. A relative path is interpreted as relative to the current working directory.</param>
        /// <param name="destinationArchiveFileName">The path of the archive to be created, specified as a relative or absolute path. A relative path is interpreted as relative to the current working directory.</param>
        /// <returns>The task object representing the asynchronous operation.</returns>
        public static async Task CreateFromDirectoryAsync(string sourceDirectoryName, string destinationArchiveFileName)
        {
            await CreateFromDirectoryAsync(sourceDirectoryName, destinationArchiveFileName, CompressionLevel.Optimal, false, null, null);
        }

        /// <summary>
        /// As an asynchronous operation, Creates a zip archive that contains the files and directories from the specified directory, uses the specified compression level, and optionally includes the base directory.
        /// </summary>
        /// <param name="sourceDirectoryName">The path to the directory to be archived, specified as a relative or absolute path. A relative path is interpreted as relative to the current working directory.</param>
        /// <param name="destinationArchiveFileName">The path of the archive to be created, specified as a relative or absolute path. A relative path is interpreted as relative to the current working directory.</param>
        /// <param name="compressionLevel">One of the enumeration values that indicates whether to emphasize speed or compression effectiveness when creating the entry.</param>
        /// <param name="includeBaseDirectory">true to include the directory name from sourceDirectoryName at the root of the archive; false to include only the contents of the directory.</param>
        /// <returns>The task object representing the asynchronous operation.</returns>
        public static async Task CreateFromDirectoryAsync(string sourceDirectoryName, string destinationArchiveFileName, CompressionLevel compressionLevel, bool includeBaseDirectory)
        {
            await CreateFromDirectoryAsync(sourceDirectoryName, destinationArchiveFileName, compressionLevel, false, null, null);
        }

        /// <summary>
        /// As an asynchronous operation, Creates a zip archive that contains the files and directories from the specified directory, uses the specified compression level and character encoding for entry names, and optionally includes the base directory.
        /// </summary>
        /// <param name="sourceDirectoryName">The path to the directory to be archived, specified as a relative or absolute path. A relative path is interpreted as relative to the current working directory.</param>
        /// <param name="destinationArchiveFileName">The path of the archive to be created, specified as a relative or absolute path. A relative path is interpreted as relative to the current working directory.</param>
        /// <param name="compressionLevel">One of the enumeration values that indicates whether to emphasize speed or compression effectiveness when creating the entry.</param>
        /// <param name="includeBaseDirectory">true to include the directory name from sourceDirectoryName at the root of the archive; false to include only the contents of the directory.</param>
        /// <param name="entryNameEncoding">The encoding to use when reading or writing entry names in this archive. Specify a value for this parameter only when an encoding is required for interoperability with zip archive tools and libraries that do not support UTF-8 encoding for entry names.</param>
        /// <returns>The task object representing the asynchronous operation.</returns>
        public static async Task CreateFromDirectoryAsync(string sourceDirectoryName, string destinationArchiveFileName, CompressionLevel compressionLevel, bool includeBaseDirectory, Encoding entryNameEncoding)
        {
            await CreateFromDirectoryAsync(sourceDirectoryName, destinationArchiveFileName, compressionLevel, includeBaseDirectory, entryNameEncoding, null);
        }

        /// <summary>
        /// As an asynchronous operation, Creates a zip archive that contains the files and directories from the specified directory, uses character encoding for entry names, and IProgress.
        /// </summary>
        /// <param name="sourceDirectoryName">The path to the directory to be archived, specified as a relative or absolute path. A relative path is interpreted as relative to the current working directory.</param>
        /// <param name="destinationArchiveFileName">The path of the archive to be created, specified as a relative or absolute path. A relative path is interpreted as relative to the current working directory.</param>
        /// <param name="progress">A provider for progress updates.</param>
        /// <returns>The task object representing the asynchronous operation.</returns>
        public static async Task CreateFromDirectoryAsync(string sourceDirectoryName, string destinationArchiveFileName, IProgress<FileSystemProgressInfo> progress)
        {
            await CreateFromDirectoryAsync(sourceDirectoryName, destinationArchiveFileName, CompressionLevel.Optimal, false, null, progress);
        }

        /// <summary>
        /// As an asynchronous operation, Creates a zip archive that contains the files and directories from the specified directory, uses the specified compression level, and optionally includes the base directory, and IProgress.
        /// </summary>
        /// <param name="sourceDirectoryName">The path to the directory to be archived, specified as a relative or absolute path. A relative path is interpreted as relative to the current working directory.</param>
        /// <param name="destinationArchiveFileName">The path of the archive to be created, specified as a relative or absolute path. A relative path is interpreted as relative to the current working directory.</param>
        /// <param name="compressionLevel">One of the enumeration values that indicates whether to emphasize speed or compression effectiveness when creating the entry.</param>
        /// <param name="includeBaseDirectory">true to include the directory name from sourceDirectoryName at the root of the archive; false to include only the contents of the directory.</param>
        /// <param name="progress">A provider for progress updates.</param>
        /// <returns>The task object representing the asynchronous operation.</returns>
        public static async Task CreateFromDirectoryAsync(string sourceDirectoryName, string destinationArchiveFileName,CompressionLevel compressionLevel, bool includeBaseDirectory, IProgress<FileSystemProgressInfo> progress)
        {
            await CreateFromDirectoryAsync(sourceDirectoryName, destinationArchiveFileName, compressionLevel, includeBaseDirectory, null, progress);
        }

        /// <summary>
        /// As an asynchronous operation, Creates a zip archive that contains the files and directories from the specified directory, uses the specified compression level and character encoding for entry names, and optionally includes the base directory, and IProgress.
        /// </summary>
        /// <param name="sourceDirectoryName">The path to the directory to be archived, specified as a relative or absolute path. A relative path is interpreted as relative to the current working directory.</param>
        /// <param name="destinationArchiveFileName">The path of the archive to be created, specified as a relative or absolute path. A relative path is interpreted as relative to the current working directory.</param>
        /// <param name="compressionLevel">One of the enumeration values that indicates whether to emphasize speed or compression effectiveness when creating the entry.</param>
        /// <param name="includeBaseDirectory">true to include the directory name from sourceDirectoryName at the root of the archive; false to include only the contents of the directory.</param>
        /// <param name="entryNameEncoding">The encoding to use when reading or writing entry names in this archive. Specify a value for this parameter only when an encoding is required for interoperability with zip archive tools and libraries that do not support UTF-8 encoding for entry names.</param>
        /// <param name="progress">A provider for progress updates.</param>
        /// <returns>The task object representing the asynchronous operation.</returns>
        public static async Task CreateFromDirectoryAsync(string sourceDirectoryName, string destinationArchiveFileName,CompressionLevel compressionLevel, bool includeBaseDirectory, Encoding entryNameEncoding, IProgress<FileSystemProgressInfo> progress)
        {
            if (!Directory.Exists(sourceDirectoryName))
            {
                throw new DirectoryNotFoundException(string.Format("{0} does not exist.", sourceDirectoryName));
            }

            var outDic = Path.GetDirectoryName(destinationArchiveFileName);

            if (string.IsNullOrEmpty(outDic))
            {
                throw new DirectoryNotFoundException(string.Format("destinationArchiveFileName directory - {0} does not exist.", outDic));
            }

            if (!Directory.Exists(outDic))
            {
                throw new DirectoryNotFoundException(string.Format("destinationArchiveFileName directory - {0} does not exist.", outDic));
            }

            await Task.Run(() =>
            {
                var inputDirectoryInfo = new DirectoryInfo(sourceDirectoryName);

                using (var zip = new ZipArchive(File.Create(destinationArchiveFileName), ZipArchiveMode.Create, false, entryNameEncoding))
                {
                    var inputs = inputDirectoryInfo.GetFileSystemInfos("*", SearchOption.AllDirectories);

                    Array.ForEach(inputs, fileSystem =>
                    {
                        string entryName;

                        if(includeBaseDirectory)
                        {
                            entryName = inputDirectoryInfo.Name + "\\" + fileSystem.FullName.Remove(0, inputDirectoryInfo.FullName.Length + 1);
                        }
                        else
                        {
                            entryName = fileSystem.FullName.Remove(0, inputDirectoryInfo.FullName.Length + 1);
                        }

                        if (fileSystem.Attributes.HasFlag(FileAttributes.Directory))
                        {
                            zip.CreateEntry(entryName + "\\", compressionLevel);
                        }
                        else
                        {
                            zip.CreateEntryFromFile(fileSystem.FullName,entryName, compressionLevel);
                        }

                        if(progress != null)
                        {
                            progress.Report(new FileSystemProgressInfo(fileSystem, inputs));
                        }
                    });
                }
            });
        }

        /// <summary>
        /// Extracts all the files in the specified zip archive to a directory on the file system.
        /// </summary>
        /// <param name="sourceArchiveFileName">The path to the archive that is to be extracted.</param>
        /// <param name="destinationDirectoryName">The path to the directory in which to place the extracted files, specified as a relative or absolute path. A relative path is interpreted as relative to the current working directory.</param>
        /// <returns>The task object representing the asynchronous operation.</returns>
        public static async Task ExtractToDirectoryAsync(string sourceArchiveFileName, string destinationDirectoryName)
        {
            await ExtractToDirectoryAsync(sourceArchiveFileName, destinationDirectoryName, null, null);
        }

        /// <summary>
        /// Extracts all the files in the specified zip archive to a directory on the file system and uses the specified character encoding for entry names.
        /// </summary>
        /// <param name="sourceArchiveFileName">The path to the archive that is to be extracted.</param>
        /// <param name="destinationDirectoryName">The path to the directory in which to place the extracted files, specified as a relative or absolute path. A relative path is interpreted as relative to the current working directory.</param>
        /// <param name="entryNameEncoding">The encoding to use when reading or writing entry names in this archive. Specify a value for this parameter only when an encoding is required for interoperability with zip archive tools and libraries that do not support UTF-8 encoding for entry names.</param>
        /// <returns>The task object representing the asynchronous operation.</returns>
        public static async Task ExtractToDirectoryAsync(string sourceArchiveFileName, string destinationDirectoryName, Encoding entryNameEncoding)
        {
            await ExtractToDirectoryAsync(sourceArchiveFileName, destinationDirectoryName, entryNameEncoding, null);
        }

        /// <summary>
        /// Extracts all the files in the specified zip archive to a directory on the file system and uses IProgress.
        /// </summary>
        /// <param name="sourceArchiveFileName">The path to the archive that is to be extracted.</param>
        /// <param name="destinationDirectoryName">The path to the directory in which to place the extracted files, specified as a relative or absolute path. A relative path is interpreted as relative to the current working directory.</param>
        /// <param name="progress">A provider for progress updates.</param>
        /// <returns>The task object representing the asynchronous operation.</returns>
        public static async Task ExtractToDirectoryAsync(string sourceArchiveFileName, string destinationDirectoryName, IProgress<ZipArchiveEntryProgressInfo> progress)
        {
            await ExtractToDirectoryAsync(sourceArchiveFileName, destinationDirectoryName, null, progress);
        }

        /// <summary>
        /// Extracts all the files in the specified zip archive to a directory on the file system and uses the specified character encoding for entry names, and IProgress.
        /// </summary>
        /// <param name="sourceArchiveFileName">The path to the archive that is to be extracted.</param>
        /// <param name="destinationDirectoryName">The path to the directory in which to place the extracted files, specified as a relative or absolute path. A relative path is interpreted as relative to the current working directory.</param>
        /// <param name="entryNameEncoding">The encoding to use when reading or writing entry names in this archive. Specify a value for this parameter only when an encoding is required for interoperability with zip archive tools and libraries that do not support UTF-8 encoding for entry names.</param>
        /// <param name="progress">A provider for progress updates.</param>
        /// <returns>The task object representing the asynchronous operation.</returns>
        public static async Task ExtractToDirectoryAsync(string sourceArchiveFileName, string destinationDirectoryName, Encoding entryNameEncoding, IProgress<ZipArchiveEntryProgressInfo> progress)
        {
            if (!File.Exists(sourceArchiveFileName))
            {
                throw new FileNotFoundException(string.Format("{0} does not exist.", sourceArchiveFileName));
            }

            if (!Directory.Exists(destinationDirectoryName))
            {
                throw new DirectoryNotFoundException(string.Format("{0} does not exist.",destinationDirectoryName));
            }

            using (var zip = new ZipArchive(File.OpenRead(sourceArchiveFileName), ZipArchiveMode.Read, false, entryNameEncoding))
            {
                await zip.ExtractToDirectoryAsync(destinationDirectoryName, progress);
            }
        }

        /// <summary>
        /// Extracts all the files in the zip archive to a directory on the file system.
        /// </summary>
        /// <param name="zip">The zip archive to extract files from.</param>
        /// <param name="destinationDirectoryName">The path to the directory to place the extracted files in. You can specify either a relative or an absolute path. A relative path is interpreted as relative to the current working directory.</param>
        /// <returns>The task object representing the asynchronous operation.</returns>
        public static async Task ExtractToDirectoryAsync(this ZipArchive zip, string destinationDirectoryName)
        {
            await zip.ExtractToDirectoryAsync(destinationDirectoryName, null);
        }

        /// <summary>
        /// Extracts all the files in the zip archive to a directory on the file system uses IProgress.
        /// </summary>
        /// <param name="zip">The zip archive to extract files from.</param>
        /// <param name="destinationDirectoryName">The path to the directory to place the extracted files in. You can specify either a relative or an absolute path. A relative path is interpreted as relative to the current working directory.</param>
        /// <param name="progress">A provider for progress updates.</param>
        /// <returns>The task object representing the asynchronous operation.</returns>
        public static async Task ExtractToDirectoryAsync(this ZipArchive zip, string destinationDirectoryName, IProgress<ZipArchiveEntryProgressInfo> progress)
        {
            await Task.Run(() =>
            {
                foreach (var entry in zip.Entries)
                {
                    var outPath = Path.Combine(destinationDirectoryName, entry.FullName);

                    if (entry.FullName.EndsWith("\\"))
                    {
                        Directory.CreateDirectory(outPath);
                    }
                    else
                    {
                        var dicName = Path.GetDirectoryName(outPath);
                        if (!Directory.Exists(dicName))
                        {
                            Directory.CreateDirectory(dicName);
                        }
                        entry.ExtractToFile(outPath, true);
                    }
                    if (progress != null)
                    {
                        progress.Report(new ZipArchiveEntryProgressInfo(entry, zip.Entries));
                    }
                }
            });
        }
    }

    public class FileSystemProgressInfo
    {
        public FileSystemProgressInfo(FileSystemInfo fileSystem, IReadOnlyList<FileSystemInfo> all)
        {
            FileSystemInfo = fileSystem;
            All = all;
        }

        public FileSystemInfo FileSystemInfo { get; private set; }
        public IReadOnlyList<FileSystemInfo> All { get; private set; }
    }

    public class ZipArchiveEntryProgressInfo
    {
        public ZipArchiveEntryProgressInfo(ZipArchiveEntry entry, IReadOnlyList<ZipArchiveEntry> all)
        {
            ZipArchiveEntry = entry;
            All = all;
        }

        public ZipArchiveEntry ZipArchiveEntry { get; private set; }
        public IReadOnlyList<ZipArchiveEntry> All { get; private set; }
    }
}
