using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Alphaleonis.Win32.Filesystem;
using Path = System.IO.Path;

namespace TVRename
{
    internal static class FinderHelper
    {
        public static bool FindSeasEp(FileInfo fi, out int seas, out int ep, out int maxEp, ShowItem si,
    out TVSettings.FilenameProcessorRE re)
        {
            return FindSeasEp(fi, out seas, out ep, out maxEp, si, TVSettings.Instance.FNPRegexs,
                TVSettings.Instance.LookForDateInFilename, out re);
        }

        public static bool FindSeasEp(FileInfo fi, out int seas, out int ep, out int maxEp, ShowItem si,
            List<TVSettings.FilenameProcessorRE> rexps, bool doDateCheck, out TVSettings.FilenameProcessorRE re)
        {
            re = null;
            if (fi == null)
            {
                seas = -1;
                ep = -1;
                maxEp = -1;
                return false;
            }

            if (doDateCheck && FindSeasEpDateCheck(fi, out seas, out ep, out maxEp, si))
                return true;

            string filename = fi.Name;
            int l = filename.Length;
            int le = fi.Extension.Length;
            filename = filename.Substring(0, l - le);
            return FindSeasEp(fi.Directory.FullName, filename, out seas, out ep, out maxEp, si, rexps, out re);
        }

        public static bool FindSeasEpDateCheck(FileInfo fi, out int seas, out int ep, out int maxEp, ShowItem si)
        {
            if (fi == null || si == null)
            {
                seas = -1;
                ep = -1;
                maxEp = -1;
                return false;
            }

            // look for a valid airdate in the filename
            // check for YMD, DMY, and MDY
            // only check against airdates we expect for the given show
            SeriesInfo ser = TheTVDB.Instance.GetSeries(si.TvdbCode);
            string[] dateFormats = new[] { "yyyy-MM-dd", "dd-MM-yyyy", "MM-dd-yyyy", "yy-MM-dd", "dd-MM-yy", "MM-dd-yy" };
            string filename = fi.Name;
            // force possible date separators to a dash
            filename = filename.Replace("/", "-");
            filename = filename.Replace(".", "-");
            filename = filename.Replace(",", "-");
            filename = filename.Replace(" ", "-");

            ep = -1;
            seas = -1;
            maxEp = -1;
            Dictionary<int, Season> seasonsToUse = si.DvdOrder ? ser.DvdSeasons : ser.AiredSeasons;

            foreach (KeyValuePair<int, Season> kvp in seasonsToUse)
            {
                if (si.IgnoreSeasons.Contains(kvp.Value.SeasonNumber))
                    continue;

                foreach (Episode epi in kvp.Value.Episodes.Values)
                {
                    DateTime? dt = epi.GetAirDateDt(); // file will have local timezone date, not ours
                    if (dt == null)
                        continue;

                    TimeSpan closestDate = TimeSpan.MaxValue;

                    foreach (string dateFormat in dateFormats)
                    {
                        string datestr = dt.Value.ToString(dateFormat);
                        if (filename.Contains(datestr) && DateTime.TryParseExact(datestr, dateFormat,
                                new CultureInfo("en-GB"), DateTimeStyles.None, out DateTime dtInFilename))
                        {
                            TimeSpan timeAgo = DateTime.Now.Subtract(dtInFilename);
                            if (timeAgo < closestDate)
                            {
                                seas = (si.DvdOrder ? epi.DvdSeasonNumber : epi.AiredSeasonNumber);
                                ep = si.DvdOrder ? epi.DvdEpNum : epi.AiredEpNum;
                                closestDate = timeAgo;
                            }
                        }
                    }
                }
            }

            return ((ep != -1) && (seas != -1));
        }

        public static bool FindSeasEp(DirectoryInfo di, out int seas, out int ep, ShowItem si,
            out TVSettings.FilenameProcessorRE re)
        {
            List<TVSettings.FilenameProcessorRE> rexps = TVSettings.Instance.FNPRegexs;
            re = null;

            if (di == null)
            {
                seas = -1;
                ep = -1;
                return false;
            }

            return FindSeasEp(di.Parent.FullName, di.Name, out seas, out ep, out int _, si, rexps, out re);
        }

        public static bool FindSeasEp(string directory, string filename, out int seas, out int ep, out int maxEp,
            ShowItem si, List<TVSettings.FilenameProcessorRE> rexps)
        {
            return FindSeasEp(directory, filename, out seas, out ep, out maxEp, si, rexps, out TVSettings.FilenameProcessorRE _);
        }

        public static bool FindSeasEp(string itemName, out int seas, out int ep, out int maxEp, ShowItem show)
        {
            return FindSeasEp(String.Empty, itemName, out seas, out ep, out maxEp, show, TVSettings.Instance.FNPRegexs, out TVSettings.FilenameProcessorRE _);
        }

        public static bool FindSeasEp(FileInfo theFile, out int seasF, out int epF, out int maxEp, ShowItem sI)
        {
            return FindSeasEp(theFile, out seasF, out epF, out maxEp, sI, out TVSettings.FilenameProcessorRE _);
        }

        public static bool FileNeeded(FileInfo fi, ShowItem si, DirFilesCache dfc)
        {
            if (FindSeasEp(fi, out int seasF, out int epF, out _, si, out _))
            {
                return EpisodeNeeded(si, dfc, seasF, epF, fi);
            }

            //We may need the file
            return true;
        }

