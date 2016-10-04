﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using EnvDTE;
using Microsoft.VisualStudio.Web.BrowserLink;

namespace BrowserReloadOnSave
{
    public class ReloadExtension : BrowserLinkExtension, IDisposable
    {
        IEnumerable<string> _extensions = VSPackage.Options.FileExtensions.Split(';');
        IEnumerable<string> _ignorePatterns = VSPackage.Options.GetIgnorePatterns();
        List<BrowserLinkConnection> _connections = new List<BrowserLinkConnection>();

        bool _isDisposed;
        Project _project;
        Timer _timer;
        FileSystemWatcher _watcher;
        int _state;

        public ReloadExtension(Project project)
        {
            _project = project;
            string folder = project.GetRootFolder();

            if (string.IsNullOrEmpty(folder))
                return;

            _watcher = new FileSystemWatcher(folder);
            _watcher.Changed += FileChanged;
            _watcher.IncludeSubdirectories = true;
            _watcher.NotifyFilter = NotifyFilters.Size | NotifyFilters.CreationTime;
            _watcher.EnableRaisingEvents = VSPackage.Options.EnableReload;

            _timer = new Timer(TimerElapsed, null, 0, VSPackage.Options.Delay);

            VSPackage.Options.Saved += OptionsSaved;
        }

        public override void OnConnected(BrowserLinkConnection connection)
        {
            if (connection.Project == _project)
                _connections.Add(connection);

            base.OnConnected(connection);
        }

        public override void OnDisconnecting(BrowserLinkConnection connection)
        {
            if (_connections.Contains(connection))
                _connections.Remove(connection);

            base.OnDisconnecting(connection);
        }

        public void Reload()
        {
            Browsers.Clients(_connections.ToArray()).Invoke("reload");
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                VSPackage.Options.Saved -= OptionsSaved;
                _watcher.Dispose();
                _timer.Dispose();

                _isDisposed = true;
            }
        }

        void FileChanged(object sender, FileSystemEventArgs e)
        {
            if (e.ChangeType != WatcherChangeTypes.Changed)
                return;

            string path = e.FullPath.ToLowerInvariant();
            string file = Path.GetFileName(path);

            if (_extensions.Any(ext => file.Contains($".{ext}")) && !_ignorePatterns.Any(p => file.Contains(p)))
            {
                // Only reload on CSS file changes if it's a ASP.NET Core project, due to a bug in Browser Link
                if (file.Contains(".css") && !_project.Kind.Equals("{8BB2217D-0F2D-49D1-97BC-3654ED321F3B}", StringComparison.OrdinalIgnoreCase))
                    return;

                Interlocked.Exchange(ref _state, 2);
            }
        }

        void TimerElapsed(object state)
        {
            //Move from changed + refresh pending to just refresh pending
            if (Interlocked.CompareExchange(ref _state, 1, 2) == 2)
            {
                return;
            }

            //Move from refresh pending without a recent change to no refresh pending
            if (Interlocked.CompareExchange(ref _state, 0, 1) == 1)
            {
                Reload();
            }
        }

        void OptionsSaved(object sender, EventArgs e)
        {
            _extensions = VSPackage.Options.FileExtensions.Split(';');
            _ignorePatterns = VSPackage.Options.GetIgnorePatterns();
            _watcher.EnableRaisingEvents = VSPackage.Options.EnableReload;
        }
    }
}
