using AssetsTools.NET;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System;
using System.IO;
using AssetsTools.NET.Extra;
using Avalonia.Markup.Xaml.Templates;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using System.ComponentModel;

namespace UABEAvalonia
{
    public partial class ModMakerDialog : Window
    {
        //controls
        private TextBox boxModName;
        private TextBox boxCredits;
        private TextBox boxDesc;
        private TextBox boxBaseFolder;
        private Button btnBaseFolder;
        private TreeView treeView;
        private Button btnImport;
        private Button btnRemove;
        private Button btnOk;
        private Button btnCancel;

        private bool isBundle;
        private bool builtTree;
        private TreeViewItem affectedBundles;
        private TreeViewItem affectedFiles;
        private AssetWorkspace assetWs;
        private Stream importedEmipStream;
        //private BundleWorkspace bundleWs;

        private ObservableCollection<ModMakerTreeFileInfo> filesItems;
        private Dictionary<string, ModMakerTreeFileInfo> fileToTvi;

        public ModMakerDialog()
        {
            InitializeComponent();
#if DEBUG
            this.AttachDevTools();
#endif
            //generated items
            boxModName = this.FindControl<TextBox>("boxModName");
            boxCredits = this.FindControl<TextBox>("boxCredits");
            boxDesc = this.FindControl<TextBox>("boxDesc");
            boxBaseFolder = this.FindControl<TextBox>("boxBaseFolder");
            btnBaseFolder = this.FindControl<Button>("btnBaseFolder");
            treeView = this.FindControl<TreeView>("treeView");
            btnImport = this.FindControl<Button>("btnImport");
            btnRemove = this.FindControl<Button>("btnRemove");
            btnOk = this.FindControl<Button>("btnOk");
            btnCancel = this.FindControl<Button>("btnCancel");
            //generated events
            btnBaseFolder.Click += BtnBaseFolder_Click;
            btnImport.Click += BtnImport_Click;
            btnOk.Click += BtnOk_Click;
            btnCancel.Click += BtnCancel_Click;

            //workaround since there is no textchanged event
            boxBaseFolder.GetObservable(TextBox.TextProperty).Subscribe(text => UpdateTree());
        }

        //for assets files
        public ModMakerDialog(AssetWorkspace workspace) : this()
        {
            assetWs = workspace;
            isBundle = false;

            BuildTreeAssets();
        }

        //for assets files in bundles
        //public ModMakerDialog(BundleWorkspace workspace) : this()
        //{
        //
        //}

        private void BuildTreeAssets()
        {
            builtTree = false;

            affectedBundles = CreateTreeItem("Affected bundles");
            affectedFiles = CreateTreeItem("Affected assets files");

            treeView.Items = new List<TreeViewItem>() { affectedBundles, affectedFiles };

            string rootPath = boxBaseFolder.Text;

            filesItems = new ObservableCollection<ModMakerTreeFileInfo>();
            fileToTvi = new Dictionary<string, ModMakerTreeFileInfo>();

            foreach (var newAsset in assetWs.NewAssets)
            {
                string file = newAsset.Key.fileName;
                if (!fileToTvi.ContainsKey(file))
                {
                    ModMakerTreeFileInfo newFileItem = new ModMakerTreeFileInfo(file, rootPath);
                    filesItems.Add(newFileItem);
                    fileToTvi.Add(file, newFileItem);
                }

                ModMakerTreeFileInfo fileItem = fileToTvi[file];

                var obsItems = fileItem.Replacers;
                obsItems.Add(new ModMakerTreeReplacerInfo(newAsset.Key, newAsset.Value));
            }

            affectedFiles.Items = filesItems;

            builtTree = true;
        }

        private void UpdateTree()
        {
            if (!builtTree)
                return;

            if (!isBundle)
            {
                string rootPath = boxBaseFolder.Text;
                foreach (ModMakerTreeFileInfo fileItem in affectedFiles.Items)
                {
                    fileItem.UpdateRootPath(rootPath);
                }
            }
            else
            {
                //todo
            }
        }
        
        private bool IsPathRootedSafe(string path)
        {
            try
            {
                return Path.IsPathRooted(path);
            }
            catch
            {
                return false;
            }
        }

        private TreeViewItem CreateTreeItem(string text)
        {
            return new TreeViewItem() { Header = text };
        }

        private void BuildEmip(string path)
        {
            InstallerPackageFile emip = new InstallerPackageFile
            {
                magic = "EMIP",
                includesCldb = false,
                modName = boxModName.Text ?? "",
                modCreators = boxCredits.Text ?? "",
                modDescription = boxDesc.Text ?? ""
            };

            emip.affectedFiles = new List<InstallerPackageAssetsDesc>();

            foreach (ModMakerTreeFileInfo file in affectedFiles.Items)
            {
                //hack pls fix thx
                string filePath = file.relPath;
                InstallerPackageAssetsDesc desc = new InstallerPackageAssetsDesc()
                {
                    isBundle = false,
                    path = filePath
                };
                desc.replacers = new List<object>();
                foreach (ModMakerTreeReplacerInfo change in file.Replacers)
                {
                    desc.replacers.Add(change.assetsReplacer);
                }
                emip.affectedFiles.Add(desc);
            }

            using (FileStream fs = File.OpenWrite(path))
            using (AssetsFileWriter writer = new AssetsFileWriter(fs))
            {
                emip.Write(writer);
            }
        }

