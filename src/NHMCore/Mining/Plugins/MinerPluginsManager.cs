﻿using NHM.MinerPlugin;
using NHM.MinerPluginLoader;
using NHM.MinerPluginToolkitV1;
using Newtonsoft.Json;
using NHM.Common;
using NHM.MinersDownloader;
using NHMCore.ApplicationState;
using NHMCore.Configs;
using NHMCore.Notifications;
using NHMCore.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using NHMCore.Configs.Data;

namespace NHMCore.Mining.Plugins
{
    public static class MinerPluginsManager
    {
        private static readonly List<IMinerPlugin> _integratedPlugins;
        static MinerPluginsManager()
        {
            // This is just a list of miners that are intergated in the nhm client. usefull when debuging
            _integratedPlugins = new List<IMinerPlugin>
            {
                // __DEV__*          
#if INTEGRATE_BrokenMiner_PLUGIN
                new BrokenMiner.BrokenMinerPlugin(),
#endif
#if INTEGRATE_ExamplePlugin_PLUGIN
                new Example.ExamplePlugin(),
#endif
#if INTEGRATE_FakePlugin_PLUGIN
                new FakePlugin.FakePlugin(),
#endif

// real miners
#if INTEGRATE_GMiner_PLUGIN
                new GMinerPlugin.GMinerPlugin(),
#endif
#if INTEGRATE_NBMiner_PLUGIN
                new NBMiner.NBMinerPlugin(),
#endif
#if INTEGRATE_Phoenix_PLUGIN
                new Phoenix.PhoenixPlugin(),
#endif
#if INTEGRATE_TeamRedMiner_PLUGIN
                new TeamRedMiner.TeamRedMinerPlugin(),
#endif
#if INTEGRATE_TRex_PLUGIN
                new TRex.TRexPlugin(),
#endif
#if INTEGRATE_TTMiner_PLUGIN
                new TTMiner.TTMinerPlugin(),
#endif
#if INTEGRATE_NanoMiner_PLUGIN
                new NanoMiner.NanoMinerPlugin(),
#endif
#if INTEGRATE_WildRig_PLUGIN
                new WildRig.WildRigPlugin(),
#endif
#if INTEGRATE_CryptoDredge_PLUGIN
                new CryptoDredge.CryptoDredgePlugin(),
#endif
#if INTEGRATE_ZEnemy_PLUGIN
                new ZEnemy.ZEnemyPlugin(),
#endif
#if INTEGRATE_LolMiner_PLUGIN
                new LolMiner.LolMinerPlugin(),
#endif
#if INTEGRATE_SRBMiner_PLUGIN
                new SRBMiner.SRBMinerPlugin(),
#endif
#if INTEGRATE_XMRig_PLUGIN
                new XMRig.XMRigPlugin(),
#endif
#if INTEGRATE_MiniZ_PLUGIN
                new MiniZ.MiniZPlugin(),
#endif

#if INTEGRATE_ALL_PLUGINS
                new GMinerPlugin.GMinerPlugin(),
                new NBMiner.NBMinerPlugin(),
                new Phoenix.PhoenixPlugin(),
                new TeamRedMiner.TeamRedMinerPlugin(),
                new TRex.TRexPlugin(),
                new TTMiner.TTMinerPlugin(),
                new NanoMiner.NanoMinerPlugin(),
                new WildRig.WildRigPlugin(),
                new CryptoDredge.CryptoDredgePlugin(),
                new ZEnemy.ZEnemyPlugin(),
                new LolMiner.LolMinerPlugin(),
                new SRBMiner.SRBMinerPlugin(),
                new MiniZ.MiniZPlugin(),
#endif


                // service plugin
                EthlargementIntegratedPlugin.Instance,

                // plugin dependencies
                VC_REDIST_x64_2015_2019_DEPENDENCY_PLUGIN.Instance
            };
            var filteredIntegratedPlugins = _integratedPlugins.Where(p => BlacklistedPlugins.Instance.IsSupported(p.PluginUUID)).ToList();
            foreach (var integratedPlugin in filteredIntegratedPlugins)
            {
                PluginContainer.Create(integratedPlugin);
            }

            _initOnlinePlugins = InitOnlinePlugins();
        }

        private static readonly bool _initOnlinePlugins;

        // API data
        private static List<PluginPackageInfo> OnlinePlugins { get; set; }
        private static Dictionary<string, PluginPackageInfoCR> PluginsPackagesInfosCRs { get; set; } = new Dictionary<string, PluginPackageInfoCR>();

        //public static PluginPackageInfoCR GetPluginPackageInfoCR(string pluginUUID)
        //{
        //    if (PluginsPackagesInfosCRs.ContainsKey(pluginUUID)) return PluginsPackagesInfosCRs[pluginUUID];
        //    return null;
        //}

