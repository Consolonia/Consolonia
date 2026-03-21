using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;

namespace Consolonia.ManagedWindows.Controls
{
    [DebuggerDisplay("Folder: {Name}")]
    internal sealed class SystemStorageFolder(DirectoryInfo directoryInfo, bool isParent = false) : IStorageFolder
    {
        public SystemStorageFolder(string path) : this(new DirectoryInfo(path))
        {
        }

        public SystemStorageFolder(Uri uri) : this(new DirectoryInfo(uri.LocalPath))
        {
        }

        public string Name => isParent ? ".." : directoryInfo.Name;

        public Uri Path => new($"file://{directoryInfo.FullName}");

        public bool CanBookmark => false;

        public async Task<IStorageFile> CreateFileAsync(string name)
        {
            await Task.CompletedTask;

            string path = System.IO.Path.Combine(directoryInfo.FullName, name);
            await using (FileStream stream = File.Create(path))
            {
                await stream.WriteAsync(Array.Empty<byte>().AsMemory(0, 0));
            }

            return new SystemStorageFile(path);
        }

        public Task<IStorageFolder> CreateFolderAsync(string name)
        {
            DirectoryInfo dirInfo = Directory.CreateDirectory(System.IO.Path.Combine(directoryInfo.FullName, name));
            return Task.FromResult((IStorageFolder)new SystemStorageFolder(dirInfo));
        }

        public Task DeleteAsync()
        {
            return Task.Run(directoryInfo.Delete);
        }

        public void Dispose()
        {
        }

        public Task<StorageItemProperties> GetBasicPropertiesAsync()
        {
            var properties = new StorageItemProperties(dateCreated: directoryInfo.CreationTime,
                dateModified: directoryInfo.LastAccessTime);
            return Task.FromResult(properties);
        }

        public Task<IStorageFile> GetFileAsync(string name)
        {
            string path = System.IO.Path.Combine(directoryInfo.FullName, name);
            return Task.FromResult<IStorageFile>(new SystemStorageFile(path));
        }

        public Task<IStorageFolder> GetFolderAsync(string name)
        {
            string path = System.IO.Path.Combine(directoryInfo.FullName, name);
            return Task.FromResult<IStorageFolder>(new SystemStorageFolder(path));
        }

        public async IAsyncEnumerable<IStorageItem> GetItemsAsync()
        {
            await Task.CompletedTask;

            if (directoryInfo.Exists)
            {
                foreach (DirectoryInfo folder in directoryInfo.GetDirectories())
                    yield return new SystemStorageFolder(folder);

                foreach (FileInfo file in directoryInfo.GetFiles()) yield return new SystemStorageFile(file);
            }
        }

        public Task<IStorageFolder> GetParentAsync()
        {
            if (directoryInfo.Parent == null)
                return Task.FromResult((IStorageFolder)null);

            return Task.FromResult((IStorageFolder)new SystemStorageFolder(directoryInfo.Parent));
        }

        public Task<IStorageItem> MoveAsync(IStorageFolder destination)
        {
            string targetPath = System.IO.Path.Combine(destination.Path.LocalPath, directoryInfo.Name);
            directoryInfo.MoveTo(targetPath);
            return Task.FromResult((IStorageItem)new SystemStorageFolder(targetPath));
        }

        public Task<string> SaveBookmarkAsync()
        {
            throw new NotImplementedException();
        }
    }
}