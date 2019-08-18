using PlaylistsNET.Content;
using PlaylistsNET.Models;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Xml;

namespace IPTVNotifier
{
    class Program
    {
        static Configuration configFile = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
        static void Main(string[] args)
        {
            try
            {
                var directory = configFile.AppSettings.Settings["PlaylistDirectory"].Value;
                var url = configFile.AppSettings.Settings["ExternalPlaylistURL"].Value;
                var now = DateTime.Now;

                var fileName = "Playlist" + now.ToString("yyyyMMddHHmmssFFFFFFF") + ".m3u";
                var downloadFile = directory + fileName;

                Directory.CreateDirectory(directory);

                using (var client = new WebClient())
                {
                    client.DownloadFile(url, downloadFile);
                }

                var newPlaylist = GetPlaylistFromFile(downloadFile);

                var dir = new DirectoryInfo(directory);
                var myFile = dir.GetFiles()
                             .OrderByDescending(f => f.Name)
                             .Skip(1)
                             .First();

                var oldPlaylist = GetPlaylistFromFile(myFile.FullName);

                var newEntries = newPlaylist.PlaylistEntries.Select(x => x.Title).ToList();
                var oldEntries = oldPlaylist.PlaylistEntries.Select(x => x.Title).ToList();

                var result = newEntries.Except(oldEntries).ToList();

                if (result.Count() > 0)
                {
                    configFile.AppSettings.Settings["LastNewRecordCount"].Value = result.Count().ToString();
                    configFile.Save(ConfigurationSaveMode.Modified);
                    string mailBody, mailSubject;
                    PrepareMail(now, result, newPlaylist, out mailBody, out mailSubject);
                    SendMail(mailSubject, mailBody);
                }

                int.TryParse(configFile.AppSettings.Settings["TryCount"].Value, out int tryCount);
                configFile.AppSettings.Settings["TryCount"].Value = (tryCount + 1).ToString();
                configFile.AppSettings.Settings["LastRunTime"].Value = now.ToString("dd/MM/yyyy - HH:mm");
                configFile.Save(ConfigurationSaveMode.Modified);

                DeleteOldestPlaylist(directory);

            }
            catch (Exception ex)
            {
                try
                {
                    SendMail("Error - IPTVNotifier", ex.ToString());
                    configFile.Save(ConfigurationSaveMode.Modified);
                }
                catch
                {
                }
            }
        }
        static M3uPlaylist GetPlaylistFromFile(string path)
        {
            var playlist = new M3uPlaylist();
            var content = new M3uContent();

            using (MemoryStream ms = new MemoryStream())
            using (FileStream stream = File.Open(path, FileMode.Open))
            {
                playlist = content.GetFromStream(stream);
            }

            return playlist;
        }
        static void DeleteOldestPlaylist(string directory)
        {
            var dir = new DirectoryInfo(directory);
            var myFile = dir.GetFiles()
                         .OrderByDescending(f => f.Name)
                         .Skip(1);

            foreach (var fileInfo in myFile)
            {
                fileInfo.Delete();
            }
        }
        static void SendMail(string subject, string body)
        {
            var fromAddress = new MailAddress(configFile.AppSettings.Settings["FromMail"].Value, "IPTVNotifier");
            string fromPassword = configFile.AppSettings.Settings["FromMailPassword"].Value;

            var confToAddr = configFile.AppSettings.Settings["MailTo"].Value;
            var to = confToAddr.Split(new[] { ";" }, StringSplitOptions.RemoveEmptyEntries);
            var toAddress = new MailAddress(to[0]);

            var smtp = new SmtpClient
            {
                Host = configFile.AppSettings.Settings["FromMailHost"].Value,
                Port = 587,
                EnableSsl = true,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                Credentials = new NetworkCredential(fromAddress.Address, fromPassword),
                Timeout = 20000
            };
            using (var message = new MailMessage(fromAddress, toAddress)
            {
                Subject = subject,
                Body = body
            })
            {
                if (to.Count() > 1)
                    for (int i = 1; i < to.Count(); i++)
                        message.To.Add(new MailAddress(to[i].Trim()));

                smtp.Send(message);
            }
        }
        private static void PrepareMail(DateTime now, List<string> result, M3uPlaylist playlist, out string mailBody, out string mailSubject)
        {
            var mailXml = new XmlDocument();
            var langCode = configFile.AppSettings.Settings["MailLangCode"].Value;
            mailXml.Load("MailTexts.xml");

            var playlistEntries = playlist.PlaylistEntries;
            var newResult = result.Select(p => p + Environment.NewLine + playlistEntries.First(x => x.Title == p).Path + Environment.NewLine).ToList();


            mailBody = mailXml.SelectSingleNode($"/MailText/NewEntriesCaption[@lang-code='{langCode}']").InnerText + Environment.NewLine;
            mailBody += Environment.NewLine;
            mailBody += String.Join(Environment.NewLine, newResult);
            mailBody += Environment.NewLine;
            mailBody += Environment.NewLine;
            mailBody += $"-- {mailXml.SelectSingleNode($"/MailText/MailSignature[@lang-code='{langCode}']").InnerText} :)";
            mailBody += Environment.NewLine;

            mailSubject = "Duyuru! IPTVNotifier - Yeni eklenen içerik - " + now.ToString("dd/MM/yyyy - HH:mm");
        }
    }
}
