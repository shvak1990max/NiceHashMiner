using NHM.MinerPluginToolkitV1.Configs;
using NHM.Common;
using NHM.Common.Enums;
using NHMCore.Configs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NHMCore.Switching
{
    /// <summary>
    /// Maintains global registry of NH SMA
    /// </summary>
    public static class NHSmaData
    {
        private const string Tag = "NHSMAData";
        private static string CachedFile => Paths.InternalsPath("cached_sma.json");

        /// <summary>
        /// True iff there has been at least one SMA update
        /// </summary>
        public static bool _hasData = false;
        public static bool HasData
        {
            get
            {
                if (BuildOptions.FORCE_MINING || BuildOptions.FORCE_PROFITABLE) return true;
                return _hasData;
            }
            private set
            {
                _hasData = value;
            }
        }

        // private static Dictionary<AlgorithmType, List<double>> _recentPaying;

        // Global list of SMA data, should be accessed with a lock since callbacks/timers update it
        private static Dictionary<AlgorithmType, double> _currentPayingRates;
        // Global list of stable algorithms, should be accessed with a lock
        private static HashSet<AlgorithmType> _stableAlgorithms;

        static NHSmaData()
        {
            _currentPayingRates = new Dictionary<AlgorithmType, double>();
            _stableAlgorithms = new HashSet<AlgorithmType>((new List<int>() { 40, 20, 44, 39, 50, 36, 14, 52, 53, 55, 48, 51, 8, 54, 24, 47, 43 }).Select(id => (AlgorithmType)id));

            var cacheDict = InternalConfigs.ReadFileSettings<Dictionary<AlgorithmType, double>>(CachedFile);

            // _recentPaying = new Dictionary<AlgorithmType, List<double>>();
            foreach (AlgorithmType algo in Enum.GetValues(typeof(AlgorithmType)))
            {
                if (algo >= 0)
                {
                    var paying = 0d;

                    if (cacheDict?.TryGetValue(algo, out paying) ?? false)
                        HasData = true;

                    if (BuildOptions.FORCE_MINING || BuildOptions.FORCE_PROFITABLE)
                    {
                        paying = 10000;
                    }
                    _currentPayingRates[algo] = paying;
                }
            }
            var lastSnapshot = new List<(int, string)>() { (40, "4.317857648e-06"), (20, "0.00198938141"), (44, "226.2915601"), (21, "7.563976343e-10"), (39, "45325.19832"), (50, "146090.0511"), (36, "570.5138002"), (14, "5.106136802e-06"), (52, "0.002518868836"), (53, "20.26181287"), (55, "9564.047443"), (28, "9.67893707e-10"), (5, "3.055966374e-06"), (48, "1.45517891e-07"), (51, "0"), (33, "0.0001772506288"), (42, "1.9"), (23, "6.620672934e-07"), (46, "0.001228682944"), (8, "0.005096720781"), (54, "2267.308038"), (7, "1e-07"), (32, "6.552168258e-05"), (24, "2.705435444") };
            foreach(var pair in lastSnapshot)
            {
                var (id, paying) = pair;
                if(double.TryParse(paying, out var d))
                {
                    _currentPayingRates[(AlgorithmType)id] = d;
                }
            }

        }

#region Update Methods

        // TODO maybe just swap the dictionaries???
        /// <summary>
        /// Change SMA profits to new values
        /// </summary>
        /// <param name="newSma">Algorithm/profit dictionary with new values</param>
        public static void UpdateSmaPaying(Dictionary<AlgorithmType, double> newSma)
        {
            lock (_currentPayingRates)
            {
                foreach (var algo in newSma.Keys)
                {
                    if (_currentPayingRates.ContainsKey(algo))
                    {
                        _currentPayingRates[algo] = newSma[algo];
                        if (BuildOptions.FORCE_MINING || BuildOptions.FORCE_PROFITABLE)
                        {
                            _currentPayingRates[algo] = 1000;
                        }
                    }
                }

                if (MiscSettings.Instance.UseSmaCache)
                {
                    // Cache while in lock so file is not accessed on multiple threads
                    var isFileSaved = InternalConfigs.WriteFileSettings(CachedFile, newSma);
                    if (!isFileSaved) Logger.Error(Tag, "CachedSma not saved");
                }
            }

            HasData = true;
        }

        /// <summary>
        /// Change SMA profit for one algo
        /// </summary>
        internal static void UpdatePayingForAlgo(AlgorithmType algo, double paying)
        {
            lock (_currentPayingRates)
            {
                if (!_currentPayingRates.ContainsKey(algo))
                    throw new ArgumentException("Algo not setup in SMA");
                _currentPayingRates[algo] = paying;
            }

            if (BuildOptions.FORCE_MINING || BuildOptions.FORCE_PROFITABLE)
            {
                _currentPayingRates[algo] = 1000;
            }

            HasData = true;
        }

        /// <summary>
        /// Update list of stable algorithms
        /// </summary>
        /// <param name="algorithms">Algorithms that are stable</param>
        public static void UpdateStableAlgorithms(IEnumerable<AlgorithmType> algorithms)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Updating stable algorithms");
            var hasChange = false;

            lock (_stableAlgorithms)
            {
                var algosEnumd = algorithms as AlgorithmType[] ?? algorithms.ToArray();
                foreach (var algo in algosEnumd)
                {
                    if (_stableAlgorithms.Add(algo))
                    {
                        sb.AppendLine($"\tADDED {algo}");
                        hasChange = true;
                    }
                }

                _stableAlgorithms.RemoveWhere(algo =>
                {
                    if (algosEnumd.Contains(algo)) return false;

                    sb.AppendLine($"\tREMOVED {algo}");
                    hasChange = true;
                    return true;
                });
            }
            if (!hasChange)
            {
                sb.AppendLine("\tNone changed");
            }
            Logger.Info(Tag, sb.ToString());
        }

#endregion

#region Get Methods
        
        /// <summary>
        /// Attempt to get paying rate for an algorithm
        /// </summary>
        /// <param name="algo">Algorithm</param>
        /// <param name="paying">Variable to place paying in</param>
        /// <returns>True iff we know about this algo</returns>
        public static bool TryGetPaying(AlgorithmType algo, out double paying)
        {
            lock (_currentPayingRates)
            {
                return _currentPayingRates.TryGetValue(algo, out paying);
            }
        }

        public static bool IsAlgorithmStable(AlgorithmType algo)
        {
            lock (_stableAlgorithms)
            {
                return _stableAlgorithms.Contains(algo);
            }
        }

        /// <summary>
        /// Filters SMA profits based on whether the algorithm is stable
        /// </summary>
        /// <param name="stable">True to get stable, false to get unstable</param>
        /// <returns>Filtered Algorithm/double map</returns>
        public static Dictionary<AlgorithmType, double> FilteredCurrentProfits(bool stable)
        {
            var dict = new Dictionary<AlgorithmType, double>();

            lock (_currentPayingRates)
            {
                var filtered = _currentPayingRates.Where(kvp => _stableAlgorithms.Contains(kvp.Key) == stable); 
                foreach (var kvp in filtered)
                {
                    dict[kvp.Key] = kvp.Value;
                }
            }

            return dict;
        }

        /// <summary>
        /// Copy and return SMA profits 
        /// </summary>
        public static Dictionary<AlgorithmType, double> CurrentPayingRatesSnapshot()
        {
            var dict = new Dictionary<AlgorithmType, double>();

            lock (_currentPayingRates)
            {
                foreach (var kvp in _currentPayingRates)
                {
                    dict[kvp.Key] = kvp.Value;
                }
            }

            return dict;
        }

        public static async Task<bool> WaitOnDataAsync(int seconds)
        {
            var hasData = HasData;

            for (var i = 0; i < seconds; i++)
            {
                if (hasData) return true;
                await Task.Delay(1000);
                hasData = HasData;
                Logger.Info("NICEHASH", $"After {i}s has data: {hasData}");
            }

            return hasData;
        }
#endregion
    }
}
