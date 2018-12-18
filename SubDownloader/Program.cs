using OSDBnet;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Messaging;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace SubDownloader
{
    internal class Program
    {
        private static List<string> extensions = new List<string>(new[] { ".mp4", ".mkv", ".3gp", ".avi" });
        private static List<Show> FailedMovies = new List<Show>();

        private static MessageQueue errorQueue = null;

        private static void Main(string[] args)
        {
            MessageQueue messageQueue = null;
            messageQueue = SetMessageQueues();

            messageQueue.Formatter = new XmlMessageFormatter(new Type[] { typeof(Show) });
            errorQueue.Formatter = new XmlMessageFormatter(new Type[] { typeof(ErrorMessage) });

            IAnonymousClient client;

            List<Show> Shows = new List<Show>();
            List<Show> DownloadedShows = new List<Show>();

            int j = 0;
            client = Login();
            while (client == null)
            {
                if (j++ > 100)
                {
                    Thread.Sleep(new TimeSpan(0, 10, 0));
                    j = 0;
                    ShowMessageBox("Connectivity problems\nWaiting 10 minutes", "SubDownloader");
                }
                Thread.Sleep(500);
                client = Login();
            }

            List<string> newDirs = new List<string>();
            if (args.Length == 0)
            {
                AddShowList(Shows, Directory.GetCurrentDirectory());
            }
            else
            {
                if (File.Exists(args[0]))
                {
                    if (extensions.Contains(Path.GetExtension(args[0])))
                    {
                        Shows.Add(CreateShow(args[0]));
                    }
                }
                else
                {
                    AddShowList(Shows, args[0]);
                }
            }

            if (Process.GetProcessesByName("SubDownloader").Count() > 1)
            {
                foreach (var show in Shows)
                {
                    messageQueue.Send(show);
                }
                return;
            }

            string downloading = "Finding subs for:\n";
            foreach (var s in Shows)
            {
                downloading += s.OutputName();
            }
            ShowMessageBox(downloading, "SubDownloader");
            DownloadShowList(ref client, ref Shows, DownloadedShows, messageQueue);
        }

        private static MessageQueue SetMessageQueues()
        {
            MessageQueue messageQueue;
            string MessageQueuePath = @".\Private$\SubDownloader";
            string ErrorQueuePath = @".\Private$\SubErrors";
            if (MessageQueue.Exists(MessageQueuePath))
            {
                messageQueue = new MessageQueue(MessageQueuePath);
                messageQueue.Label = "SubQueue";
            }
            else
            {
                MessageQueue.Create(MessageQueuePath);
                messageQueue = new MessageQueue(MessageQueuePath);
                messageQueue.Label = "SubQ";
            }

            if (MessageQueue.Exists(ErrorQueuePath))
            {
                errorQueue = new MessageQueue(ErrorQueuePath);
                errorQueue.Label = "ErrorQueue";
            }
            else
            {
                MessageQueue.Create(ErrorQueuePath);
                errorQueue = new MessageQueue(ErrorQueuePath);
                errorQueue.Label = "ErrorQ";
            }

            return messageQueue;
        }

        private static void DownloadShowList(ref IAnonymousClient client, ref List<Show> Shows, List<Show> DownloadedShows, MessageQueue messageQueue)
        {
            int j = 0;
            while (Shows.Count != 0)
            {
                string output = "";
                var messages = messageQueue.GetAllMessages();
                if (messages.Length > 0)
                {
                    output += "Added to search:\n";
                    foreach (var message in messages)
                    {
                        var temp = (Show)message.Body;
                        if (Shows.Contains(temp))
                        {
                            continue;
                        }
                        else
                        {
                            Shows.Add(temp);
                            output += Shows.Last().OutputName();
                        }
                    }
                    if (!output.Equals("Added to search:\n"))
                    {
                        ShowMessageBox(output, "SubDownloader");
                    }
                    output = "";
                }
                messageQueue.Purge();

                DownloadedShows.Clear();
                foreach (var show in Shows)
                {
                    j = 0;
                    while (client == null)
                    {
                        if (j++ > 100)
                        {
                            Thread.Sleep(new TimeSpan(0, 10, 0));
                            j = 0;
                            ShowMessageBox("Connectivity problems\nWaiting 10 minutes", "SubDownloader");
                        }
                        Thread.Sleep(500);
                        client = Login();
                    }

                    if (DownloadShowSub(show, client)) DownloadedShows.Add(show);
                    else if (show.Season == null && show.Episode == null)
                    {
                        FailedMovies.Add(show);
                    }
                }
                Shows = Shows.Except(DownloadedShows).ToList();
                Shows = Shows.Except(FailedMovies).ToList();

                if (DownloadedShows.Count == 0 && Shows.Count == 0 && FailedMovies.Count > 0)
                {
                    output += "Failed to find subs for:\n";
                    foreach (var s in FailedMovies)
                    {
                        output += s.OutputName();
                    }
                    ShowMessageBox(output, "SubDownloader");
                }

                if (DownloadedShows.Count > 0)
                {
                    if (FailedMovies.Count > 0)
                    {
                        output += "Failed to find subs for:\n";
                        foreach (var s in FailedMovies)
                        {
                            output += s.OutputName();
                        }
                    }
                    FailedMovies.Clear();
                    output += "Downloaded subs for:\n";
                    foreach (var s in DownloadedShows)
                    {
                        output += s.OutputName();
                    }
                    if (Shows.Count > 0)
                    {
                        output += "Still searching in the background for:\n";
                        foreach (var s in Shows)
                        {
                            output += s.OutputName();
                        }
                    }
                    ShowMessageBox(output, "SubDownloader");
                }
                if (FailedMovies.Count > 0)
                {
                    output += "Failed to find subs for:\n";
                    foreach (var s in FailedMovies)
                    {
                        output += s.OutputName();
                    }
                }
                FailedMovies.Clear();
                if (Shows.Count > 0)
                {
                    Thread.Sleep(new TimeSpan(0, 2, 0));
                }
            }
        }

        private static void ShowMessageBox(string text, string caption)
        {
            new Thread(() => MyMessageBox(text, caption)).Start();
        }

        private static void MyMessageBox(string text, string caption)
        {
            MessageBox.Show(text, caption);
        }

        private static IAnonymousClient Login()
        {
            try
            {
                IAnonymousClient client = Osdb.Login("en", "SolEol 0.0.8");
                return client;
            }
            catch (Exception ex)
            {
                errorQueue.Send(new ErrorMessage { Message = ex.ToString(), Time = DateTime.Now });
                return null;
            }
        }

        private static Show CreateShow(string path)
        {
            Regex regex = new Regex(@"[Ss](?<season>\d{1,2})[Ee](?<episode>\d{1,2})");
            Match match = regex.Match(Path.GetFileNameWithoutExtension(path));
            string name = "";
            if (match.Success)
            {
                var fileName = Path.GetFileNameWithoutExtension(path);
                var season = match.Groups["season"].ToString();
                var episode = match.Groups["episode"].ToString();
                name = fileName.Replace('.', ' ').Remove(fileName.IndexOf(match.Groups[0].ToString())).Trim();
                return new Show { Season = int.Parse(season), Episode = int.Parse(episode), Name = name, PathToFile = path };
            }
            else
            {
                return new Show { Season = null, Episode = null, Name = Path.GetFileName(path), PathToFile = path };
            }
        }

        private static bool DownloadShowSub(Show show, IAnonymousClient client)
        {
            try
            {
                Subtitle sub = null;
                IList<Subtitle> subs;

                subs = client.SearchSubtitlesFromFile("eng", show.PathToFile);
                if (!subs.Any(i => i.LanguageName.Equals("English")) && show.Episode != null && show.Season != null)
                {
                    subs = client.SearchSubtitlesFromQuery("eng", show.Name, show.Season, show.Episode);
                }

                if (subs.Any(i => i.LanguageName.Equals("English") && i.SubtitleFileName.Contains(".HI.")))
                {
                    sub = subs.FirstOrDefault(i => i.LanguageName.Equals("English") && i.SubtitleFileName.Contains(".HI.") && SubsCorrect(show, i));
                }
                else if (subs.Any(i => i.LanguageName.Equals("English")))
                {
                    sub = subs.FirstOrDefault(i => i.LanguageName.Equals("English") && SubsCorrect(show, i));
                }

                if (sub != null)
                {
                    client.DownloadSubtitleToPath(Path.GetDirectoryName(show.PathToFile), sub, $"{Path.GetFileNameWithoutExtension(show.PathToFile)}.srt");
                    return true;
                }
                else
                {
                    return false;
                }
            }
            catch (FormatException ex)
            {
                ShowMessageBox($"{show.Name} --- subs currenty unfindable", "SubDownloader");
                errorQueue.Send(new ErrorMessage { Message = $"{show.Name} --- {ex.ToString()}", Time = DateTime.Now });
                FailedMovies.Add(show);

                return false;
            }
            catch (CookComputing.XmlRpc.XmlRpcException ex)
            {
                errorQueue.Send(new ErrorMessage { Message = ex.ToString(), Time = DateTime.Now });
                return false;
            }
            catch (System.Net.WebException ex)
            {
                errorQueue.Send(new ErrorMessage { Message = ex.ToString(), Time = DateTime.Now });
                return false;
            }
            catch (Exception ex)
            {
                errorQueue.Send(new ErrorMessage { Message = ex.ToString(), Time = DateTime.Now });
                client = null;
                return false;
            }
        }

        private static bool SubsCorrect(Show show, Subtitle i)
        {
            Regex regex = new Regex(@"[Ss](?<season>\d{1,2})[Ee](?<episode>\d{1,2})");
            Match match = regex.Match(i.SubtitleFileName);
            string name = "";
            if (match.Success)
            {
                var fileName = Path.GetFileNameWithoutExtension(i.SubtitleFileName);
                var season = match.Groups["season"].ToString();
                var episode = match.Groups["episode"].ToString();
                name = fileName.Replace('.', ' ').Remove(fileName.IndexOf(match.Groups[0].ToString())).Trim();
                return (name.ToLower().Equals(show.Name.ToLower()) && int.Parse(season).Equals(show.Season) && int.Parse(episode).Equals(show.Episode));
            }

            return false;
        }

        private static void AddShowList(List<Show> shows, string dirPath)
        {
            shows.AddRange(Directory.EnumerateFiles(dirPath)
                .Where(i => extensions.Contains(Path.GetExtension(i)))
                .Select(f => CreateShow(f)).ToList());

            foreach (var dir in Directory.EnumerateDirectories(dirPath))
            {
                if (Directory.EnumerateDirectories(dir).Contains("Subs")) continue;
                AddShowList(shows, dir);
            }
        }
    }

    public class Show
    {
        public int? Season { get; set; }
        public int? Episode { get; set; }
        public string Name { get; set; }
        public string PathToFile { get; set; }

        public override bool Equals(object obj)
        {
            var show = obj as Show;
            return show != null &&
                   PathToFile == show.PathToFile;
        }

        public override int GetHashCode()
        {
            return -1107405011 + EqualityComparer<string>.Default.GetHashCode(PathToFile);
        }

        public string OutputName()
        {
            if (Season != null && Episode != null)
            {
                if (Season > 9 && Episode > 9)
                {
                    return $"{Name} S{Season}E{Episode}\n";
                }
                else if (Season < 10 && Episode > 9)
                {
                    return $"{Name} S0{Season}E{Episode}\n";
                }
                else
                {
                    return $"{Name} S0{Season}E0{Episode}\n";
                }
            }
            else
            {
                return $"{Name}\n";
            }
        }
    }

    public class ErrorMessage
    {
        public string Message { get; set; }
        public DateTime Time { get; set; }
    }
}