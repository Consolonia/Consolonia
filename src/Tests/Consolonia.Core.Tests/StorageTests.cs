// ReSharper disable PossibleNullReferenceException
// ReSharper disable ConstantConditionalAccessQualifier

using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using Consolonia.ManagedWindows.Storage;
using NUnit.Framework;

namespace Consolonia.Core.Tests
{
    [TestFixture]
    public class StorageTests
    {
        [Test]
        public void DefaultAttributes()
        {
            var storageProvider = new ConsoloniaStorageProvider();
            Assert.True(storageProvider.CanOpen);
            Assert.True(storageProvider.CanSave);
            Assert.True(storageProvider.CanPickFolder);
        }

        [Test]
        public async Task TestFileSemantics()
        {
            var storageProvider = new ConsoloniaStorageProvider();
            string tempFile = Path.GetTempFileName();
            IStorageFile file = await storageProvider.TryGetFileFromPathAsync(new Uri($"file://{tempFile}"));
            Assert.IsNotNull(file);
            Assert.AreEqual(tempFile, file.Path.LocalPath);

            using (Stream stream = await file.OpenWriteAsync())
            {
                using (var streamWriter = new StreamWriter(stream))
                {
                    await streamWriter.WriteAsync("Hello world");
                }
            }

            Assert.True(File.Exists(tempFile));
            Assert.True(File.Exists(file.Path.LocalPath));
            StorageItemProperties props = await file.GetBasicPropertiesAsync();

            void CheckTimingMatches()
            {
                Assert.AreEqual(File.GetCreationTime(tempFile).ToString("G", CultureInfo.InvariantCulture),
                    props.DateCreated?.DateTime.ToString("G", CultureInfo.InvariantCulture));
                Assert.AreEqual(File.GetLastWriteTime(tempFile).ToString("G", CultureInfo.InvariantCulture),
                    props.DateModified?.DateTime.ToString("G", CultureInfo.InvariantCulture));
                Assert.AreEqual(new FileInfo(tempFile).Length, (long)(props.Size ?? 0));
            }

            CheckTimingMatches();

            using (Stream stream = await file.OpenReadAsync())
            {
                using (var streamReader = new StreamReader(stream))
                {
                    string text = await streamReader.ReadToEndAsync();
                    Assert.AreEqual("Hello world", text);
                }
            }

            props = await file.GetBasicPropertiesAsync();

            CheckTimingMatches();

            IStorageFolder parentFolder = await file.GetParentAsync();
            Assert.IsNotNull(parentFolder);
            Assert.AreEqual(Path.GetDirectoryName(tempFile), parentFolder.Path.LocalPath);

            string subPath = Path.Combine(Path.GetDirectoryName(tempFile)!, nameof(TestFileSemantics));
            if (Directory.Exists(subPath))
                Directory.Delete(subPath);

            IStorageFolder subFolder = await storageProvider.TryGetFolderFromPathAsync(new Uri($"file://{subPath}"));
            Assert.IsNull(subFolder);

            subFolder = await parentFolder.CreateFolderAsync(nameof(TestFileSemantics));
            Assert.IsNotNull(subFolder);
            Assert.IsTrue(new DirectoryInfo(subFolder.Path.LocalPath).Exists);

            IStorageItem newFile = await file.MoveAsync(subFolder);
            Assert.True(File.Exists(newFile.Path?.LocalPath));
            Assert.False(File.Exists(tempFile));
            Assert.AreEqual(
                new Uri(
                    $"file://{parentFolder.Path.LocalPath}/{nameof(TestFileSemantics)}/{Path.GetFileName(tempFile)}"),
                newFile.Path);
            await newFile.DeleteAsync();
            Assert.False(File.Exists(newFile.Path.LocalPath));

            await subFolder.DeleteAsync();
            Assert.IsFalse(new DirectoryInfo(subFolder.Path.LocalPath).Exists);
        }

        [Test]
        public async Task TestWellKnownFolder()
        {
            var storageProvider = new ConsoloniaStorageProvider();
            IStorageFolder folder = await storageProvider.TryGetWellKnownFolderAsync(WellKnownFolder.Pictures);
            if (folder != null)
                Assert.AreEqual(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), folder.Path.LocalPath);

            folder = await storageProvider.TryGetWellKnownFolderAsync(WellKnownFolder.Documents);
            if (folder != null)
                Assert.AreEqual(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    folder.Path.LocalPath);

            folder = await storageProvider.TryGetWellKnownFolderAsync(WellKnownFolder.Music);
            if (folder != null)
                Assert.AreEqual(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), folder.Path.LocalPath);

            folder = await storageProvider.TryGetWellKnownFolderAsync(WellKnownFolder.Videos);
            if (folder != null)
                Assert.AreEqual(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), folder.Path.LocalPath);

            folder = await storageProvider.TryGetWellKnownFolderAsync(WellKnownFolder.Downloads);
            if (folder != null)
                Assert.AreEqual(
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
                    folder.Path.LocalPath);

            folder = await storageProvider.TryGetWellKnownFolderAsync(WellKnownFolder.Desktop);
            if (folder != null)
                Assert.AreEqual(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), folder.Path.LocalPath);
        }


        [Test]
        public async Task TestFolderSemantics()
        {
            var storageProvider = new ConsoloniaStorageProvider();
            string tempPath = Path.GetTempPath();
            IStorageFolder tempFolder = await storageProvider.TryGetFolderFromPathAsync(new Uri($"file://{tempPath}"));
            string testPath = Path.Combine(tempPath, nameof(TestFolderSemantics))!;
            IStorageFolder testFolder = await tempFolder.CreateFolderAsync(nameof(TestFolderSemantics));

            Assert.IsNotNull(testFolder);
            Assert.AreEqual(testPath, testFolder.Path.LocalPath);
            Assert.IsFalse(testFolder.CanBookmark);
            Assert.AreEqual(nameof(TestFolderSemantics), testFolder.Name);
            IStorageFile file = await testFolder.CreateFileAsync($"{nameof(TestFolderSemantics)}.txt");
            Assert.IsTrue(File.Exists(file.Path?.LocalPath));

            StorageItemProperties props = await testFolder.GetBasicPropertiesAsync();
            Assert.AreEqual(Directory.GetCreationTime(testPath).ToString("G", CultureInfo.InvariantCulture),
                props.DateCreated?.DateTime.ToString("G", CultureInfo.InvariantCulture));
            Assert.AreEqual(Directory.GetLastWriteTime(testPath).ToString("G", CultureInfo.InvariantCulture),
                props.DateModified?.DateTime.ToString("G", CultureInfo.InvariantCulture));

            await file.DeleteAsync();
            Assert.IsFalse(File.Exists(file.Path?.LocalPath));

            IStorageFolder subFolder = await testFolder.CreateFolderAsync("sub");
            Assert.IsTrue(Directory.Exists(subFolder.Path?.LocalPath));

            file = await subFolder.CreateFileAsync($"{nameof(TestFolderSemantics)}.txt");
            Assert.IsTrue(File.Exists(file.Path?.LocalPath));

            await foreach (IStorageItem item in subFolder.GetItemsAsync())
                Assert.AreEqual(file.Path.LocalPath, item.Path.LocalPath);
            await file.DeleteAsync();

            await subFolder.DeleteAsync();
            Assert.IsFalse(Directory.Exists(subFolder.Path.LocalPath));

            await testFolder.DeleteAsync();
            Assert.IsFalse(Directory.Exists(testPath));
        }
    }
}