        public static IEnumerable<PluginPackageInfoCR> RankedPlugins
        {
            get
            {
                return PluginsPackagesInfosCRs
                    .Where(kvp => !_integratedPlugins.Any(p => p.PluginUUID == kvp.Value.PluginUUID))
                    .Select(kvp => kvp.Value)
                    .OrderByDescending(info => info.HasNewerVersion)
                    .ThenByDescending(info => info.OnlineSupportedDeviceCount)
                    .ThenBy(info => info.PluginName);
            }
        }

#region Update miner plugin dlls
        public static async Task CleanupPlugins()
        {
            Action<string> deleteDirOrFile = (string path) => {
                if (Directory.Exists(path)) try { Directory.Delete(path, true); } catch { }
                if (File.Exists(path)) try { File.Delete(path); } catch { }
            };
            Func<string, List<string>> getObsoletePaths = (string dirPath) => {
                var sorted = Directory.GetDirectories(dirPath, "*.*", SearchOption.TopDirectoryOnly).OrderBy(fullPathDir => {
                    var verStr = fullPathDir.Replace(dirPath, "").Replace("\\", "").Trim();
                    return Version.TryParse(verStr, out var ver) ? ver : new Version(1, 0);
                }).ToList();
                sorted.Remove(sorted.LastOrDefault());
                return sorted;
            };
            Logger.Info("MinerPluginsManager", "CleanupPlugins START");
            try
            {
                Logger.Info("MinerPluginsManager", "CleanupPlugins EXECUTE");
                string minerPluginsPath = Paths.MinerPluginsPath();
                var zipPackages = Directory.GetFiles(Paths.RootPath("plugins_packages"), "*.zip", SearchOption.TopDirectoryOnly);
                var installedExternalPackages = Directory.GetDirectories(minerPluginsPath, "*", SearchOption.TopDirectoryOnly);
                foreach (var installedPath in installedExternalPackages)
                {
                    try
                    {
                        var uuid = installedPath.Replace(minerPluginsPath, "").Trim('\\');
                        if (!System.Guid.TryParse(uuid, out var _)) continue;
                        var zipPackage = zipPackages.FirstOrDefault(package => package.Contains(uuid));
                        if (zipPackage == null) continue;
                        // cleanup old bins and dll's
                        var removePackageDirBins = getObsoletePaths(Path.Combine(installedPath, "bins"));
                        foreach (var removeDir in removePackageDirBins) deleteDirOrFile(removeDir);

                        var removePackageDirDlls = getObsoletePaths(Path.Combine(installedPath, "dlls"));
                        foreach (var removeDir in removePackageDirDlls) deleteDirOrFile(removeDir);

                        var installedPackageRootDlls = Directory.GetFiles(installedPath, "*.dll", SearchOption.TopDirectoryOnly);
                        if (ConfigManager.IsVersionChanged || installedPackageRootDlls.Length > 1)
                        {
                            foreach (var dllFile in installedPackageRootDlls) deleteDirOrFile(dllFile);
                            await ArchiveHelpers.ExtractFileAsync(zipPackage, installedPath, null, CancellationToken.None);
                        }
                    }
                    catch (Exception e)
                    {
                        Logger.Error("MinerPluginsManager", $"CleanupPlugins {installedPath} Error: {e.Message}");
                    }
                }
                
            }
            catch (Exception e)
            {
                Logger.Error("MinerPluginsManager", $"CleanupPlugins Error: {e.Message}");
            }
        }

        public static void CheckAndDeleteUnsupportedPlugins()
        {
            try
            {
                foreach (var obsolete in Checkers.ObsoleteMinerPlugins)
                {
                    try
                    {
                        var obsoletePath = Paths.MinerPluginsPath(obsolete);
                        if (Directory.Exists(obsoletePath))
                        {
                            Directory.Delete(obsoletePath, true);
                        }
                    }
                    catch (Exception e)
                    {
                        Logger.Error("CheckAndDeleteUnsupportedPlugins", e.Message);
                    }
                }
            }
            catch (Exception e)
            {
                Logger.Error("CheckAndDeleteUnsupportedPlugins", e.Message);
            }
        }

#endregion Update miner plugin dlls

        public static async Task LoadAndInitMinerPlugins()
        {
            // load dll's and create plugin containers
            var loadedPlugins = MinerPluginHost.LoadPlugins(Paths.MinerPluginsPath()).Where(uuid => BlacklistedPlugins.Instance.IsSupported(uuid));
            foreach (var pluginUUID in loadedPlugins) PluginContainer.Create(MinerPluginHost.MinerPlugin[pluginUUID]);
            // init all containers
            foreach (var plugin in PluginContainer.PluginContainers)
            {
                if (!plugin.IsInitialized)
                {
                    plugin.InitPluginContainer();
                }

                if (plugin.Enabled)
                {
                    plugin.AddAlgorithmsToDevices();
                }
                else
                {
                    plugin.RemoveAlgorithmsFromDevices();
                }
            }
            // cross reference local and online list
            var success = await GetOnlineMinerPlugins();
            if (success) CrossReferenceInstalledWithOnline();
            EthlargementIntegratedPlugin.Instance.ServiceEnabled = MiscSettings.Instance.UseEthlargement && Helpers.IsElevated;
            Logger.Info("MinerPluginsManager", "Finished initialization of miners.");
        }

        public static Task RunninLoops { get; private set; } = null;

        public static void StartLoops(CancellationToken stop)
        {
            RunninLoops = Task.Run(() => {
                var loop1 = MainLoop(stop);
                return Task.WhenAll(loop1);
            });
        }

