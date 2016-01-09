// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNet.FileProviders.Physical;
using Microsoft.AspNet.Testing.xunit;
using Microsoft.Extensions.FileSystemGlobbing.Tests.TestUtility;
using Xunit;

namespace Microsoft.AspNet.FileProviders
{
    public class PhysicalFileProviderTests
    {
        private const int WaitTimeForTokenToFire = 2 * 100;

        [Fact]
        public void GetFileInfoReturnsNotFoundFileInfoForNullPath()
        {
            using (var provider = new PhysicalFileProvider(Path.GetTempPath()))
            {
                var info = provider.GetFileInfo(null);
                Assert.IsType(typeof(NotFoundFileInfo), info);
            }
        }

        [Fact]
        public void GetFileInfoReturnsNotFoundFileInfoForEmptyPath()
        {
            using (var provider = new PhysicalFileProvider(Path.GetTempPath()))
            {
                var info = provider.GetFileInfo(string.Empty);
                Assert.IsType(typeof(NotFoundFileInfo), info);
            }
        }

        [ConditionalFact]
        [OSSkipCondition(OperatingSystems.Linux, SkipReason = "Paths starting with / are considered relative.")]
        [OSSkipCondition(OperatingSystems.MacOSX, SkipReason = "Paths starting with / are considered relative.")]
        public void GetFileInfoReturnsNotFoundFileInfoForAbsolutePath()
        {
            using (var provider = new PhysicalFileProvider(Path.GetTempPath()))
            {
                var info = provider.GetFileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));
                Assert.IsType(typeof(NotFoundFileInfo), info);
            }
        }

        [Fact]
        public void GetFileInfoReturnsNotFoundFileInfoForRelativePathAboveRootPath()
        {
            using (var provider = new PhysicalFileProvider(Path.GetTempPath()))
            {
                var info = provider.GetFileInfo(Path.Combine("..", Guid.NewGuid().ToString()));
                Assert.IsType(typeof(NotFoundFileInfo), info);
            }
        }

        [ConditionalFact]
        [OSSkipCondition(OperatingSystems.Linux, SkipReason = "Hidden and system files only make sense on Windows.")]
        [OSSkipCondition(OperatingSystems.MacOSX, SkipReason = "Hidden and system files only make sense on Windows.")]
        public void GetFileInfoReturnsNotFoundFileInfoForHiddenFile()
        {
            using (var root = new DisposableFileSystem())
            {
                using (var provider = new PhysicalFileProvider(root.RootPath))
                {
                    var fileName = Guid.NewGuid().ToString();
                    var filePath = Path.Combine(root.RootPath, fileName);
                    File.Create(filePath);
                    var fileInfo = new FileInfo(filePath);
                    File.SetAttributes(filePath, fileInfo.Attributes | FileAttributes.Hidden);

                    var info = provider.GetFileInfo(fileName);

                    Assert.IsType(typeof(NotFoundFileInfo), info);
                }
            }
        }

        [Fact]
        public void GetFileInfoReturnsNotFoundFileInfoForFileNameStartingWithPeriod()
        {
            using (var root = new DisposableFileSystem())
            {
                using (var provider = new PhysicalFileProvider(root.RootPath))
                {
                    var fileName = "." + Guid.NewGuid().ToString();
                    var filePath = Path.Combine(root.RootPath, fileName);

                    var info = provider.GetFileInfo(fileName);

                    Assert.IsType(typeof(NotFoundFileInfo), info);
                }
            }
        }

        [Fact]
        public void TokenIsSameForSamePath()
        {
            using (var root = new DisposableFileSystem())
            {
                var fileName = Guid.NewGuid().ToString();
                var fileLocation = Path.Combine(root.RootPath, fileName);

                using (var provider = new PhysicalFileProvider(root.RootPath))
                {
                    var fileInfo = provider.GetFileInfo(fileName);

                    var token1 = provider.Watch(fileName);
                    var token2 = provider.Watch(fileName);

                    Assert.NotNull(token1);
                    Assert.NotNull(token2);
                    Assert.Equal(token2, token1);
                }
            }
        }

        [Fact]
        public async Task TokensFiredOnFileChange()
        {
            using (var root = new DisposableFileSystem())
            {
                var fileName = Guid.NewGuid().ToString();
                var fileLocation = Path.Combine(root.RootPath, fileName);

                using (var fileSystemWatcher = new MockFileSystemWatcher(root.RootPath))
                {
                    using (var physicalFilesWatcher = new PhysicalFilesWatcher(root.RootPath + Path.DirectorySeparatorChar, fileSystemWatcher))
                    {
                        using (var provider = new PhysicalFileProvider(root.RootPath, physicalFilesWatcher))
                        {
                            var token = provider.Watch(fileName);
                            Assert.NotNull(token);
                            Assert.False(token.HasChanged);
                            Assert.True(token.ActiveChangeCallbacks);

                            fileSystemWatcher.CallOnChanged(new FileSystemEventArgs(WatcherChangeTypes.Changed, root.RootPath, fileName));
                            await Task.Delay(WaitTimeForTokenToFire);

                            Assert.True(token.HasChanged);
                        }
                    }
                }
            }
        }

        [Fact]
        public async Task TokenCallbackInvokedOnFileChange()
        {
            using (var root = new DisposableFileSystem())
            {
                var fileName = Guid.NewGuid().ToString();
                var fileLocation = Path.Combine(root.RootPath, fileName);

                using (var fileSystemWatcher = new MockFileSystemWatcher(root.RootPath))
                {
                    using (var physicalFilesWatcher = new PhysicalFilesWatcher(root.RootPath + Path.DirectorySeparatorChar, fileSystemWatcher))
                    {
                        using (var provider = new PhysicalFileProvider(root.RootPath, physicalFilesWatcher))
                        {
                            var token = provider.Watch(fileName);
                            Assert.NotNull(token);
                            Assert.False(token.HasChanged);
                            Assert.True(token.ActiveChangeCallbacks);

                            bool callbackInvoked = false;
                            token.RegisterChangeCallback(state =>
                            {
                                callbackInvoked = true;
                            }, state: null);

                            fileSystemWatcher.CallOnChanged(new FileSystemEventArgs(WatcherChangeTypes.Changed, root.RootPath, fileName));
                            await Task.Delay(WaitTimeForTokenToFire);

                            Assert.True(callbackInvoked);
                        }
                    }
                }
            }
        }

        [Fact]
        public async Task TokensFiredOnFileDeleted()
        {
            using (var root = new DisposableFileSystem())
            {
                var fileName = Guid.NewGuid().ToString();
                var fileLocation = Path.Combine(root.RootPath, fileName);

                using (var fileSystemWatcher = new MockFileSystemWatcher(root.RootPath))
                {
                    using (var physicalFilesWatcher = new PhysicalFilesWatcher(root.RootPath + Path.DirectorySeparatorChar, fileSystemWatcher))
                    {
                        using (var provider = new PhysicalFileProvider(root.RootPath, physicalFilesWatcher))
                        {
                            var token = provider.Watch(fileName);
                            Assert.NotNull(token);
                            Assert.False(token.HasChanged);
                            Assert.True(token.ActiveChangeCallbacks);

                            fileSystemWatcher.CallOnDeleted(new FileSystemEventArgs(WatcherChangeTypes.Deleted, root.RootPath, fileName));
                            await Task.Delay(WaitTimeForTokenToFire);

                            Assert.True(token.HasChanged);
                        }
                    }
                }
            }
        }

        // On Unix the minimum invalid file path characters are / and \0
        [ConditionalTheory]
        [OSSkipCondition(OperatingSystems.Linux)]
        [OSSkipCondition(OperatingSystems.MacOSX)]
        [InlineData("/test:test")]
        [InlineData("/dir/name\"")]
        [InlineData("/dir>/name")]
        public void InvalidPath_DoesNotThrowWindows_GetFileInfo(string path)
        {
            InvalidPath_DoesNotThrowGeneric_GetFileInfo(path);
        }

        [ConditionalTheory]
        [OSSkipCondition(OperatingSystems.Windows)]
        [InlineData("/test:test\0")]
        [InlineData("/dir/\0name\"")]
        [InlineData("/dir>/name\0")]
        public void InvalidPath_DoesNotThrowUnix_GetFileInfo(string path)
        {
            InvalidPath_DoesNotThrowGeneric_GetFileInfo(path);
        }

        public void InvalidPath_DoesNotThrowGeneric_GetFileInfo(string path)
        {
            using (var provider = new PhysicalFileProvider(Directory.GetCurrentDirectory()))
            {
                var info = provider.GetFileInfo(path);
                Assert.NotNull(info);
                Assert.IsType<NotFoundFileInfo>(info);
            }
        }

        [ConditionalTheory]
        [OSSkipCondition(OperatingSystems.Linux)]
        [OSSkipCondition(OperatingSystems.MacOSX)]
        [InlineData("/test:test")]
        [InlineData("/dir/name\"")]
        [InlineData("/dir>/name")]
        public void InvalidPath_DoesNotThrowWindows_GetDirectoryContents(string path)
        {
            InvalidPath_DoesNotThrowGeneric_GetDirectoryContents(path);
        }

        [ConditionalTheory]
        [OSSkipCondition(OperatingSystems.Windows)]
        [InlineData("/test:test\0")]
        [InlineData("/dir/\0name\"")]
        [InlineData("/dir>/name\0")]
        public void InvalidPath_DoesNotThrowUnix_GetDirectoryContents(string path)
        {
            InvalidPath_DoesNotThrowGeneric_GetDirectoryContents(path);
        }

        public void InvalidPath_DoesNotThrowGeneric_GetDirectoryContents(string path)
        {
            using (var provider = new PhysicalFileProvider(Directory.GetCurrentDirectory()))
            {
                var info = provider.GetDirectoryContents(path);
                Assert.NotNull(info);
                Assert.IsType<NotFoundDirectoryContents>(info);
            }
        }

        [Fact]
        public void GetDirectoryContentsReturnsNotFoundDirectoryContentsForNullPath()
        {
            using (var provider = new PhysicalFileProvider(Path.GetTempPath()))
            {
                var contents = provider.GetDirectoryContents(null);
                Assert.IsType(typeof(NotFoundDirectoryContents), contents);
            }
        }

        [Fact]
        public void GetDirectoryContentsReturnsNotFoundDirectoryContentsForAbsolutePath()
        {
            using (var provider = new PhysicalFileProvider(Path.GetTempPath()))
            {
                var contents = provider.GetDirectoryContents(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));
                Assert.IsType(typeof(NotFoundDirectoryContents), contents);
            }
        }

        [Fact]
        public void GetDirectoryContentsReturnsNotFoundDirectoryContentsForNonExistingDirectory()
        {
            using (var provider = new PhysicalFileProvider(Path.GetTempPath()))
            {
                var contents = provider.GetDirectoryContents(Guid.NewGuid().ToString());
                Assert.IsType(typeof(NotFoundDirectoryContents), contents);
            }
        }

        [Fact]
        public void GetDirectoryContentsReturnsRootDirectoryContentsForEmptyPath()
        {
            using (var root = new DisposableFileSystem())
            {
                File.Create(Path.Combine(root.RootPath, "File" + Guid.NewGuid().ToString()));
                Directory.CreateDirectory(Path.Combine(root.RootPath, "Dir" + Guid.NewGuid().ToString()));

                using (var provider = new PhysicalFileProvider(root.RootPath))
                {
                    var contents = provider.GetDirectoryContents(string.Empty);
                    Assert.Collection(contents.OrderBy(c => c.Name),
                        item => Assert.IsType<PhysicalDirectoryInfo>(item),
                        item => Assert.IsType<PhysicalFileInfo>(item));
                }
            }
        }

        [ConditionalFact]
        [OSSkipCondition(OperatingSystems.Linux, SkipReason = "Hidden and system files only make sense on Windows.")]
        [OSSkipCondition(OperatingSystems.MacOSX, SkipReason = "Hidden and system files only make sense on Windows.")]
        public void GetDirectoryContentsDoesNotReturnFileInfoForHiddenFile()
        {
            using (var root = new DisposableFileSystem())
            {
                var directoryName = Guid.NewGuid().ToString();
                var directoryPath = Path.Combine(root.RootPath, directoryName);
                Directory.CreateDirectory(directoryPath);

                var fileName = Guid.NewGuid().ToString();
                var filePath = Path.Combine(directoryPath, fileName);
                File.Create(filePath);
                var fileInfo = new FileInfo(filePath);
                File.SetAttributes(filePath, fileInfo.Attributes | FileAttributes.Hidden);

                using (var provider = new PhysicalFileProvider(root.RootPath))
                {
                    var contents = provider.GetDirectoryContents(directoryName);
                    Assert.Empty(contents);
                }
            }
        }

        [ConditionalFact]
        [OSSkipCondition(OperatingSystems.Linux, SkipReason = "Hidden and system files only make sense on Windows.")]
        [OSSkipCondition(OperatingSystems.MacOSX, SkipReason = "Hidden and system files only make sense on Windows.")]
        public void GetDirectoryContentsDoesNotReturnFileInfoForSystemFile()
        {
            using (var root = new DisposableFileSystem())
            {
                var directoryName = Guid.NewGuid().ToString();
                var directoryPath = Path.Combine(root.RootPath, directoryName);
                Directory.CreateDirectory(directoryPath);

                var fileName = Guid.NewGuid().ToString();
                var filePath = Path.Combine(directoryPath, fileName);
                File.Create(filePath);
                var fileInfo = new FileInfo(filePath);
                File.SetAttributes(filePath, fileInfo.Attributes | FileAttributes.System);

                using (var provider = new PhysicalFileProvider(root.RootPath))
                {
                    var contents = provider.GetDirectoryContents(directoryName);
                    Assert.Empty(contents);
                }
            }
        }

        [Fact]
        public void GetDirectoryContentsDoesNotReturnFileInfoForFileNameStartingWithPeriod()
        {
            using (var root = new DisposableFileSystem())
            {
                var directoryName = Guid.NewGuid().ToString();
                var directoryPath = Path.Combine(root.RootPath, directoryName);
                Directory.CreateDirectory(directoryPath);

                var fileName = "." + Guid.NewGuid().ToString();
                var filePath = Path.Combine(directoryPath, fileName);
                File.Create(filePath);

                using (var provider = new PhysicalFileProvider(root.RootPath))
                {
                    var contents = provider.GetDirectoryContents(directoryName);
                    Assert.Empty(contents);
                }
            }
        }

        [Fact]
        public async Task FileChangeTokenNotNotifiedAfterExpiry()
        {
            using (var root = new DisposableFileSystem())
            {
                using (var fileSystemWatcher = new MockFileSystemWatcher(root.RootPath))
                {
                    using (var physicalFilesWatcher = new PhysicalFilesWatcher(root.RootPath + Path.DirectorySeparatorChar, fileSystemWatcher))
                    {
                        using (var provider = new PhysicalFileProvider(root.RootPath, physicalFilesWatcher))
                        {
                            var fileName = Guid.NewGuid().ToString();
                            var changeToken = provider.Watch(fileName);
                            var invocationCount = 0;
                            changeToken.RegisterChangeCallback(_ => { invocationCount++; }, null);

                            // Callback expected.
                            fileSystemWatcher.CallOnChanged(new FileSystemEventArgs(WatcherChangeTypes.Changed, root.RootPath, fileName));
                            await Task.Delay(WaitTimeForTokenToFire);

                            // Callback not expected.
                            fileSystemWatcher.CallOnChanged(new FileSystemEventArgs(WatcherChangeTypes.Changed, root.RootPath, fileName));
                            await Task.Delay(WaitTimeForTokenToFire);

                            Assert.Equal(1, invocationCount);
                        }
                    }
                }
            }
        }

        [Fact]
        public void TokenIsSameForSamePathCaseInsensitive()
        {
            using (var provider = new PhysicalFileProvider(Path.GetTempPath()))
            {
                var fileName = Guid.NewGuid().ToString();
                var token = provider.Watch(fileName);
                var lowerCaseToken = provider.Watch(fileName.ToLowerInvariant());
                Assert.Equal(token, lowerCaseToken);
            }
        }

        [Fact]
        public async Task CorrectTokensFiredForMultipleFiles()
        {
            using (var root = new DisposableFileSystem())
            {
                using (var fileSystemWatcher = new MockFileSystemWatcher(root.RootPath))
                {
                    using (var physicalFilesWatcher = new PhysicalFilesWatcher(root.RootPath + Path.DirectorySeparatorChar, fileSystemWatcher))
                    {
                        using (var provider = new PhysicalFileProvider(root.RootPath, physicalFilesWatcher))
                        {
                            var fileName1 = Guid.NewGuid().ToString();
                            var token1 = provider.Watch(fileName1);
                            var fileName2 = Guid.NewGuid().ToString();
                            var token2 = provider.Watch(fileName2);

                            fileSystemWatcher.CallOnChanged(new FileSystemEventArgs(WatcherChangeTypes.Changed, root.RootPath, fileName1));
                            await Task.Delay(WaitTimeForTokenToFire);

                            Assert.True(token1.HasChanged);
                            Assert.False(token2.HasChanged);

                            fileSystemWatcher.CallOnChanged(new FileSystemEventArgs(WatcherChangeTypes.Changed, root.RootPath, fileName2));
                            await Task.Delay(WaitTimeForTokenToFire);

                            Assert.True(token2.HasChanged);
                        }
                    }
                }
            }
        }

        [Fact]
        public async Task TokenNotAffectedByExceptions()
        {
            using (var root = new DisposableFileSystem())
            {
                using (var fileSystemWatcher = new MockFileSystemWatcher(root.RootPath))
                {
                    using (var physicalFilesWatcher = new PhysicalFilesWatcher(root.RootPath + Path.DirectorySeparatorChar, fileSystemWatcher))
                    {
                        using (var provider = new PhysicalFileProvider(root.RootPath, physicalFilesWatcher))
                        {
                            var fileName = Guid.NewGuid().ToString();
                            var token = provider.Watch(fileName);

                            token.RegisterChangeCallback(_ =>
                            {
                                throw new Exception();
                            }, null);

                            fileSystemWatcher.CallOnChanged(new FileSystemEventArgs(WatcherChangeTypes.Changed, root.RootPath, fileName));
                            await Task.Delay(WaitTimeForTokenToFire);

                            Assert.True(token.HasChanged);
                        }
                    }
                }
            }
        }

        [Fact]
        public void NoopChangeTokenForNullFilter()
        {
            using (var root = new DisposableFileSystem())
            {
                using (var provider = new PhysicalFileProvider(root.RootPath))
                {
                    var token = provider.Watch(null);

                    Assert.Same(NoopChangeToken.Singleton, token);
                }
            }
        }

        [Fact]
        public void TokenForEmptyFilter()
        {
            using (var root = new DisposableFileSystem())
            {
                using (var provider = new PhysicalFileProvider(root.RootPath))
                {
                    var token = provider.Watch(string.Empty);

                    Assert.False(token.HasChanged);
                    Assert.True(token.ActiveChangeCallbacks);
                }
            }
        }

        [Fact]
        public void TokenForWhitespaceFilters()
        {
            using (var root = new DisposableFileSystem())
            {
                using (var provider = new PhysicalFileProvider(root.RootPath))
                {
                    var token = provider.Watch("  ");

                    Assert.False(token.HasChanged);
                    Assert.True(token.ActiveChangeCallbacks);
                }
            }
        }

        [ConditionalFact]
        [OSSkipCondition(OperatingSystems.Linux,
            SkipReason = "We treat forward slash differently so rooted path can happen only on windows.")]
        [OSSkipCondition(OperatingSystems.MacOSX,
            SkipReason = "We treat forward slash differently so rooted path can happen only on windows.")]
        public void NoopChangeTokenForAbsolutePathFilters()
        {
            using (var root = new DisposableFileSystem())
            {
                using (var provider = new PhysicalFileProvider(root.RootPath))
                {
                    var path = Path.Combine(root.RootPath, Guid.NewGuid().ToString());
                    var token = provider.Watch(path);

                    Assert.Same(NoopChangeToken.Singleton, token);
                }
            }
        }

        [Fact]
        public async Task TokenFiredOnCreation()
        {
            using (var root = new DisposableFileSystem())
            {
                using (var fileSystemWatcher = new MockFileSystemWatcher(root.RootPath))
                {
                    using (var physicalFilesWatcher = new PhysicalFilesWatcher(root.RootPath + Path.DirectorySeparatorChar, fileSystemWatcher))
                    {
                        using (var provider = new PhysicalFileProvider(root.RootPath, physicalFilesWatcher))
                        {
                            var name = Guid.NewGuid().ToString();
                            var token = provider.Watch(name);

                            fileSystemWatcher.CallOnChanged(new FileSystemEventArgs(WatcherChangeTypes.Created, root.RootPath, name));
                            await Task.Delay(WaitTimeForTokenToFire);

                            Assert.True(token.HasChanged);
                        }
                    }
                }
            }
        }

        [Fact]
        public async Task TokenFiredOnDeletion()
        {
            using (var root = new DisposableFileSystem())
            {
                using (var fileSystemWatcher = new MockFileSystemWatcher(root.RootPath))
                {
                    using (var physicalFilesWatcher = new PhysicalFilesWatcher(root.RootPath + Path.DirectorySeparatorChar, fileSystemWatcher))
                    {
                        using (var provider = new PhysicalFileProvider(root.RootPath, physicalFilesWatcher))
                        {
                            var name = Guid.NewGuid().ToString();
                            var token = provider.Watch(name);

                            fileSystemWatcher.CallOnDeleted(new FileSystemEventArgs(WatcherChangeTypes.Deleted, root.RootPath, name));
                            await Task.Delay(WaitTimeForTokenToFire);

                            Assert.True(token.HasChanged);
                        }
                    }
                }
            }
        }

        [Fact]
        public async Task TokenFiredForFilesUnderPathEndingWithSlash()
        {
            using (var root = new DisposableFileSystem())
            {
                using (var fileSystemWatcher = new MockFileSystemWatcher(root.RootPath))
                {
                    using (var physicalFilesWatcher = new PhysicalFilesWatcher(root.RootPath + Path.DirectorySeparatorChar, fileSystemWatcher))
                    {
                        using (var provider = new PhysicalFileProvider(root.RootPath, physicalFilesWatcher))
                        {
                            var directoryName = Guid.NewGuid().ToString();
                            var token = provider.Watch(directoryName + Path.DirectorySeparatorChar);

                            fileSystemWatcher.CallOnChanged(new FileSystemEventArgs(WatcherChangeTypes.Changed, Path.Combine(root.RootPath, directoryName), Guid.NewGuid().ToString()));
                            await Task.Delay(WaitTimeForTokenToFire);

                            Assert.True(token.HasChanged);
                        }
                    }
                }
            }
        }

        [Fact]
        public async Task TokenFiredForRelativePathStartingWithSlash()
        {
            using (var root = new DisposableFileSystem())
            {
                using (var fileSystemWatcher = new MockFileSystemWatcher(root.RootPath))
                {
                    using (var physicalFilesWatcher = new PhysicalFilesWatcher(root.RootPath + Path.DirectorySeparatorChar, fileSystemWatcher))
                    {
                        using (var provider = new PhysicalFileProvider(root.RootPath, physicalFilesWatcher))
                        {
                            var fileName = Guid.NewGuid().ToString();
                            var token = provider.Watch("/" + fileName);

                            fileSystemWatcher.CallOnChanged(new FileSystemEventArgs(WatcherChangeTypes.Changed, root.RootPath, fileName));
                            await Task.Delay(WaitTimeForTokenToFire);

                            Assert.True(token.HasChanged);
                        }
                    }
                }
            }
        }

        [Fact]
        public async Task TokensFiredForRegularExpressionPatterns()
        {
            using (var root = new DisposableFileSystem())
            {
                using (var fileSystemWatcher = new MockFileSystemWatcher(root.RootPath))
                {
                    using (var physicalFilesWatcher = new PhysicalFilesWatcher(root.RootPath + Path.DirectorySeparatorChar, fileSystemWatcher))
                    {
                        using (var provider = new PhysicalFileProvider(root.RootPath, physicalFilesWatcher))
                        {
                            var token1 = provider.Watch("**/*");
                            var pattern1tokenCount = 0;
                            token1.RegisterChangeCallback(_ => { pattern1tokenCount++; }, null);

                            var token2 = provider.Watch("*.cshtml");
                            var pattern2tokenCount = 0;
                            token2.RegisterChangeCallback(_ => { pattern2tokenCount++; }, null);

                            var fileName = Guid.NewGuid().ToString();

                            fileSystemWatcher.CallOnChanged(new FileSystemEventArgs(WatcherChangeTypes.Changed, root.RootPath, fileName + ".cshtml"));
                            await Task.Delay(WaitTimeForTokenToFire);

                            Assert.Equal(1, pattern1tokenCount);
                            Assert.Equal(1, pattern2tokenCount);

                            token1 = provider.Watch("**/*");
                            token1.RegisterChangeCallback(_ => { pattern1tokenCount++; }, null);
                            fileSystemWatcher.CallOnChanged(new FileSystemEventArgs(WatcherChangeTypes.Changed, root.RootPath, fileName + ".txt"));
                            await Task.Delay(WaitTimeForTokenToFire);

                            Assert.Equal(2, pattern1tokenCount);
                            Assert.Equal(1, pattern2tokenCount);
                        }
                    }
                }
            }
        }

        [Fact]
        public async Task TokenFiredForRegularExpressionPatternsPointingToSubDirectory()
        {
            using (var root = new DisposableFileSystem())
            {
                using (var fileSystemWatcher = new MockFileSystemWatcher(root.RootPath))
                {
                    using (var physicalFilesWatcher = new PhysicalFilesWatcher(root.RootPath + Path.DirectorySeparatorChar, fileSystemWatcher))
                    {
                        using (var provider = new PhysicalFileProvider(root.RootPath, physicalFilesWatcher))
                        {
                            var subDirectoryName = Guid.NewGuid().ToString();
                            var pattern = string.Format(Path.Combine(subDirectoryName, "**", "*.cshtml"));
                            var token = provider.Watch(pattern);

                            var subSubDirectoryName = Guid.NewGuid().ToString();
                            var fileName = Guid.NewGuid().ToString() + ".cshtml";
                            fileSystemWatcher.CallOnChanged(new FileSystemEventArgs(WatcherChangeTypes.Changed, Path.Combine(root.RootPath, subDirectoryName, subSubDirectoryName), fileName));
                            await Task.Delay(WaitTimeForTokenToFire);

                            Assert.True(token.HasChanged);
                        }
                    }
                }
            }
        }

        [Fact]
        public void TokensWithForwardAndBackwardSlashesAreSame()
        {
            using (var provider = new PhysicalFileProvider(Path.GetTempPath()))
            {
                var token1 = provider.Watch(@"a/b\c");
                var token2 = provider.Watch(@"a\b/c");

                Assert.Equal(token1, token2);
            }
        }

        [Fact]
        public async Task TokensFiredForOldAndNewNamesOnRename()
        {
            using (var root = new DisposableFileSystem())
            {
                using (var fileSystemWatcher = new MockFileSystemWatcher(root.RootPath))
                {
                    using (var physicalFilesWatcher = new PhysicalFilesWatcher(root.RootPath + Path.DirectorySeparatorChar, fileSystemWatcher))
                    {
                        using (var provider = new PhysicalFileProvider(root.RootPath, physicalFilesWatcher))
                        {
                            var oldFileName = Guid.NewGuid().ToString();
                            var oldToken = provider.Watch(oldFileName);

                            var newFileName = Guid.NewGuid().ToString();
                            var newToken = provider.Watch(newFileName);

                            fileSystemWatcher.CallOnRenamed(new RenamedEventArgs(WatcherChangeTypes.Renamed, root.RootPath, newFileName, oldFileName));
                            await Task.Delay(WaitTimeForTokenToFire);
                            
                            Assert.True(oldToken.HasChanged);
                            Assert.True(newToken.HasChanged);
                        }
                    }
                }
            }
        }

        [Fact]
        public async Task TokensFiredForNewDirectoryContentsOnRename()
        {
            using (var root = new DisposableFileSystem())
            {
                using (var fileSystemWatcher = new MockFileSystemWatcher(root.RootPath))
                {
                    using (var physicalFilesWatcher = new PhysicalFilesWatcher(root.RootPath + Path.DirectorySeparatorChar, fileSystemWatcher))
                    {
                        using (var provider = new PhysicalFileProvider(root.RootPath, physicalFilesWatcher))
                        {
                            var oldDirectoryName = Guid.NewGuid().ToString();
                            var oldSubDirectoryName = Guid.NewGuid().ToString();
                            var oldSubDirectoryPath = Path.Combine(oldDirectoryName, oldSubDirectoryName);
                            var oldFileName = Guid.NewGuid().ToString();
                            var oldFilePath = Path.Combine(oldDirectoryName, oldSubDirectoryName, oldFileName);

                            var newDirectoryName = Guid.NewGuid().ToString();
                            var newSubDirectoryName = Guid.NewGuid().ToString();
                            var newSubDirectoryPath = Path.Combine(newDirectoryName, newSubDirectoryName);
                            var newFileName = Guid.NewGuid().ToString();
                            var newFilePath = Path.Combine(newDirectoryName, newSubDirectoryName, newFileName);

                            Directory.CreateDirectory(Path.Combine(root.RootPath, newDirectoryName));
                            Directory.CreateDirectory(Path.Combine(root.RootPath, newDirectoryName, newSubDirectoryName));
                            File.Create(Path.Combine(root.RootPath, newDirectoryName, newSubDirectoryName, newFileName));

                            await Task.Delay(WaitTimeForTokenToFire);

                            var oldDirectoryToken = provider.Watch(oldDirectoryName);
                            var oldSubDirectoryToken = provider.Watch(oldSubDirectoryPath);
                            var oldFileToken = provider.Watch(oldFilePath);

                            var newDirectoryToken = provider.Watch(newDirectoryName);
                            var newSubDirectoryToken = provider.Watch(newSubDirectoryPath);
                            var newFileToken = provider.Watch(newFilePath);

                            await Task.Delay(WaitTimeForTokenToFire);

                            Assert.False(oldDirectoryToken.HasChanged);
                            Assert.False(oldSubDirectoryToken.HasChanged);
                            Assert.False(oldFileToken.HasChanged);
                            Assert.False(newDirectoryToken.HasChanged);
                            Assert.False(newSubDirectoryToken.HasChanged);
                            Assert.False(newFileToken.HasChanged);

                            fileSystemWatcher.CallOnRenamed(new RenamedEventArgs(WatcherChangeTypes.Renamed, root.RootPath, newDirectoryName, oldDirectoryName));
                            await Task.Delay(WaitTimeForTokenToFire);

                            Assert.True(oldDirectoryToken.HasChanged);
                            Assert.False(oldSubDirectoryToken.HasChanged);
                            Assert.False(oldFileToken.HasChanged);
                            Assert.True(newDirectoryToken.HasChanged);
                            Assert.True(newSubDirectoryToken.HasChanged);
                            Assert.True(newFileToken.HasChanged);
                        }
                    }
                }
            }
        }

        [Fact]
        public async Task TokenNotFiredForFileNameStartingWithPeriod()
        {
            using (var root = new DisposableFileSystem())
            {
                using (var fileSystemWatcher = new MockFileSystemWatcher(root.RootPath))
                {
                    using (var physicalFilesWatcher = new PhysicalFilesWatcher(root.RootPath + Path.DirectorySeparatorChar, fileSystemWatcher))
                    {
                        using (var provider = new PhysicalFileProvider(root.RootPath, physicalFilesWatcher))
                        {
                            var fileName = "." + Guid.NewGuid().ToString();
                            var token = provider.Watch(Path.GetFileName(fileName));

                            fileSystemWatcher.CallOnChanged(new FileSystemEventArgs(WatcherChangeTypes.Changed, root.RootPath, fileName));
                            await Task.Delay(WaitTimeForTokenToFire);

                            Assert.False(token.HasChanged);
                        }
                    }
                }
            }
        }

        [ConditionalFact]
        [OSSkipCondition(OperatingSystems.Linux, SkipReason = "Hidden and system files only make sense on Windows.")]
        [OSSkipCondition(OperatingSystems.MacOSX, SkipReason = "Hidden and system files only make sense on Windows.")]
        public async Task TokensNotFiredForHiddenAndSystemFiles()
        {
            using (var root = new DisposableFileSystem())
            {
                var hiddenFileName = Guid.NewGuid().ToString();
                var hiddenFilePath = Path.Combine(root.RootPath, hiddenFileName);
                File.Create(hiddenFilePath);
                var fileInfo = new FileInfo(hiddenFilePath);
                File.SetAttributes(hiddenFilePath, fileInfo.Attributes | FileAttributes.Hidden);

                var systemFileName = Guid.NewGuid().ToString();
                var systemFilePath = Path.Combine(root.RootPath, systemFileName);
                File.Create(systemFilePath);
                fileInfo = new FileInfo(systemFilePath);
                File.SetAttributes(systemFilePath, fileInfo.Attributes | FileAttributes.System);

                using (var fileSystemWatcher = new MockFileSystemWatcher(root.RootPath))
                {
                    using (var physicalFilesWatcher = new PhysicalFilesWatcher(root.RootPath + Path.DirectorySeparatorChar, fileSystemWatcher))
                    {
                        using (var provider = new PhysicalFileProvider(root.RootPath, physicalFilesWatcher))
                        {
                            var hiddenFiletoken = provider.Watch(Path.GetFileName(hiddenFileName));
                            var systemFiletoken = provider.Watch(Path.GetFileName(systemFileName));

                            fileSystemWatcher.CallOnChanged(new FileSystemEventArgs(WatcherChangeTypes.Changed, root.RootPath, hiddenFileName));
                            await Task.Delay(WaitTimeForTokenToFire);
                            Assert.False(hiddenFiletoken.HasChanged);

                            fileSystemWatcher.CallOnChanged(new FileSystemEventArgs(WatcherChangeTypes.Changed, root.RootPath, systemFileName));
                            await Task.Delay(WaitTimeForTokenToFire);
                            Assert.False(systemFiletoken.HasChanged);
                        }
                    }
                }
            }
        }

        [Fact]
        public async Task TokensFiredForAllEntriesOnError()
        {
            using (var root = new DisposableFileSystem())
            {
                using (var fileSystemWatcher = new MockFileSystemWatcher(root.RootPath))
                {
                    using (var physicalFilesWatcher = new PhysicalFilesWatcher(root.RootPath + Path.DirectorySeparatorChar, fileSystemWatcher))
                    {
                        using (var provider = new PhysicalFileProvider(root.RootPath, physicalFilesWatcher))
                        {
                            var token1 = provider.Watch(Guid.NewGuid().ToString());
                            var token2 = provider.Watch(Guid.NewGuid().ToString());
                            var token3 = provider.Watch(Guid.NewGuid().ToString());

                            fileSystemWatcher.CallOnError(new ErrorEventArgs(new Exception()));
                            await Task.Delay(WaitTimeForTokenToFire);

                            Assert.True(token1.HasChanged);
                            Assert.True(token2.HasChanged);
                            Assert.True(token3.HasChanged);
                        }
                    }
                }
            }
        }
    }
}
