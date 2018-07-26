using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Linq;

namespace TVRename
{
    // ReSharper disable once InconsistentNaming
    internal class qBitTorrentFinder : TorrentFinder
    {
        public qBitTorrentFinder(TVDoc i) : base(i) { }
        public override bool Active() => TVSettings.Instance.CheckqBitTorrent;
        
        public override void Check(SetProgressDelegate prog, int startpct, int totPct)
        {
            List<TorrentEntry> downloading = GetqBitTorrentDownloads();
            SearchForAppropriateDownloads(prog, startpct, totPct, downloading);
        }

        private static List<TorrentEntry> GetqBitTorrentDownloads()
        {
            List < TorrentEntry >  ret = new List<TorrentEntry>();

            // get list of files being downloaded by qBitTorrentFinder
            string host = TVSettings.Instance.qBitTorrentHost;
            string port = TVSettings.Instance.qBitTorrentPort;
            if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(host))
                return ret;

            string url = $"http://{host}:{port}/query/";

            JToken settings = JsonHelper.ObtainToken(url + "preferences");
            JArray currentDownloads = JsonHelper.ObtainArray(url + "torrents?filter=all");

            foreach (JToken torrent in currentDownloads.Children())
            {
                JArray stuff2 = JsonHelper.ObtainArray(url + "propertiesFiles/" + torrent["hash"]);

                foreach (JToken file in stuff2.Children())
                {
                    ret.Add(new TorrentEntry(torrent["name"].ToString(), settings["save_path"] + file["name"].ToString(), (int)(100 * file["progress"].ToObject<float>())));
                }

                if (!stuff2.Children().Any())
                {
                    ret.Add(new TorrentEntry(torrent["name"].ToString(), settings["save_path"] + torrent["name"].ToString() + TVSettings.Instance.VideoExtensionsArray[0], 0));
                }
            }

            return ret;
        }
    }
}