        private static async Task MainLoop(CancellationToken stop)
        {
            try
            {
                var checkWaitTime = TimeSpan.FromMilliseconds(50);
                Func<bool> isActive = () => !stop.IsCancellationRequested;


                // TODO set this interval somwhere
                // periodically update the plugin list
                var getOnlineMinerPluginsElapsedTimeChecker = new ElapsedTimeChecker(MinerPluginsUpdaterSettings.CheckPluginsInterval, false);


                // TODO for now every minute check
                // TODO debug only we should check plugin updates after we update the miner plugin API
                //var pluginsUpdateElapsedTimeChecker = new ElapsedTimeChecker(TimeSpan.FromSeconds(30), false);

                var restartActiveDevicesPluginsList = new List<string>();

                Logger.Debug("MinerPluginsManager", $"STARTING MAIN LOOP");
                while (isActive())
                {
                    try
                    {
                        if (isActive()) await TaskHelpers.TryDelay(checkWaitTime, stop);

                        if (isActive() && getOnlineMinerPluginsElapsedTimeChecker.CheckAndMarkElapsedTime() && UpdateSettings.Instance.AutoUpdateMinerPlugins)
                        {
                            Logger.Debug("MinerPluginsManager", $"Checking for plugin updates");
                            // TODO Cross refference online plugins
                            var success = await GetOnlineMinerPlugins();
                            if (success)
                            {
                                Logger.Debug("MinerPluginsManager", $"Checking for plugin updates SUCCESS");
                                CrossReferenceInstalledWithOnline();
                                // TODO check settings for plugins updates installs
                                // TODO install online compatible plugins
                                Logger.Debug("MinerPluginsManager", $"Checking plugins to Install/Update");
                                foreach (var packageInfoCR in PluginsPackagesInfosCRs)
                                {
                                    var pluginUUID = packageInfoCR.Key;
                                    // plugin updates cases
                                    var installed = packageInfoCR.Value.Installed;
                                    var supportedAndCompatible = packageInfoCR.Value.CompatibleNHPluginVersion && packageInfoCR.Value.Supported;
                                    var updatesEnabled = packageInfoCR.Value.IsAutoUpdateEnabled;
                                    var canUpdate = supportedAndCompatible && installed && packageInfoCR.Value.HasNewerVersion;
                                    var compatibleNotInstalled = !installed && supportedAndCompatible && false; // disable by default
                                    var isInstalling = MinerPluginInstallTasks.ContainsKey(pluginUUID);

                                    if (updatesEnabled && !isInstalling && (canUpdate || compatibleNotInstalled))
                                    {
                                        Logger.Debug("MinerPluginsManager", $"Main loop Install/Update {packageInfoCR.Key}");

                                        IProgress<Tuple<PluginInstallProgressState, int>> progress = null;
                                        if (_minerPluginInstallTasksProgress.TryGetValue(pluginUUID, out progress))
                                        {
                                            // TODO log no progress
                                        }
                                        _ = DownloadAndInstall(pluginUUID, progress);
                                    }
                                }
                                // check plugins to instal
                            }
                            else
                            {
                                Logger.Debug("MinerPluginsManager", $"Checking for plugin updates FAIL");
                            }
                        }

                        // TODO trigger active device re-evaluation after install/remove/updates are finished
                        if (isActive() && (_minerPluginInstallRemoveStates.Count + restartActiveDevicesPluginsList.Count) > 0)
                        {
                            var finishedKeys = new List<string>();
                            foreach (var kvp in _minerPluginInstallRemoveStates)
                            {
                                if (kvp.Value != PluginInstallRemoveState.Remove && kvp.Value != PluginInstallRemoveState.InstallOrUpdate)
                                {
                                    restartActiveDevicesPluginsList.Add(kvp.Key);
                                    finishedKeys.Add(kvp.Key);
                                }
                                Logger.DebugDelayed("MinerPluginsManager", $"_minerPluginInstallRemoveStates {kvp.Key}-{kvp.Value.ToString()}", TimeSpan.FromSeconds(5));
                            }
                            var allRemoved = true;
                            foreach (var key in finishedKeys)
                            {
                                allRemoved &= _minerPluginInstallRemoveStates.TryRemove(key, out var _);
                            }
                            if (!allRemoved)
                            {
                                Logger.DebugDelayed("MinerPluginsManager", $"_minerPluginInstallRemoveStates allRemoved false", TimeSpan.FromSeconds(5));
                            }
                            if (_minerPluginInstallRemoveStates.Count == 0)
                            {
                                restartActiveDevicesPluginsList.Clear();
                                Logger.DebugDelayed("MinerPluginsManager", $"RESTART ACTIVE DEVICES", TimeSpan.FromSeconds(5));
                                _ = ApplicationStateManager.RestartDevicesState();
                            }
                            else
                            {
                                Logger.DebugDelayed("MinerPluginsManager", $"SKIP!!!!!!!!!!!!!!! RESTART ACTIVE DEVICES", TimeSpan.FromSeconds(5));
                            }
                        }
                    }
                    catch (TaskCanceledException e)
                    {
                        Logger.Info("MinerPluginsManager", $"Main Loop TaskCanceledException {e.Message}");
                        return;
                    }
                    catch (Exception e)
                    {
                        Logger.Error("MinerPluginsManager", $"Main Loop Tick Exception {e.Message}");
                    }
                }
            }
            finally
            {
                Logger.Debug("MinerPluginsManager", $"EXITING MAIN LOOP");
                // cleanup
                var pluginUUIDs = MinerPluginInstallTasks.Keys;
                foreach (var pluginUUID in pluginUUIDs)
                {
                    TryCancelInstall(pluginUUID);
                }
            }            
        }