        private async void BtnBaseFolder_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var openDir = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select base folder"
            });

            if (openDir == null || openDir.Count <= 0) return;
            if (!openDir[0].TryGetUri(out Uri? uri) || uri == null) return;

            string dir = Path.GetFullPath(uri.OriginalString);

            boxBaseFolder.Text = dir;
        }

        private async void BtnImport_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var openFiles = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Open package file",
                FileTypeFilter = new List<FilePickerFileType>()
                {
                    new FilePickerFileType("UABE Mod Installer Package") { Patterns = new List<string>() { "*.emip" } }
                }
            });

            if (openFiles == null || openFiles.Count <= 0) return;
            if (!openFiles[0].TryGetUri(out Uri? uri) || uri == null) return;

            string emipPath = Path.GetFullPath(uri.OriginalString);

            var openDir = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select base folder"
            });

            if (openDir == null || openDir.Count <= 0) return;
            if (!openDir[0].TryGetUri(out uri) || uri == null) return;

            string rootPath = Path.GetFullPath(uri.OriginalString);

            InstallerPackageFile impEmip = new InstallerPackageFile();

            if (importedEmipStream != null && importedEmipStream.CanRead)
                importedEmipStream.Close();

            importedEmipStream = File.OpenRead(emipPath);
            AssetsFileReader r = new AssetsFileReader(importedEmipStream);
            impEmip.Read(r);

            boxModName.Text = impEmip.modName;
            boxCredits.Text = impEmip.modCreators;
            boxDesc.Text = impEmip.modDescription;

            foreach (InstallerPackageAssetsDesc affectedFile in impEmip.affectedFiles)
            {
                if (!affectedFile.isBundle)
                {
                    string file = Path.GetFullPath(affectedFile.path, rootPath);
                    if (!fileToTvi.ContainsKey(file))
                    {
                        ModMakerTreeFileInfo newFileItem = new ModMakerTreeFileInfo(file, rootPath);
                        filesItems.Add(newFileItem);
                        fileToTvi.Add(file, newFileItem);
                    }

                    ModMakerTreeFileInfo fileItem = fileToTvi[file];

                    foreach (AssetsReplacer replacer in affectedFile.replacers)
                    {
                        AssetID assetId = new AssetID(file, replacer.GetPathID());

                        var obsItems = fileItem.Replacers;
                        obsItems.Add(new ModMakerTreeReplacerInfo(assetId, replacer));
                    }
                }
            }
        }

        private async void BtnOk_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            using var saveFile = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                DefaultExtension = "emip",
                FileTypeChoices = new List<FilePickerFileType>()
                {
                    new FilePickerFileType("UABE Mod Installer Package") { Patterns = new List<string>() { "*.emip" } }
                },
            });
            if (saveFile == null) return;
            if (!saveFile.TryGetUri(out Uri? uri)) return;

            string path = Path.GetFullPath(uri.OriginalString);

            if (path != null && path != string.Empty)
            {
                BuildEmip(path);

                if (importedEmipStream != null && importedEmipStream.CanRead)
                    importedEmipStream.Close();

                Close(true);
            }
        }

        private void BtnCancel_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            Close(false);
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }

    public class ModMakerTreeFileInfo : INotifyPropertyChanged
    {
        public string rootPath;
        public string fullPath;
        public string relPath; //this could probably be a prop but whatever it's already here

        public ObservableCollection<ModMakerTreeReplacerInfo> Replacers { get; }
        public string DisplayText { get => relPath; }

        public event PropertyChangedEventHandler? PropertyChanged;

        public ModMakerTreeFileInfo(string fullPath)
        {
            Replacers = new ObservableCollection<ModMakerTreeReplacerInfo>();
            this.fullPath = fullPath;
            rootPath = "";
            relPath = fullPath;
        }

        public ModMakerTreeFileInfo(string fullPath, string rootPath)
        {
            Replacers = new ObservableCollection<ModMakerTreeReplacerInfo>();
            this.fullPath = fullPath;
            this.rootPath = rootPath;
            this.relPath = "";

            UpdateRootPath(rootPath);
        }

        public void UpdateRootPath(string rootPath)
        {
            this.rootPath = rootPath;

            if (IsPathRootedSafe(rootPath))
                relPath = Path.GetRelativePath(rootPath, fullPath);
            else
                relPath = fullPath;

            Update(nameof(DisplayText));
        }

        private bool IsPathRootedSafe(string path)
        {
            try
            {
                return Path.IsPathRooted(path);
            }
            catch
            {
                return false;
            }
        }

        public void Update(string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class ModMakerTreeReplacerInfo
    {
        public bool isBundle;
        public AssetID assetId;
        public AssetsReplacer assetsReplacer;
        //public BundleReplacer bundleReplacer;

        public string DisplayText { get => ToString(); }

        public ModMakerTreeReplacerInfo(AssetID assetId, AssetsReplacer assetsReplacer)
        {
            isBundle = false;
            this.assetId = assetId;
            this.assetsReplacer = assetsReplacer;
        }

        public override string ToString()
        {
            if (!isBundle)
            {
                if (assetsReplacer is AssetsRemover)
                    return $"Remove path id {assetsReplacer.GetPathID()}";
                else //if (replacer is AssetsReplacerFromMemory || replacer is AssetsReplacerFromStream)
                    return $"Replace path id {assetsReplacer.GetPathID()}";
            }
            else
            {
                //todo
                return "no u";
            }
        }
    }
}
