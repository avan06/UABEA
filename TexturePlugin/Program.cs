﻿using AssetsTools.NET;
using AssetsTools.NET.Extra;
using AssetsTools.NET.Texture;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using MessageBox.Avalonia.Enums;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UABEAvalonia;
using UABEAvalonia.Plugins;

namespace TexturePlugin
{
    public static class TextureHelper
    {
        public static AssetTypeValueField GetByteArrayTexture(AssetWorkspace workspace, AssetContainer tex)
        {
            AssetTypeTemplateField textureTemp = workspace.GetTemplateField(tex);
            AssetTypeTemplateField image_data = textureTemp.Children.FirstOrDefault(f => f.Name == "image data");
            if (image_data == null)
                return null;
            image_data.ValueType = AssetValueType.ByteArray;

            AssetTypeTemplateField m_PlatformBlob = textureTemp.Children.FirstOrDefault(f => f.Name == "m_PlatformBlob");
            if (m_PlatformBlob != null)
            {
                AssetTypeTemplateField m_PlatformBlob_Array = m_PlatformBlob.Children[0];
                m_PlatformBlob_Array.ValueType = AssetValueType.ByteArray;
            }

            AssetTypeValueField baseField = textureTemp.MakeValue(tex.FileReader, tex.FilePosition);
            return baseField;
        }

        public static byte[] GetRawTextureBytes(TextureFile texFile, AssetsFileInstance inst)
        {
            string rootPath = Path.GetDirectoryName(inst.path);
            if (texFile.m_StreamData.size != 0 && texFile.m_StreamData.path != string.Empty)
            {
                string fixedStreamPath = texFile.m_StreamData.path;
                if (!Path.IsPathRooted(fixedStreamPath) && rootPath != null)
                {
                    fixedStreamPath = Path.Combine(rootPath, fixedStreamPath);
                }
                if (File.Exists(fixedStreamPath))
                {
                    Stream stream = File.OpenRead(fixedStreamPath);
                    stream.Position = (long)texFile.m_StreamData.offset;
                    texFile.pictureData = new byte[texFile.m_StreamData.size];
                    stream.Read(texFile.pictureData, 0, (int)texFile.m_StreamData.size);
                }
                else
                {
                    return null;
                }
            }
            return texFile.pictureData;
        }

        public static byte[] GetPlatformBlob(AssetTypeValueField texBaseField)
        {
            AssetTypeValueField m_PlatformBlob = texBaseField["m_PlatformBlob"];
            byte[] platformBlob = null;
            if (!m_PlatformBlob.IsDummy)
            {
                platformBlob = m_PlatformBlob["Array"].AsByteArray;
            }
            return platformBlob;
        }
    }

    public class ImportTextureOption : UABEAPluginOption
    {
        public bool SelectionValidForPlugin(AssetsManager am, UABEAPluginAction action, List<AssetContainer> selection, out string name)
        {
            name = "Batch import textures";

            if (action != UABEAPluginAction.Import)
                return false;

            if (selection.Count <= 1)
                return false;

            int classId = am.ClassDatabase.FindAssetClassByName("Texture2D").ClassId;

            foreach (AssetContainer cont in selection)
            {
                if (cont.ClassId != classId)
                    return false;
            }
            return true;
        }