        public static async Task DevicesCrossReferenceIDsWithMinerIndexes(IStartupLoader loader)
        {
            // get devices
            var baseDevices = AvailableDevices.Devices.Select(dev => dev.BaseDevice);
            var checkPlugins = PluginContainer.PluginContainers
                .Where(p => p.IsCompatible)
                .Where(p => p.Enabled)
                .ToArray();

            if (checkPlugins.Length > 0 && loader != null) {
                loader.SecondaryVisible = true;
                loader.SecondaryTitle = Translations.Tr("Devices Cross Reference");
                loader?.SecondaryProgress?.Report((Translations.Tr("Pending"), 0));
            }
            var pluginDoneCount = 0d;
            foreach (var plugin in checkPlugins)
            {
                
                loader?.SecondaryProgress?.Report((Translations.Tr("Cross Reference {0}", plugin.Name), (int)((pluginDoneCount / checkPlugins.Length) * 100)));
                await plugin.DevicesCrossReference(baseDevices);
                pluginDoneCount += 1;
                loader?.SecondaryProgress?.Report((Translations.Tr("Cross Reference {0}", plugin.Name), (int)((pluginDoneCount / checkPlugins.Length) * 100)));
            }
            if (loader != null)
            {
                loader.SecondaryVisible = false;
            }
        }

        public static async Task DownloadMissingMinersBins(IProgress<(string loadMessageText, int prog)> progress, CancellationToken stop)
        {
            var checkPlugins = PluginContainer.PluginContainers
                .Where(p => p.IsCompatible)
                .Where(p => p.Enabled)
                .ToArray();

            foreach (var plugin in checkPlugins)
            {
                var urls = plugin.GetMinerBinsUrls().ToList();
                var missingFiles = plugin.CheckBinaryPackageMissingFiles();
                var hasMissingFiles = missingFiles.Any();
                var hasUrls = urls.Any();
                if (hasMissingFiles && hasUrls && !plugin.IsBroken)
                {
                    Logger.Info("MinerPluginsManager", $"Downloading missing files for {plugin.PluginUUID}-{plugin.Name}");
                    var downloadProgress = new Progress<int>(perc => progress?.Report((Translations.Tr("Downloading {0} %", $"{plugin.Name} {perc}"), perc)));
                    var unzipProgress = new Progress<int>(perc => progress?.Report((Translations.Tr("Unzipping {0} %", $"{plugin.Name} {perc}"), perc)));
                    await DownloadInternalBins(plugin, urls.ToList(), downloadProgress, unzipProgress, stop);
                    // check if we have missing files after the download 
                    if (plugin.CheckBinaryPackageMissingFiles().Any()) AvailableNotifications.CreateMissingMinerBinsInfo(plugin.Name);
                }
            }
        }

        public static async Task UpdateMinersBins(IProgress<(string loadMessageText, int prog)> progress, CancellationToken stop)
        {
            var checkPlugins = PluginContainer.PluginContainers
                .Where(p => p.IsCompatible)
                .Where(p => p.Enabled)
                .ToArray();

            foreach (var plugin in checkPlugins)
            {
                var urls = plugin.GetMinerBinsUrls();
                var hasUrls = urls.Count() > 0;
                var versionMismatch = plugin.IsVersionMismatch;
                if (versionMismatch && hasUrls && !plugin.IsBroken)
                {
                    Logger.Info("MinerPluginsManager", $"Version mismatch for {plugin.PluginUUID}-{plugin.Name}. Downloading...");
                    var downloadProgress = new Progress<int>(perc => progress?.Report((Translations.Tr("Downloading {0} %", $"{plugin.Name} {perc}"), perc)));
                    var unzipProgress = new Progress<int>(perc => progress?.Report((Translations.Tr("Unzipping {0} %", $"{plugin.Name} {perc}"), perc)));
                    await DownloadInternalBins(plugin, urls.ToList(), downloadProgress, unzipProgress, stop);
                }
            }
        }

        public static List<string> GetMissingMiners()
        {
            var checkPlugins = PluginContainer.PluginContainers
                .Where(p => p.IsCompatible)
                .Where(p => p.Enabled)
                .ToArray();

            var ret = new List<string>();
            foreach (var plugin in checkPlugins)
            {
                ret.AddRange(plugin.CheckBinaryPackageMissingFiles());
            }
            return ret;
        }

        public static bool HasMinerUpdates()
        {
            var checkPlugins = PluginContainer.PluginContainers
                .Where(p => p.IsCompatible)
                .Where(p => p.Enabled)
                .Where(p => p.IsVersionMismatch)
                .ToArray();


            return checkPlugins.Count() > 0;
        }