        public static bool FileNeeded(DirectoryInfo di, ShowItem si, DirFilesCache dfc)
        {
            if (FindSeasEp(di, out int seasF, out int epF, si, out _))
            {
                return EpisodeNeeded(si, dfc, seasF, epF, di);
            }

            //We may need the file
            return true;
        }
        public static List<FileInfo> FindEpOnDisk(this DirFilesCache dfc, ProcessedEpisode pe,
            bool checkDirectoryExist = true)
        {
            return FindEpOnDisk(dfc, pe.Show, pe, checkDirectoryExist);
        }

        public static List<FileInfo> FindEpOnDisk(DirFilesCache dfc, ShowItem si, Episode epi,
            bool checkDirectoryExist = true)
        {
            if (dfc == null)
                dfc = new DirFilesCache();

            List<FileInfo> ret = new List<FileInfo>();

            int seasWanted = si.DvdOrder ? epi.TheDvdSeason.SeasonNumber : epi.TheAiredSeason.SeasonNumber;
            int epWanted = si.DvdOrder ? epi.DvdEpNum : epi.AiredEpNum;

            int snum = seasWanted;

            Dictionary<int, List<string>> dirs = si.AllFolderLocationsEpCheck(checkDirectoryExist);

            if (!dirs.ContainsKey(snum))
                return ret;

            foreach (string folder in dirs[snum])
            {
                FileInfo[] files = dfc.Get(folder);
                if (files == null)
                    continue;

                foreach (FileInfo fiTemp in files)
                {
                    if (!TVSettings.Instance.UsefulExtension(fiTemp.Extension, false))
                        continue; // move on

                    if (!FindSeasEp(fiTemp, out int seasFound, out int epFound, out int _, si)) continue;

                    if (seasFound == -1)
                        seasFound = seasWanted;

                    if ((seasFound == seasWanted) && (epFound == epWanted))
                        ret.Add(fiTemp);
                }
            }

            return ret;
        }

        public static bool EpisodeNeeded(ShowItem si, DirFilesCache dfc, int seasF, int epF, FileSystemInfo fi)
        {
            try
            {
                SeriesInfo s = si.TheSeries();
                Episode ep = s.GetEpisode(seasF, epF, si.DvdOrder);
                ProcessedEpisode pep = new ProcessedEpisode(ep, si);

                foreach (FileInfo testFileInfo in FindEpOnDisk(dfc, si, pep))
                {
                    //We will check that the file that is found is not the one we are testing
                    if (fi.FullName == testFileInfo.FullName) continue;

                    //We have found another file that matches
                    return false;
                }
            }
            catch (SeriesInfo.EpisodeNotFoundException)
            {
                //Ignore execption, we may need the file
                return true;
            }
            return true;
        }
        public static string SimplifyFilename(string filename, string showNameHint)
        {
            // Look at showNameHint and try to remove the first occurance of it from filename
            // This is very helpful if the showname has a >= 4 digit number in it, as that
            // would trigger the 1302 -> 13,02 matcher
            // Also, shows like "24" can cause confusion

            //TODO: More replacement of non useful characters - MarkSummerville
            filename = filename.Replace(".", " "); // turn dots into spaces

            if (string.IsNullOrEmpty(showNameHint))
                return filename;

            bool nameIsNumber = (Regex.Match(showNameHint, "^[0-9]+$").Success);

            int p = filename.IndexOf(showNameHint, StringComparison.Ordinal);

            if (p == 0)
            {
                filename = filename.Remove(0, showNameHint.Length);
                return filename;
            }

            if (nameIsNumber) // e.g. "24", or easy exact match of show name at start of filename
            {
                filename = filename.Remove(0, showNameHint.Length);
                return filename;
            }

            foreach (Match m in Regex.Matches(showNameHint, "(?:^|[^a-z]|\\b)([0-9]{3,})")
            ) // find >= 3 digit numbers in show name
            {
                if (m.Groups.Count > 1) // just in case
                {
                    string number = m.Groups[1].Value;
                    filename = Regex.Replace(filename, "(^|\\W)" + number + "\\b",
                        ""); // remove any occurances of that number in the filename
                }
            }

            return filename;
        }

        public static bool FindSeasEp(string directory, string filename, out int seas, out int ep, out int maxEp,
            ShowItem si, List<TVSettings.FilenameProcessorRE> rexps, out TVSettings.FilenameProcessorRE rex)
        {
            string showNameHint = (si != null) ? si.ShowName : "";
            maxEp = -1;
            seas = ep = -1;
            rex = null;

            filename = SimplifyFilename(filename, showNameHint);

            string fullPath =
                directory + Path.DirectorySeparatorChar +
                filename; // construct full path with sanitised filename

            filename = filename.ToLower() + " ";
            fullPath = fullPath.ToLower() + " ";

            foreach (TVSettings.FilenameProcessorRE re in rexps)
            {
                if (!re.Enabled)
                    continue;

                try
                {
                    Match m = Regex.Match(re.UseFullPath ? fullPath : filename, re.RegExpression,
                        RegexOptions.IgnoreCase);

                    if (m.Success)
                    {
                        if (!Int32.TryParse(m.Groups["s"].ToString(), out seas))
                            seas = -1;

                        if (!Int32.TryParse(m.Groups["e"].ToString(), out ep))
                            ep = -1;

                        if (!Int32.TryParse(m.Groups["f"].ToString(), out maxEp))
                            maxEp = -1;

                        rex = re;
                        if ((seas != -1) || (ep != -1)) return true;
                    }
                }
                catch (FormatException)
                {
                }
                catch (ArgumentException)
                {
                }
            }

            return ((seas != -1) || (ep != -1));
        }
    }
}