﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Diagnostics;
using System.Threading;
using Microsoft.Common.Core;
using Microsoft.Common.Core.IO;
using Microsoft.Extensions.FileSystemGlobbing;

namespace Microsoft.R.Platform.IO {
    public abstract class FileSystem : IFileSystem {
        public IFileSystemWatcher CreateFileSystemWatcher(string path, string filter) => new FileSystemWatcherProxy(path, filter);
        public IDirectoryInfo GetDirectoryInfo(string directoryPath) => new DirectoryInfoProxy(directoryPath);
        public bool FileExists(string path) => File.Exists(path);

        public long FileSize(string path) {
            var fileInfo = new FileInfo(path);
            return fileInfo.Length;
        }

        public string ReadAllText(string path) => File.ReadAllText(path);
        public void WriteAllText(string path, string content) => File.WriteAllText(path, content);
        public IEnumerable<string> FileReadAllLines(string path) => File.ReadLines(path);
        public void FileWriteAllLines(string path, IEnumerable<string> contents) => File.WriteAllLines(path, contents);
        public byte[] FileReadAllBytes(string path) => File.ReadAllBytes(path);
        public void FileWriteAllBytes(string path, byte[] bytes) => File.WriteAllBytes(path, bytes);
        public Stream CreateFile(string path) => File.Create(path);
        public Stream FileOpen(string path, FileMode mode) => File.Open(path, mode);
        public bool DirectoryExists(string path) => Directory.Exists(path);
        public FileAttributes GetFileAttributes(string path) => File.GetAttributes(path);
        public string ToLongPath(string path) => path;
        public string ToShortPath(string path) => path;
        public Version GetFileVersion(string path) {
            var fvi = FileVersionInfo.GetVersionInfo(path);
            return new Version(fvi.FileMajorPart, fvi.FileMinorPart, fvi.FileBuildPart, fvi.FilePrivatePart);
        }

        public void DeleteFile(string path) => File.Delete(path);
        public void DeleteDirectory(string path, bool recursive) => Directory.Delete(path, recursive);
        public string[] GetFileSystemEntries(string path, string searchPattern, SearchOption options) => Directory.GetFileSystemEntries(path, searchPattern, options);
        public void CreateDirectory(string path) => Directory.CreateDirectory(path);
        public string[] GetFiles(string path) => Directory.GetFiles(path);
        public string[] GetFiles(string path, string pattern) => Directory.GetFiles(path, pattern);
        public string[] GetFiles(string path, string pattern, SearchOption option) => Directory.GetFiles(path, pattern, option);
        public string[] GetDirectories(string path) => Directory.GetDirectories(path);

        public string CompressFile(string path, string relativeTodir) {
            var zipFilePath = Path.GetTempFileName();
            using (var zipStream = new FileStream(zipFilePath, FileMode.Create))
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create)) {
                var entryName = Path.GetFileName(path);
                if (!string.IsNullOrWhiteSpace(relativeTodir)) {
                    entryName = path.MakeRelativePath(relativeTodir).Replace('\\', '/');
                }
                archive.CreateEntryFromFile(path, entryName);
            }
            return zipFilePath;
        }

        public string CompressFiles(IEnumerable<string> paths, string relativeTodir, IProgress<string> progress, CancellationToken ct) {
            var zipFilePath = Path.GetTempFileName();
            using (var zipStream = new FileStream(zipFilePath, FileMode.Create))
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create)) {
                foreach (var path in paths) {
                    if (ct.IsCancellationRequested) {
                        break;
                    }

                    string entryName = null;
                    if (!string.IsNullOrWhiteSpace(relativeTodir)) {
                        entryName = path.MakeRelativePath(relativeTodir).Replace('\\', '/');
                    } else {
                        entryName = path.MakeRelativePath(Path.GetDirectoryName(path)).Replace('\\', '/');
                    }
                    progress?.Report(path);
                    archive.CreateEntryFromFile(path, entryName);
                }
            }
            return zipFilePath;
        }

        public string CompressDirectory(string path) {
            var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
            matcher.AddInclude("*.*");
            return CompressDirectory(path, matcher, new Progress<string>((p) => { }), CancellationToken.None);
        }

        public string CompressDirectory(string path, Matcher matcher, IProgress<string> progress, CancellationToken ct) {
            var zipFilePath = Path.GetTempFileName();
            using (var zipStream = new FileStream(zipFilePath, FileMode.Create))
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create)) {
                var dirs = new Queue<string>();
                dirs.Enqueue(path);
                while (dirs.Count > 0) {
                    var dir = dirs.Dequeue();
                    var subdirs = Directory.GetDirectories(dir);
                    foreach (var subdir in subdirs) {
                        dirs.Enqueue(subdir);
                    }

                    var files = matcher.GetResultsInFullPath(dir);
                    foreach (var file in files) {
                        if (ct.IsCancellationRequested) {
                            return string.Empty;
                        }
                        progress?.Report(file);
                        var entryName = file.MakeRelativePath(dir).Replace('\\', '/');
                        archive.CreateEntryFromFile(file, entryName);
                    }
                }
            }
            return zipFilePath;
        }

        public virtual string GetDownloadsPath(string fileName)
            => throw new NotImplementedException("GetDownloadsPath not implemented in platform.");
    }
}