        public static void RemovePlugin(string pluginUUID, bool crossReferenceInstalledWithOnline = true)
        {
            // assume ok
            var isOk = true;
            try
            {
                _minerPluginInstallRemoveStates.TryAdd(pluginUUID, PluginInstallRemoveState.Remove);
                var deletePath = Path.Combine(Paths.MinerPluginsPath(), pluginUUID);
                MinerPluginHost.MinerPlugin.Remove(pluginUUID);
                var oldPlugins = PluginContainer.PluginContainers.Where(p => p.PluginUUID == pluginUUID).ToArray();
                foreach (var old in oldPlugins)
                {
                    PluginContainer.RemovePluginContainer(old);
                }
                // TODO this remove is probably redundant CHECK
                foreach (var dev in AvailableDevices.Devices)
                {
                    dev.RemovePluginAlgorithms(pluginUUID);
                }

                // remove from cross ref dict
                if (PluginsPackagesInfosCRs.ContainsKey(pluginUUID))
                {
                    PluginsPackagesInfosCRs[pluginUUID].LocalInfo = null;
                    // TODO we might not have any online reference so remove it in this case
                    if (PluginsPackagesInfosCRs[pluginUUID].OnlineInfo == null)
                    {
                        PluginsPackagesInfosCRs.Remove(pluginUUID);
                    }
                }

                if (crossReferenceInstalledWithOnline) CrossReferenceInstalledWithOnline();
                // TODO before deleting you will need to unload the dll
                if (Directory.Exists(deletePath))
                {
                    Directory.Delete(deletePath, true);
                }
            } catch(Exception e)
            {
                isOk = false;
                Logger.Error("MinerPluginsManager", $"Error occured while removing {pluginUUID} plugin: {e.Message}");
            }
            finally
            {
                if (isOk)
                {
                    BlacklistedPlugins.Instance.AddPluginToBlacklist(pluginUUID);
                    _minerPluginInstallRemoveStates.TryUpdate(pluginUUID, PluginInstallRemoveState.RemoveSuccess, PluginInstallRemoveState.Remove);
                }
                else
                {
                    _minerPluginInstallRemoveStates.TryUpdate(pluginUUID, PluginInstallRemoveState.RemoveFailed, PluginInstallRemoveState.Remove);
                }
            }
        }

        public static void CrossReferenceInstalledWithOnline()
        {
            // first go over the installed plugins
            // TODO rename installed to externalInstalledPlugin
            var checkPlugins = PluginContainer.PluginContainers
                //.Where(p => !p.IsIntegrated)
                //.Where(p => p.IsCompatible)
                //.Where(p => p.Enabled)
                .ToArray();
            foreach (var installed in checkPlugins)
            {
                var uuid = installed.PluginUUID;
                var localPluginInfo = new PluginPackageInfo
                {
                    PluginAuthor = installed.Author,
                    PluginName = installed.Name,
                    PluginUUID = uuid,
                    PluginVersion = installed.Version,
                    // other stuff is not inside the plugin
                };
                if (PluginsPackagesInfosCRs.ContainsKey(uuid) == false)
                {
                    PluginsPackagesInfosCRs[uuid] = new PluginPackageInfoCR(uuid);
                }
                PluginsPackagesInfosCRs[uuid].LocalInfo = localPluginInfo;
            }

            // get online list and check what we have and what is online
            if (OnlinePlugins == null) return;

            foreach (var online in OnlinePlugins)
            {
                var uuid = online.PluginUUID;
                if (PluginsPackagesInfosCRs.ContainsKey(uuid) == false)
                {
                    PluginsPackagesInfosCRs[uuid] = new PluginPackageInfoCR(uuid);
                }
                PluginsPackagesInfosCRs[uuid].OnlineInfo = online;
                if (online.SupportedDevicesAlgorithms != null)
                {
                    var supportedDevices = online.SupportedDevicesAlgorithms
                        .Where(kvp => kvp.Value.Count > 0)
                        .Select(kvp => kvp.Key);
                    var devRank = AvailableDevices.Devices
                        .Where(d => supportedDevices.Contains(d.DeviceType.ToString()))
                        .Count();
                    PluginsPackagesInfosCRs[uuid].OnlineSupportedDeviceCount = devRank;
                }
            }

            MinerPluginsManagerState.Instance.RankedPlugins = RankedPlugins.ToList();
        }

        public static List<string> GetPluginUUIDsAndVersionsList()
        {
            var ret = new List<string>();
            var checkPlugins = PluginContainer.PluginContainers
                .Where(p => p.IsCompatible)
                .Where(p => p.Enabled)
                .ToArray();
            foreach (var integrated in checkPlugins)
            {
                ret.Add($"{integrated.PluginUUID}-{integrated.Version.Major}.{integrated.Version.Minor}");
            }
            return ret;
        }


        private class NoKeepAlivesWebClient : WebClient
        {
            protected override WebRequest GetWebRequest(Uri address)
            {
                var request = base.GetWebRequest(address);
                if (request is HttpWebRequest)
                {
                    ((HttpWebRequest)request).KeepAlive = false;
                }

                return request;
            }
        }


        public static async Task<bool> GetOnlineMinerPlugins()
        {
            try
            {
                using (var client = new NoKeepAlivesWebClient())
                {
                    string s = await client.DownloadStringTaskAsync(Links.PluginsJsonApiUrl);
                    //// local fake string
                    //string s = Properties.Resources.pluginJSON;
                    var onlinePlugins = JsonConvert.DeserializeObject<List<PluginPackageInfo>>(s, new JsonSerializerSettings
                    {
                        NullValueHandling = NullValueHandling.Ignore,
                        MissingMemberHandling = MissingMemberHandling.Ignore,
                        Culture = CultureInfo.InvariantCulture
                    });
                    OnlinePlugins = onlinePlugins;
                }

                return true;
            } catch(Exception e)
            {
                Logger.Error("MinerPluginsManager", $"Error occured while getting online miner plugins: {e.Message}");
            }
            return _initOnlinePlugins;
        }