        private async Task<bool> ImportTextures(Window win, List<ImportBatchInfo> batchInfos)
        {
            StringBuilder errorBuilder = new StringBuilder();

            foreach (ImportBatchInfo batchInfo in batchInfos)
            {
                AssetContainer cont = batchInfo.cont;

                string errorAssetName = $"{Path.GetFileName(cont.FileInstance.path)}/{cont.PathId}";
                string selectedFilePath = batchInfo.importFile;

                if (!cont.HasValueField)
                    continue;

                AssetTypeValueField baseField = cont.BaseValueField;
                TextureFormat fmt = (TextureFormat)baseField["m_TextureFormat"].AsInt;

                byte[] platformBlob = TextureHelper.GetPlatformBlob(baseField);
                uint platform = cont.FileInstance.file.Metadata.TargetPlatform;

                byte[] encImageBytes = TextureImportExport.Import(selectedFilePath, fmt, out int width, out int height, platform, platformBlob);

                if (encImageBytes == null)
                {
                    errorBuilder.AppendLine($"[{errorAssetName}]: Failed to encode texture format {fmt}");
                    continue;
                }

                AssetTypeValueField m_StreamData = baseField["m_StreamData"];
                m_StreamData["offset"].AsInt = 0;
                m_StreamData["size"].AsInt = 0;
                m_StreamData["path"].AsString = "";

                if (!baseField["m_MipCount"].IsDummy)
                    baseField["m_MipCount"].AsInt = 1;

                baseField["m_TextureFormat"].AsInt = (int)fmt;
                baseField["m_CompleteImageSize"].AsInt = encImageBytes.Length;

                baseField["m_Width"].AsInt = width;
                baseField["m_Height"].AsInt = height;

                AssetTypeValueField image_data = baseField["image data"];
                image_data.Value.ValueType = AssetValueType.ByteArray;
                image_data.TemplateField.ValueType = AssetValueType.ByteArray;
                image_data.AsByteArray = encImageBytes;
            }

            if (errorBuilder.Length > 0)
            {
                string[] firstLines = errorBuilder.ToString().Split('\n').Take(20).ToArray();
                string firstLinesStr = string.Join('\n', firstLines);
                await MessageBoxUtil.ShowDialog(win, "Some errors occurred while exporting", firstLinesStr);
            }

            return true;
        }

        public async Task<bool> ExecutePlugin(Window win, AssetWorkspace workspace, List<AssetContainer> selection)
        {
            for (int i = 0; i < selection.Count; i++)
            {
                selection[i] = new AssetContainer(selection[i], TextureHelper.GetByteArrayTexture(workspace, selection[i]));
            }

            var openDir = await win.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select import directory"
            });

            if (openDir == null || openDir.Count <= 0) return false;
            if (!openDir[0].TryGetUri(out Uri? uri) || uri == null) return false;

            string dir = Path.GetFullPath(uri.OriginalString);

            List<string> extensions = new List<string>() { "png", "tga" };

            ImportBatch dialog = new ImportBatch(workspace, selection, dir, extensions);
            List<ImportBatchInfo> batchInfos = await dialog.ShowDialog<List<ImportBatchInfo>>(win);
            if (batchInfos == null)
            {
                return false;
            }

            bool success = await ImportTextures(win, batchInfos);
            if (success)
            {
                foreach (AssetContainer cont in selection)
                {
                    if (batchInfos.Where(x => x.pathId == cont.PathId).Count() == 0)
                    {
                        continue;
                    }
                    byte[] savedAsset = cont.BaseValueField.WriteToByteArray();

                    var replacer = new AssetsReplacerFromMemory(
                        cont.PathId, cont.ClassId, cont.MonoId, savedAsset);

                    workspace.AddReplacer(cont.FileInstance, replacer, new MemoryStream(savedAsset));
                }
                return true;
            }

