﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.Composition;
using System.ComponentModel.Composition.Hosting;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Diagnostics.Runtime.Utilities;
using PEExplorer.Core;
using PEExplorer.Helpers;
using PEExplorer.ViewModels.Tabs;
using Prism.Commands;
using Prism.Mvvm;
using Zodiacon.WPF;

namespace PEExplorer.ViewModels {
    [Export]
    class MainViewModel : BindableBase {
        public string Title => PathName == null ? null : $"{Constants.AppName} {Constants.Copyright} ({PathName}) ";

        readonly ObservableCollection<TabViewModelBase> _tabs = new ObservableCollection<TabViewModelBase>();
        readonly ObservableCollection<string> _recentFiles = new ObservableCollection<string>();

        public IList<TabViewModelBase> Tabs => _tabs;
        public IList<string> RecentFiles => _recentFiles;

        public MainViewModel() {
            var recentFiles = Serializer.Load<ObservableCollection<string>>("RecentFiles");
            if(recentFiles != null)
                _recentFiles = recentFiles;
        }

        internal void Close() {
            Serializer.Save(_recentFiles, "RecentFiles");
        }

        public void SelectTab(TabViewModelBase tab) {
            if(!Tabs.Contains(tab))
                Tabs.Add(tab);
            SelectedTab = tab;
        }

        private TabViewModelBase _selectedTab;

        public TabViewModelBase SelectedTab {
            get { return _selectedTab; }
            set { SetProperty(ref _selectedTab, value); }
        }

        private string _fileName;
        private PEHeader _peHeader;
        public PEFileHelper PEFileHelper { get; private set; }

        public string PathName { get; set; }
        public PEHeader PEHeader {
            get { return _peHeader; }
            set { SetProperty(ref _peHeader, value); }
        }


        public string FileName {
            get { return _fileName; }
            set { SetProperty(ref _fileName, value); }
        }

        [Import]
        IFileDialogService FileDialogService;

        [Import]
        IMessageBoxService MessageBoxService;

        [Import]
        public CompositionContainer Container { get; private set; }

        ObservableCollection<TreeViewItemViewModel> _treeRoot = new ObservableCollection<TreeViewItemViewModel>();

        public IList<TreeViewItemViewModel> TreeRoot => _treeRoot;

        public ICommand OpenCommand => new DelegateCommand(() => {
            try {
                var filename = FileDialogService.GetFileForOpen("PE Files (*.exe;*.dll;*.ocx;*.obj;*.lib)|*.exe;*.dll;*.ocx;*.obj;*.lib", "Select File");
                if(filename == null) return;
                OpenInternal(filename);
            }
            catch(Exception ex) {
                MessageBoxService.ShowMessage(ex.Message, "PE Explorer");
            }
        });

        private void BuildTree() {
            TreeRoot.Clear();
            var root = new TreeViewItemViewModel(this) { Text = FileName, Icon = "/icons/data.ico", IsExpanded = true };
            TreeRoot.Add(root);

            var generalTab = Container.GetExportedValue<GeneralTabViewModel>();
            root.Items.Add(new TreeViewItemViewModel(this) { Text = "(General)", Icon = "/icons/general.ico", Tab = generalTab });
            Tabs.Add(generalTab);

            if(PEHeader.ExportDirectory.VirtualAddress > 0) {
                var exportTab = Container.GetExportedValue<ExportsTabViewModel>();
                root.Items.Add(new TreeViewItemViewModel(this) { Text = "Exports (.edata)", Icon = "/icons/export1.ico", Tab = exportTab });
            }
            if(PEHeader.ImportDirectory.VirtualAddress > 0) {
                var importsTab = Container.GetExportedValue<ImportsTabViewModel>();
                root.Items.Add(new TreeViewItemViewModel(this) { Text = "Imports (.idata)", Icon = "/icons/import2.ico", Tab = importsTab });
            }

            //if(PEHeader.ImportAddressTableDirectory.VirtualAddress > 0) {
            //    var iatTab = Container.GetExportedValue<ImportAddressTableTabViewModel>();
            //    root.Items.Add(new TreeViewItemViewModel(this) { Text = "Import Address Table", Icon = "/icons/iat.ico", Tab = iatTab });
            //}

            if(PEHeader.ResourceDirectory.VirtualAddress > 0)
                root.Items.Add(new TreeViewItemViewModel(this) {
                    Text = "Resources (.rsrc)",
                    Icon = "/icons/resources.ico",
                    Tab = Container.GetExportedValue<ResourcesTabViewModel>()
                });

            //if(PEHeader.ComDescriptorDirectory.VirtualAddress > 0) {
            //    root.Items.Add(new TreeViewItemViewModel(this) {
            //        Text = "CLR",
            //        Icon = "/icons/cpu.ico",
            //        Tab = Container.GetExportedValue<CLRTabViewModel>()
            //    });
            //}

            SelectedTab = generalTab;
        }