        public static bool InitOnlinePlugins()
        {
            try
            {
                string s = File.ReadAllText(Paths.RootPath("plugins_packages", "update.json"));

                var onlinePlugins = JsonConvert.DeserializeObject<List<PluginPackageInfo>>(s, new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Ignore,
                    MissingMemberHandling = MissingMemberHandling.Ignore,
                    Culture = CultureInfo.InvariantCulture
                });
                OnlinePlugins = onlinePlugins;
                return true;
            }
            catch (Exception e)
            {
                Logger.Error("MinerPluginsManager", $"Error occured while initing online miner plugins: {e.Message}");
            }
            return false;
        }

        public static PluginContainer GetPluginWithUuid(string pluginUuid)
        {
            var ret = PluginContainer.PluginContainers.FirstOrDefault(p => p.PluginUUID == pluginUuid);
            return ret;
        }

#region DownloadingInstalling

        public static async Task DownloadInternalBins(PluginContainer pluginContainer, List<string> urls, IProgress<int> downloadProgress, IProgress<int> unzipProgress, CancellationToken stop)
        {
            var pluginUUID = pluginContainer.PluginUUID;
            var ver = pluginContainer.Version;
            var installingPluginBinsPath = Path.Combine(Paths.MinerPluginsPath(), pluginUUID, "bins", $"{ver.Major}.{ver.Minor}");
            try
            {
                if (Directory.Exists(installingPluginBinsPath)) Directory.Delete(installingPluginBinsPath, true);
                //downloadAndInstallUpdate("Starting");
                Directory.CreateDirectory(installingPluginBinsPath);
                var installedBins = false;
                foreach (var url in urls)
                {
                    // download plugin dll
                    var downloadMinerBinsResult = await MinersDownloadManager.DownloadFileAsync(url, installingPluginBinsPath, "miner_bins", downloadProgress, stop);
                    var binsPackageDownloaded = downloadMinerBinsResult.downloadedFilePath;
                    var downloadMinerBinsOK = downloadMinerBinsResult.success;
                    if (!downloadMinerBinsOK || stop.IsCancellationRequested) return;
                    // unzip 
                    var binsUnzipPath = installingPluginBinsPath; // Path.Combine(installingPluginPath, "bins");
                    var unzipMinerBinsOK = await ArchiveHelpers.ExtractFileAsync(binsPackageDownloaded, binsUnzipPath, unzipProgress, stop);
                    if (stop.IsCancellationRequested) return;
                    if (unzipMinerBinsOK)
                    {
                        installedBins = true;
                        File.Delete(binsPackageDownloaded);
                        break;
                    }   
                }
                if (!installedBins)
                {
                    Logger.Error("MinerPluginsManager", $"Miners bins of {pluginUUID} not installed");
                }

                //clear old bins
                clearOldPluginBins(Path.Combine(Paths.MinerPluginsPath(), pluginUUID, "bins"));
            }
            catch (Exception e)
            {
                Logger.Error("MinerPluginsManager", $"Installation of {pluginUUID} failed: ${e.Message}");
            }
        }

        private static ConcurrentDictionary<string, MinerPluginInstallTask> MinerPluginInstallTasks = new ConcurrentDictionary<string, MinerPluginInstallTask>();
        // with WPF we have only one Progress 
        private static ConcurrentDictionary<string, IProgress<Tuple<PluginInstallProgressState, int>>> _minerPluginInstallTasksProgress = new ConcurrentDictionary<string, IProgress<Tuple<PluginInstallProgressState, int>>>();
        
        private static ConcurrentDictionary<string, PluginInstallRemoveState> _minerPluginInstallRemoveStates = new ConcurrentDictionary<string, PluginInstallRemoveState>();

        public static void InstallAddProgress(string pluginUUID, IProgress<Tuple<PluginInstallProgressState, int>> progress)
        {
            if (MinerPluginInstallTasks.TryGetValue(pluginUUID, out var installTask))
            {
                installTask.AddProgress(progress);
            }
            // 
            if (_minerPluginInstallTasksProgress.ContainsKey(pluginUUID) == false)
            {
                _minerPluginInstallTasksProgress.TryAdd(pluginUUID, progress);
            }
        }

        public static void InstallRemoveProgress(string pluginUUID, IProgress<Tuple<PluginInstallProgressState, int>> progress)
        {
            if (MinerPluginInstallTasks.TryGetValue(pluginUUID, out var installTask))
            {
                installTask.RemoveProgress(progress);
            }
            if (_minerPluginInstallTasksProgress.TryRemove(pluginUUID, out var _) == false)
            {
                // log error
            }
        }

        public static void TryCancelInstall(string pluginUUID)
        {
            if (MinerPluginInstallTasks.TryRemove(pluginUUID, out var installTask))
            {
                installTask.TryCancelInstall();
            }
        }

