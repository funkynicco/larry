using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Larry.File
{
    public class FileMetadata
    {
        private static readonly string RootName = "__root";

        public FileMetadata Parent { get; private set; }

        public bool IsFolder { get; private set; }

        public string Name { get; private set; }

        public long Size { get; private set; }

        public DateTime LastModifiedUtc { get; private set; }

        public FileMetadata Next { get; set; }

        public FileMetadata Children { get; set; }

        public FileMetadata(FileMetadata parent, bool isFolder, string name, long size, DateTime lastModifiedUtc)
        {
            Parent = parent;
            IsFolder = isFolder;
            Name = name;
            Size = size;
            LastModifiedUtc = lastModifiedUtc;
            Next = null;
            Children = null;
        }

        public override string ToString()
            => Name;

        private void RecursiveBuildReadableList(Dictionary<string, FileMetadata> files, string virtual_path, FileMetadata parent)
        {
            for (FileMetadata node = parent.Children; node != null; node = node.Next)
            {
                var this_path = Path.Combine(virtual_path, node.Name);

                if (node.IsFolder)
                {
                    RecursiveBuildReadableList(files, this_path, node);
                    continue;
                }

                files.Add(this_path, node);
            }
        }

        public Dictionary<string, FileMetadata> MakeReadableFileList()
        {
            var files = new Dictionary<string, FileMetadata>(StringComparer.OrdinalIgnoreCase);
            RecursiveBuildReadableList(files, "", this);
            return files;
        }

        public static FileMetadata CreateRoot()
            => new FileMetadata(null, true, RootName, 0, DateTime.UtcNow);

        private static void RecursiveGenerateFileList(FileMetadata parent, string path)
        {
            foreach (var directory in Directory.GetDirectories(path))
            {
                var item = new FileMetadata(parent, true, Path.GetFileName(directory), 0, new DateTime(0, DateTimeKind.Utc));
                item.Next = parent.Children;
                parent.Children = item;
                RecursiveGenerateFileList(item, directory);
            }

            foreach (var filename in Directory.GetFiles(path))
            {
                var fi = new FileInfo(filename);
                var item = new FileMetadata(parent, false, Path.GetFileName(filename), fi.Length, fi.LastWriteTimeUtc);
                item.Next = parent.Children;
                parent.Children = item;
            }
        }

        public static FileMetadata GenerateFileList(string directory)
        {
            if (!Directory.Exists(directory))
                throw new DirectoryNotFoundException($"Directory '{directory}' not found.");

            var root = CreateRoot();
            RecursiveGenerateFileList(root, directory);
            return root;
        }
    }
}
