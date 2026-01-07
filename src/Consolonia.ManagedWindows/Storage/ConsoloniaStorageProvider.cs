using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Platform.Storage;
using Consolonia.Core.Controls;

namespace Consolonia.Core.Infrastructure
{
    public class ConsoloniaStorageProvider : IStorageProvider
    {
        public bool CanOpen => true;

        public bool CanSave => true;

        public bool CanPickFolder => true;


        public Task<IStorageBookmarkFile> OpenFileBookmarkAsync(string bookmark)
        {
            throw new NotImplementedException();
        }

        public async Task<IReadOnlyList<IStorageFile>> OpenFilePickerAsync(FilePickerOpenOptions options)
        {
            var picker = new FileOpenPicker(options);
            var results = await picker.ShowDialog<IReadOnlyList<IStorageFile>>();
            return results;
        }

        public Task<IStorageBookmarkFolder> OpenFolderBookmarkAsync(string bookmark)
        {
            throw new NotImplementedException();
        }

        public async Task<IReadOnlyList<IStorageFolder>> OpenFolderPickerAsync(FolderPickerOpenOptions options)
        {
            var picker = new FolderPicker(options);
            var results = await picker.ShowDialog<IReadOnlyList<IStorageFolder>>();
            return results;
        }

        public async Task<IStorageFile> SaveFilePickerAsync(FilePickerSaveOptions options)
        {
            var lifetime = (ConsoloniaLifetime)Application.Current.ApplicationLifetime;
            ArgumentNullException.ThrowIfNull(lifetime);

            var picker = new FileSavePicker(options);
            var results = await picker.ShowDialog<IStorageFile>();
            return results;
        }

        public async Task<SaveFilePickerResult> SaveFilePickerWithResultAsync(FilePickerSaveOptions options)
        {
            IStorageFile file = await SaveFilePickerAsync(options);
            return new SaveFilePickerResult
            {
                File = file,
                SelectedFileType = null
            };
        }

        public Task<IStorageFile> TryGetFileFromPathAsync(Uri filePath)
        {
            if (File.Exists(filePath.LocalPath))
                return Task.FromResult<IStorageFile>(new SystemStorageFile(filePath));
            return Task.FromResult<IStorageFile>(null);
        }

        public Task<IStorageFolder> TryGetFolderFromPathAsync(Uri folderPath)
        {
            if (Directory.Exists(folderPath.LocalPath))
                return Task.FromResult<IStorageFolder>(new SystemStorageFolder(folderPath));
            return Task.FromResult<IStorageFolder>(null);
        }

        public Task<IStorageFolder> TryGetWellKnownFolderAsync(WellKnownFolder wellKnownFolder)
        {
            string dir = wellKnownFolder switch
            {
                WellKnownFolder.Desktop => Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                WellKnownFolder.Documents => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                WellKnownFolder.Downloads => Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
                WellKnownFolder.Pictures => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                WellKnownFolder.Music => Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
                WellKnownFolder.Videos => Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
                _ => null
            };
            if (string.IsNullOrEmpty(dir))
                return Task.FromResult<IStorageFolder>(null);
            return Task.FromResult<IStorageFolder>(new SystemStorageFolder(dir));
        }
    }
}