        public static async Task DownloadAndInstall(string pluginUUID, IProgress<Tuple<PluginInstallProgressState, int>> progress)
        {
            // TODO skip install if alredy in progress
            var addSuccess = false;
            _minerPluginInstallRemoveStates.TryAdd(pluginUUID, PluginInstallRemoveState.InstallOrUpdate);
            var installResult = PluginInstallProgressState.Canceled;
            using (var minerInstall = new MinerPluginInstallTask())
            {
                try
                {
                    var pluginPackageInfo = PluginsPackagesInfosCRs[pluginUUID];
                    addSuccess = MinerPluginInstallTasks.TryAdd(pluginUUID, minerInstall);
                    if (progress != null)
                    {
                        progress?.Report(Tuple.Create(PluginInstallProgressState.Pending, 0));
                        minerInstall.AddProgress(progress);
                    }
                    installResult = await DownloadAndInstall(pluginPackageInfo, minerInstall, minerInstall.CancelInstallToken);
                }
                finally
                {
                    if (addSuccess)
                    {
                        BlacklistedPlugins.Instance.RemovePluginFromBlacklist(pluginUUID);
                        MinerPluginInstallTasks.TryRemove(pluginUUID, out var _);
                    }
                    if (installResult == PluginInstallProgressState.Success)
                    {
                        AvailableNotifications.CreatePluginUpdateInfo(PluginsPackagesInfosCRs[pluginUUID].PluginName, true);
                        _minerPluginInstallRemoveStates.TryUpdate(pluginUUID, PluginInstallRemoveState.InstallOrUpdateSuccess, PluginInstallRemoveState.InstallOrUpdate);
                    }
                    else
                    {
                        AvailableNotifications.CreatePluginUpdateInfo(PluginsPackagesInfosCRs[pluginUUID].PluginName, false);
                        _minerPluginInstallRemoveStates.TryUpdate(pluginUUID, PluginInstallRemoveState.InstallOrUpdateFailed, PluginInstallRemoveState.InstallOrUpdate);
                    }
                }
            }
        }