        public ICommand SelectTabCommand => new DelegateCommand<TreeViewItemViewModel>(item => {
            if(item != null)
                SelectTab(item.Tab);
        });

        MemoryMappedFile _mmf;
        FileStream _stm;
        public MemoryMappedViewAccessor Accessor { get; private set; }

        private void MapFile() {
            _mmf = MemoryMappedFile.CreateFromFile(_stm, null, 0, MemoryMappedFileAccess.Read, null, HandleInheritability.None, false);
            Accessor = _mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            PEFileHelper = new PEFileHelper(PEHeader, Accessor);
        }

        public PEFile PEFile { get; private set; }
        private void OpenInternal(string filename) {
            CloseCommand.Execute(null);
            try {
                PEFile = new PEFile(_stm = File.Open(filename, FileMode.Open, FileAccess.Read, FileShare.Read), false);
                PEHeader = PEFile.Header;
                FileName = Path.GetFileName(filename);
                PathName = filename;
                OnPropertyChanged(nameof(Title));
                MapFile();

                BuildTree();
                RecentFiles.Remove(PathName);
                RecentFiles.Insert(0, PathName);
                if(RecentFiles.Count > 10)
                    RecentFiles.RemoveAt(RecentFiles.Count - 1);
            }
            catch(Exception ex) {
                MessageBoxService.ShowMessage($"Error: {ex.Message}", Constants.AppName);
            }

        }

        public ICommand ExitCommand => new DelegateCommand(() => Application.Current.Shutdown());

        public ICommand CloseCommand => new DelegateCommand(() => {
            if(PEFile != null && !PEFile.Disposed)
                PEFile.Dispose();
            FileName = null;
            PEHeader = null;
            if(Accessor != null)
                Accessor.Dispose();
            if(_mmf != null)
                _mmf.Dispose();
            _tabs.Clear();
            _treeRoot.Clear();
            OnPropertyChanged(nameof(Title));
        });

        public ICommand CloseTabCommand => new DelegateCommand<TabViewModelBase>(tab => Tabs.Remove(tab));

        public ICommand OpenRecentFileCommand => new DelegateCommand<string>(filename => OpenInternal(filename));

        private bool _isTopmost;

        public bool IsTopmost {
            get { return _isTopmost; }
            set {
                if(SetProperty(ref _isTopmost, value)) {
                    var win = Application.Current.MainWindow;
                    if(win != null)
                        win.Topmost = value;
                }
            }
        }

        public ICommand ViewExportsCommand => new DelegateCommand(() =>
             SelectTabCommand.Execute(TreeRoot[0].Items.SingleOrDefault(item => item.Tab is ExportsTabViewModel)),
             () => PEHeader?.ExportDirectory.VirtualAddress > 0).ObservesProperty(() => PEHeader);
        public ICommand ViewImportsCommand => new DelegateCommand(() =>
             SelectTabCommand.Execute(TreeRoot[0].Items.SingleOrDefault(item => item.Tab is ImportsTabViewModel)),
             () => PEHeader?.ImportDirectory.VirtualAddress > 0).ObservesProperty(() => PEHeader);
        public ICommand ViewResourcesCommand => new DelegateCommand(() =>
             SelectTabCommand.Execute(TreeRoot[0].Items.SingleOrDefault(item => item.Tab is ResourcesTabViewModel)),
             () => PEHeader?.ResourceDirectory.VirtualAddress > 0).ObservesProperty(() => PEHeader);
    }
}
