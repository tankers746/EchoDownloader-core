using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace echoDownloader
{
    class Config
    {
        private ILogger logger { get; } = Cli.LoggerFactory.CreateLogger<Config>();
        public static readonly string configFile = @"config.json";
        public readonly string echoFile = @"echoes.json";
        public ConcurrentDictionary<string, Echo> echoes { get; set; }
        public List<string> excludeUnits { get; set; }
        public List<string> excludeVenues { get; set; }
        public DateTime before { get; set; }
        public DateTime after { get; set; }
        public string downloads { get; set; }

        public Config()
        {
            excludeUnits = new List<string>();
            excludeVenues = new List<string>();
            before = DateTime.MaxValue;
            after = DateTime.MinValue;
        }

        public List<Echo> FilterEchoes(bool? downloaded)
        {
            return echoes
                .Select(e => e.Value)
                .Where(e => (downloaded == null || downloaded.Equals(e.downloaded)))
                .Where(e => (!excludeUnits.Any(u => e.unit.ToLowerInvariant().Contains(u.ToLowerInvariant()))))
                .Where(e => (!excludeVenues.Any(v => e.venue.ToLowerInvariant().Contains(v.ToLowerInvariant()))))
                .Where(e => (e.date < before))
                .Where(e => (e.date > after))
                .OrderBy(e => e.date)
                .ToList();
        }

        public void PrintFiltered()
        {
            List<Echo> echoList = FilterEchoes(null);
            Console.WriteLine("{0} lecture(s) found matching the filter", echoList.Count);
            foreach(var e in echoList)
            {
                string downloaded = "";
                if (e.downloaded)
                {
                    downloaded = " [D]";
                }
                string venue = "N/A";
                if (e.venue != null)
                {
                    string[] venueList = e.venue.Split(',');
                    venue = venueList[venueList.Length - 1].Split('[')[0].Trim();
                }
                Console.WriteLine("{0} - {1} @ {2} [{3}]{4}", e.unit, e.title, venue, Path.GetExtension(e.url).TrimStart('.'), downloaded);
            }
        }

        public void SaveEchoFile()
        {
            string json = JsonConvert.SerializeObject(echoes, Formatting.Indented);
            File.WriteAllText(echoFile, json);
            logger.LogDebug("Saved {echoFile}", echoFile);
        }

        public void setEchoesDownloaded(string setValue)
        {
            if (Boolean.TryParse(setValue, out bool parsed))
            {
                foreach (var e in FilterEchoes(null))
                {
                    e.downloaded = parsed;
                }
                SaveEchoFile();
            }
        }

    }
}