        internal static async Task<PluginInstallProgressState> DownloadAndInstall(PluginPackageInfoCR plugin, IProgress<Tuple<PluginInstallProgressState, int>> progress, CancellationToken stop)
        {
            var downloadPluginProgressChangedEventHandler = new Progress<int>(perc => progress?.Report(Tuple.Create(PluginInstallProgressState.DownloadingPlugin, perc)));
            var zipProgressPluginChangedEventHandler = new Progress<int>(perc => progress?.Report(Tuple.Create(PluginInstallProgressState.ExtractingPlugin, perc)));
            var downloadMinerProgressChangedEventHandler = new Progress<int>(perc => progress?.Report(Tuple.Create(PluginInstallProgressState.DownloadingMiner, perc)));
            var zipProgressMinerChangedEventHandler = new Progress<int>(perc => progress?.Report(Tuple.Create(PluginInstallProgressState.ExtractingMiner, perc)));

            var finalState = PluginInstallProgressState.Pending;
            var versionStr = $"{plugin.OnlineInfo.PluginVersion.Major}.{plugin.OnlineInfo.PluginVersion.Minor}";
            var pluginRootPath = Path.Combine(Paths.MinerPluginsPath(), plugin.PluginUUID);
            var installDllPath = Path.Combine(pluginRootPath, "dlls", versionStr);
            var installBinsPath = Path.Combine(pluginRootPath, "bins", versionStr);       
            try
            {
                if (Directory.Exists(installDllPath)) Directory.Delete(installDllPath, true);
                Directory.CreateDirectory(installDllPath);
                if (Directory.Exists(installBinsPath)) Directory.Delete(installBinsPath, true);
                Directory.CreateDirectory(installBinsPath);

                //clear old bins
                clearOldPluginBins(Path.Combine(pluginRootPath, "bins"));

                // download plugin dll
                progress?.Report(Tuple.Create(PluginInstallProgressState.PendingDownloadingPlugin, 0));
                var downloadPluginResult = await MinersDownloadManager.DownloadFileAsync(plugin.PluginPackageURL, installDllPath, "plugin", downloadPluginProgressChangedEventHandler, stop);
                var pluginPackageDownloaded = downloadPluginResult.downloadedFilePath;
                var downloadPluginOK = downloadPluginResult.success;
                if (!downloadPluginOK || stop.IsCancellationRequested)
                {
                    finalState = stop.IsCancellationRequested ? PluginInstallProgressState.Canceled : PluginInstallProgressState.FailedDownloadingPlugin;
                    return finalState;
                }
                // unzip 
                progress?.Report(Tuple.Create(PluginInstallProgressState.PendingExtractingPlugin, 0));
                var unzipPluginOK = await ArchiveHelpers.ExtractFileAsync(pluginPackageDownloaded, installDllPath, zipProgressPluginChangedEventHandler, stop);
                if (!unzipPluginOK || stop.IsCancellationRequested)
                {
                    finalState = stop.IsCancellationRequested ? PluginInstallProgressState.Canceled : PluginInstallProgressState.FailedExtractingPlugin;
                    return finalState;
                }
                File.Delete(pluginPackageDownloaded);

                // download plugin binary
                progress?.Report(Tuple.Create(PluginInstallProgressState.PendingDownloadingMiner, 0));
                var downloadMinerBinsResult = await MinersDownloadManager.DownloadFileAsync(plugin.MinerPackageURL, installBinsPath, "miner_bins", downloadMinerProgressChangedEventHandler, stop);
                var binsPackageDownloaded = downloadMinerBinsResult.downloadedFilePath;
                var downloadMinerBinsOK = downloadMinerBinsResult.success;
                if (!downloadMinerBinsOK || stop.IsCancellationRequested)
                {
                    finalState = stop.IsCancellationRequested ? PluginInstallProgressState.Canceled : PluginInstallProgressState.FailedDownloadingMiner;
                    return finalState;
                }
                // unzip 
                progress?.Report(Tuple.Create(PluginInstallProgressState.PendingExtractingMiner, 0));
                var unzipMinerBinsOK = await ArchiveHelpers.ExtractFileAsync(binsPackageDownloaded, installBinsPath, zipProgressMinerChangedEventHandler, stop);
                if (!unzipMinerBinsOK || stop.IsCancellationRequested)
                {
                    finalState = stop.IsCancellationRequested ? PluginInstallProgressState.Canceled : PluginInstallProgressState.FailedExtractingMiner;
                    return finalState;
                }
                File.Delete(binsPackageDownloaded);

                // TODO from here on add the failed plugin load state and success state
                var loadedPlugins = MinerPluginHost.LoadPlugin(installDllPath);
                if (loadedPlugins.Count() == 0)
                {
                    //downloadAndInstallUpdate($"Loaded ZERO PLUGINS");
                    finalState = stop.IsCancellationRequested ? PluginInstallProgressState.Canceled : PluginInstallProgressState.FailedPluginLoad;
                    Directory.Delete(installDllPath, true);
                    return finalState;
                }

                //downloadAndInstallUpdate("Checking old plugin");
                //downloadAndInstallUpdate($"Loaded {loadedPlugins} PLUGIN");
                // add or update plugins
                foreach (var pluginUUID in loadedPlugins)
                {
                    var newExternalPlugin = MinerPluginHost.MinerPlugin[pluginUUID];
                    // remove old
                    var oldPlugins = PluginContainer.PluginContainers.Where(p => p.PluginUUID == pluginUUID).ToArray();
                    foreach (var old in oldPlugins)
                    {
                        PluginContainer.RemovePluginContainer(old);
                    }
                    var newPlugin = PluginContainer.Create(newExternalPlugin);
                    // TODO/TESTING scope for our fake plugins
                    try
                    {
                        var newPluginDllSettings = Directory.GetFiles(installDllPath, "*.json");
                        foreach (var jsonFile in newPluginDllSettings)
                        {
                            var name = Path.GetFileName(jsonFile);
                            var installJSONFilePath = Path.Combine(pluginRootPath, name);
                            File.Copy(jsonFile, installJSONFilePath, true);
                        }
                    }
                    catch { }
                    var success = newPlugin.InitPluginContainer();
                    // TODO after add or remove plugins we should clean up the device settings
                    if (success)
                    {
                        var oldInstalledDlls = Directory.GetFiles(pluginRootPath, "*.dll");
                        foreach (var oldDll in oldInstalledDlls)
                        {
                            File.Delete(oldDll);
                        }
                        var newDllPath = Directory.GetFiles(installDllPath).FirstOrDefault();
                        var name = Path.GetFileNameWithoutExtension(newDllPath);
                        var newVerStr = $"{newPlugin.Version.Major}.{newPlugin.Version.Minor}";
                        var installedDllPath = Path.Combine(pluginRootPath, $"{name}.dll");
                        File.Copy(newDllPath, installedDllPath);

                        newPlugin.AddAlgorithmsToDevices();
                        await newPlugin.DevicesCrossReference(AvailableDevices.Devices.Select(d => d.BaseDevice));
                        finalState = stop.IsCancellationRequested ? PluginInstallProgressState.Canceled : PluginInstallProgressState.Success;
                    }
                    else
                    {
                        finalState = stop.IsCancellationRequested ? PluginInstallProgressState.Canceled : PluginInstallProgressState.FailedPluginInit;
                        // TODO mark that this plugin wasn't loaded
                        Logger.Error("MinerPluginsManager", $"DownloadAndInstall unable to init and install {pluginUUID}");
                    }
                }
                // cross reference local and online list
                CrossReferenceInstalledWithOnline();
            }
            catch (Exception e)
            {
                Logger.Error("MinerPluginsManager", $"Installation of {plugin.PluginName}_{plugin.PluginVersion}_{plugin.PluginUUID} failed: {e.Message}");
                //downloadAndInstallUpdate();
                finalState = stop.IsCancellationRequested ? PluginInstallProgressState.Canceled : PluginInstallProgressState.FailedUnknown;
            }
            finally
            {
                progress?.Report(Tuple.Create(finalState, 0));
            }
            return finalState;
        }

        private static void clearOldPluginBins(string pluginBinsPath)
        {
            try
            {
                //keep only 3 versions
                var numOfKeepVersions = 3;
                var installedVersions = new DirectoryInfo(pluginBinsPath).GetDirectories("*", SearchOption.AllDirectories).ToList();
                if (installedVersions.Count() > numOfKeepVersions)
                {

                    //parse versions
                    var versionDic = new Dictionary<Version, string>();
                    foreach (var dir in installedVersions)
                    {
                        var dirName = dir.Name;
                        var version = Version.Parse(dirName);
                        versionDic.Add(version, dir.FullName);
                    }

                    //get old versions
                    var dirsToDelete = new List<string>();
                    int counter = 0;
                    foreach (var nek in versionDic.OrderByDescending(key => key.Value))
                    {
                        counter++;
                        if (counter > numOfKeepVersions)
                        {
                            dirsToDelete.Add(nek.Value);
                        }
                    }

                    foreach (var path in dirsToDelete)
                    {
                        Directory.Delete(path, true);
                    }
                }
            }
            catch(Exception ex)
            {
                Logger.Error("MinerPluginsManager", $"Clearing of old plugin bins failed: {ex.Message}");
            }            
        }
#endregion DownloadingInstalling
    }
}