            return false;
        }
    }

    public class ExportTextureOption : UABEAPluginOption
    {
        public bool SelectionValidForPlugin(AssetsManager am, UABEAPluginAction action, List<AssetContainer> selection, out string name)
        {
            if (selection.Count > 1)
                name = "Batch export textures";
            else
                name = "Export texture";

            if (action != UABEAPluginAction.Export)
                return false;

            int classId = am.ClassDatabase.FindAssetClassByName("Texture2D").ClassId;

            foreach (AssetContainer cont in selection)
            {
                if (cont.ClassId != classId)
                    return false;
            }
            return true;
        }

        public async Task<bool> ExecutePlugin(Window win, AssetWorkspace workspace, List<AssetContainer> selection)
        {
            if (selection.Count > 1)
                return await BatchExport(win, workspace, selection);
            else
                return await SingleExport(win, workspace, selection);
        }

        private bool GetResSTexture(TextureFile texFile, AssetContainer cont)
        {
            TextureFile.StreamingInfo streamInfo = texFile.m_StreamData;
            if (streamInfo.path != null && streamInfo.path != "" && cont.FileInstance.parentBundle != null)
            {
                //some versions apparently don't use archive:/
                string searchPath = streamInfo.path;
                if (searchPath.StartsWith("archive:/"))
                    searchPath = searchPath.Substring(9);

                searchPath = Path.GetFileName(searchPath);

                AssetBundleFile bundle = cont.FileInstance.parentBundle.file;

                AssetsFileReader reader = bundle.DataReader;
                AssetBundleDirectoryInfo[] dirInf = bundle.BlockAndDirInfo.DirectoryInfos;
                for (int i = 0; i < dirInf.Length; i++)
                {
                    AssetBundleDirectoryInfo info = dirInf[i];
                    if (info.Name == searchPath)
                    {
                        reader.Position = info.Offset + (long)streamInfo.offset;
                        texFile.pictureData = reader.ReadBytes((int)streamInfo.size);
                        texFile.m_StreamData.offset = 0;
                        texFile.m_StreamData.size = 0;
                        texFile.m_StreamData.path = "";
                        return true;
                    }
                }
                return false;
            }
            else
            {
                return true;
            }
        }

        public async Task<bool> BatchExport(Window win, AssetWorkspace workspace, List<AssetContainer> selection)
        {
            for (int i = 0; i < selection.Count; i++)
            {
                selection[i] = new AssetContainer(selection[i], TextureHelper.GetByteArrayTexture(workspace, selection[i]));
            }

            ExportBatchChooseType dialog = new ExportBatchChooseType();
            string fileType = await dialog.ShowDialog<string>(win);

            if (fileType != null && fileType != string.Empty)
            {
                var openDir = await win.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = "Select export directory"
                });

                if (openDir == null || openDir.Count <= 0) return false;
                if (!openDir[0].TryGetUri(out Uri? uri) || uri == null) return false;

                string dir = Path.GetFullPath(uri.OriginalString);

                StringBuilder errorBuilder = new StringBuilder();

                foreach (AssetContainer cont in selection)
                {
                    string errorAssetName = $"{Path.GetFileName(cont.FileInstance.path)}/{cont.PathId}";

                    AssetTypeValueField texBaseField = cont.BaseValueField;
                    TextureFile texFile = TextureFile.ReadTextureFile(texBaseField);

                    //0x0 texture, usually called like Font Texture or smth
                    if (texFile.m_Width == 0 && texFile.m_Height == 0)
                        continue;

                    string assetName = Extensions.ReplaceInvalidPathChars(texFile.m_Name);
                    string file = Path.Combine(dir, $"{assetName}-{Path.GetFileName(cont.FileInstance.path)}-{cont.PathId}.{fileType.ToLower()}");

                    //bundle resS
                    if (!GetResSTexture(texFile, cont))
                    {
                        string resSName = Path.GetFileName(texFile.m_StreamData.path);
                        errorBuilder.AppendLine($"[{errorAssetName}]: resS was detected but {resSName} was not found in bundle");
                        continue;
                    }

                    byte[] data = TextureHelper.GetRawTextureBytes(texFile, cont.FileInstance);

                    if (data == null)
                    {
                        string resSName = Path.GetFileName(texFile.m_StreamData.path);
                        errorBuilder.AppendLine($"[{errorAssetName}]: resS was detected but {resSName} was not found on disk");
                        continue;
                    }

                    byte[] platformBlob = TextureHelper.GetPlatformBlob(texBaseField);
                    uint platform = cont.FileInstance.file.Metadata.TargetPlatform;

                    bool success = TextureImportExport.Export(data, file, texFile.m_Width, texFile.m_Height, (TextureFormat)texFile.m_TextureFormat, platform, platformBlob);
                    if (!success)
                    {
                        string texFormat = ((TextureFormat)texFile.m_TextureFormat).ToString();
                        errorBuilder.AppendLine($"[{errorAssetName}]: Failed to decode texture format {texFormat}");
                        continue;
                    }
                }

                if (errorBuilder.Length > 0)
                {
                    string[] firstLines = errorBuilder.ToString().Split('\n').Take(20).ToArray();
                    string firstLinesStr = string.Join('\n', firstLines);
                    await MessageBoxUtil.ShowDialog(win, "Some errors occurred while exporting", firstLinesStr);
                }

                return true;
            }
            return false;
        }
        public async Task<bool> SingleExport(Window win, AssetWorkspace workspace, List<AssetContainer> selection)
        {
            AssetContainer cont = selection[0];

            AssetTypeValueField texBaseField = TextureHelper.GetByteArrayTexture(workspace, cont);
            TextureFile texFile = TextureFile.ReadTextureFile(texBaseField);

            // 0x0 texture, usually called like Font Texture or smth
            if (texFile.m_Width == 0 && texFile.m_Height == 0)
            {
                await MessageBoxUtil.ShowDialog(win, "Error", $"Texture size is 0x0. Texture cannot be exported.");
                return false;
            }

            string assetName = Extensions.ReplaceInvalidPathChars(texFile.m_Name);

            using var saveFile = await win.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save texture",
                SuggestedFileName = $"{assetName}-{Path.GetFileName(cont.FileInstance.path)}-{cont.PathId}",
                DefaultExtension = "png",
                FileTypeChoices = new List<FilePickerFileType>()
                {
                    new FilePickerFileType("PNG file") { Patterns = new List<string>() { "*.png" } },
                    new FilePickerFileType("TGA file") { Patterns = new List<string>() { "*.tga" } }
                },
            });
            if (saveFile == null) return false;
            if (!saveFile.TryGetUri(out Uri? uri)) return false;

            string file = Path.GetFullPath(uri.OriginalString);

            string errorAssetName = $"{Path.GetFileName(cont.FileInstance.path)}/{cont.PathId}";

            //bundle resS
            if (!GetResSTexture(texFile, cont))
            {
                string resSName = Path.GetFileName(texFile.m_StreamData.path);
                await MessageBoxUtil.ShowDialog(win, "Error", $"[{errorAssetName}]: resS was detected but {resSName} was not found in bundle");
                return false;
            }

            byte[] data = TextureHelper.GetRawTextureBytes(texFile, cont.FileInstance);

            if (data == null)
            {
                string resSName = Path.GetFileName(texFile.m_StreamData.path);
                await MessageBoxUtil.ShowDialog(win, "Error", $"[{errorAssetName}]: resS was detected but {resSName} was not found on disk");
                return false;
            }

            byte[] platformBlob = TextureHelper.GetPlatformBlob(texBaseField);
            uint platform = cont.FileInstance.file.Metadata.TargetPlatform;

            bool success = TextureImportExport.Export(data, file, texFile.m_Width, texFile.m_Height, (TextureFormat)texFile.m_TextureFormat, platform, platformBlob);
            if (!success)
            {
                string texFormat = ((TextureFormat)texFile.m_TextureFormat).ToString();
                await MessageBoxUtil.ShowDialog(win, "Error", $"[{errorAssetName}]: Failed to decode texture format {texFormat}");
            }
            return success;
        }
    }

    public class EditTextureOption : UABEAPluginOption
    {
        public bool SelectionValidForPlugin(AssetsManager am, UABEAPluginAction action, List<AssetContainer> selection, out string name)
        {
            name = "Edit texture";

            if (action != UABEAPluginAction.Import)
                return false;

            if (selection.Count != 1)
                return false;

            int classId = am.ClassDatabase.FindAssetClassByName("Texture2D").ClassId;

            foreach (AssetContainer cont in selection)
            {
                if (cont.ClassId != classId)
                    return false;
            }
            return true;
        }

        public async Task<bool> ExecutePlugin(Window win, AssetWorkspace workspace, List<AssetContainer> selection)
        {
            AssetContainer cont = selection[0];
            uint platform = cont.FileInstance.file.Metadata.TargetPlatform;

            AssetTypeValueField texBaseField = TextureHelper.GetByteArrayTexture(workspace, cont);
            TextureFile texFile = TextureFile.ReadTextureFile(texBaseField);
            EditDialog dialog = new EditDialog(texFile.m_Name, texFile, texBaseField, platform);
            bool saved = await dialog.ShowDialog<bool>(win);
            if (saved)
            {
                byte[] savedAsset = texBaseField.WriteToByteArray();

                var replacer = new AssetsReplacerFromMemory(
                    cont.PathId, cont.ClassId, cont.MonoId, savedAsset);

                workspace.AddReplacer(cont.FileInstance, replacer, new MemoryStream(savedAsset));
                return true;
            }
            return false;
        }
    }

    public class TexturePlugin : UABEAPlugin
    {
        public PluginInfo Init()
        {
            PluginInfo info = new PluginInfo();
            info.name = "Texture Import/Export";

            info.options = new List<UABEAPluginOption>();
            info.options.Add(new ImportTextureOption());
            info.options.Add(new ExportTextureOption());
            info.options.Add(new EditTextureOption());
            return info;
        }
    }
}
