using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Cache;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;

namespace ReadTwitters {
  class Program {
    private static List<string> addressList = new List<string>();
    private static string dFolder = "";
    static void Main(string[] args) {
      if (ValidateArgs(args)) return;
      var deep = (args.Length == 2) && args[1].Equals("-deep", StringComparison.OrdinalIgnoreCase);     
      var fileInfo = new FileInfo(args[0]);
      dFolder = fileInfo.DirectoryName;

      ReadWebsiteList(args[0]);
     
      foreach (var address in addressList) {
        var newList = GetWebsites($"http://{address}/");

        if (deep) DeepCrawlWebPages(newList);

        Console.WriteLine($"{address} TotalList: {newList.Count}");
        RunThroughListAndSaveFiles(newList);
      }
      Console.WriteLine("Completed downloads");     
    }

    private static bool ValidateArgs(string[] args)
    {
      if (args.Length == 0)
      {
        Console.WriteLine("ReadTwitters [drive:][path][Filename] [-deep]");
        Console.WriteLine();
        Console.WriteLine(" [drive:][path][Filename]");
        Console.WriteLine("   Specifies drive, directory, and/or files to read.");
        Console.WriteLine();
        Console.WriteLine("  -deep     Crawls one extra page deep to search for email/twitter account or links");
        Console.WriteLine();
        return true;
      }

      if (!string.IsNullOrWhiteSpace(args[0])) return false;
      Console.WriteLine("Please supply filename of the csv as the first parameter");
      return true;
    }

    private static void RunThroughListAndSaveFiles(List<UrlString> newList) {
      var uDL = new List<string>();
      var uDT = new List<string>();
      var uDE = new List<string>();
      foreach (var urlString in newList) {
        if (urlString.Link.Contains("twitter") || urlString.Twitters.Count > 0 || urlString.Emails.Count > 0) {
          if (urlString.Twitters.Count > 0) {
            foreach (var twitter in urlString.Twitters) {
              if (!uDT.Contains(urlString.Domain + "," + twitter))
                uDT.Add(urlString.Domain + "," + twitter);
            }
          }
          if (urlString.Emails.Count > 0) {
            foreach (var urlStringEmail in urlString.Emails) {
              if (!uDE.Contains(urlString.Domain + "," + urlStringEmail))
                uDE.Add(urlString.Domain + "," + urlStringEmail);
            }
          }
          if ((!uDL.Contains(urlString.Domain + "," + urlString.Link)) && !string.IsNullOrWhiteSpace(urlString.Link))
            uDL.Add(urlString.Domain + "," + urlString.Link);
        }
      }
      WriteListString("ioTwitterData", uDT);
      WriteListString("ioEmailData", uDE);
      WriteListString("ioLinkData", uDL);
    }

    private static void DeepCrawlWebPages(List<UrlString> newList) {
      var rwSlim = new ReaderWriterLockSlim();
      var copyList = newList.ToArray();
      Parallel.ForEach(copyList, (urlString) => {
        if (!urlString.Url.Contains(urlString.Domain) || string.IsNullOrEmpty(urlString.Link)) return;
        var updateList = GetWebsites(urlString.Link);
        if (updateList.Count > 0) {
          rwSlim.EnterWriteLock();
          newList.AddRange(updateList);
          rwSlim.ExitWriteLock();
        }
      });
    }

    private static void WriteListString(string fileName, List<string> toWrite) {
      var csv = new StringBuilder(toWrite.Count * 25);
      foreach (var writeItem in toWrite) {
        csv.AppendLine(writeItem);
      }
      File.AppendAllText(Path.Combine(dFolder, $"{fileName}.csv"), csv.ToString());
    }

    private static void ReadWebsiteList(string filename) {
      using (var reader = new StreamReader(filename)) {
        while (!reader.EndOfStream) {
          var line = reader.ReadLine();
          addressList.Add(line);
        }
      }
    }
    private static List<UrlString> GetWebsites(string url) {
      if (!Uri.IsWellFormedUriString(url, UriKind.Absolute))
        return new List<UrlString>();

      Uri tUri = new Uri(url);
      var newUrlList = new List<UrlString>();
      string domain = tUri.Host;
      UrlString usTwitter = new UrlString() {
        Domain = domain,
        Url = url,
        Link = "",
        Twitters = new List<string>(),
        Emails = new List<string>()
      };

      try {
        using (var client = new WebClient()) // WebClient class inherits IDisposable
             {
          client.CachePolicy = new RequestCachePolicy(RequestCacheLevel.CacheIfAvailable);
          var web = new HtmlWeb();
          try {
            newUrlList.AddRange(from descendant in web.Load(url).DocumentNode.Descendants("a")
                                where descendant.Attributes["href"]?.Value?.StartsWith("http") ?? false
                                select new UrlString() {
                                  Domain = domain,
                                  Url = url,
                                  Link = descendant.Attributes["href"].Value,
                                  Twitters = new List<string>(),
                                  Emails = new List<string>()
                                }
                            into us
                                where !us.Link.Contains("?")
                                select us);
          }
          catch (Exception ) {      
            // ignore errors
          }
          try {
            var htmlTaskCode = client.DownloadStringTaskAsync(url);
            if (htmlTaskCode.Wait(TimeSpan.FromSeconds(1)) && !string.IsNullOrWhiteSpace(htmlTaskCode.Result)) {
              var htmlCode = htmlTaskCode.Result.Replace("</s>", "").Replace("<b>", "").Replace("<s>", "").Replace("</b>", "");
              List<Task> filterTasks = new List<Task>
              {
                Task.Run(() => GetTwitterMatches(htmlCode, usTwitter)),
                Task.Run(() => GetEmailMatches(htmlCode, usTwitter))
              };
              Task.WhenAll(filterTasks);
            }
          }
          catch (Exception)
          {
            // ignore errors
          }
         
        }
        newUrlList.Add(usTwitter);
      }
      catch (Exception) {
      }
      return newUrlList;
    }

    private static void GetEmailMatches(string htmlCode, UrlString usTwitter)
    {
      var regEmailMatches = Regex.Match(htmlCode,
        @"(?:[a-z0-9!#$%&'*+=~-]+(?:\.[a-z0-9!#$%&'*+={|}~-]+)*@(?:[a-z0-9](?:[a-z0-9-]*[a-z0-9])?\.)+[a-z0-9](?:[a-z0-9-]*[a-z0-9])?)",
        RegexOptions.IgnoreCase);
      while (regEmailMatches.Success)
      {
        if (!usTwitter.Emails.Contains(regEmailMatches.Value))
        {
          //Console.WriteLine(regExMatches.Value);
          usTwitter.Emails.Add(regEmailMatches.Value);
        }
        regEmailMatches = regEmailMatches.NextMatch();
      }
    }

    private static void GetTwitterMatches(string htmlCode, UrlString usTwitter)
    {
      var regExMatches = Regex.Match(htmlCode, @"(?<=^|(?<=[^a-zA-Z0-9-_\.]))@([A-Za-z]+[A-Za-z0-9]+)");

      while (regExMatches.Success)
      {
        if (!usTwitter.Twitters.Contains(regExMatches.Value))
        {
          var twitterPossible = regExMatches.Value;
          if (twitterPossible != "@context" && twitterPossible != "@type" && twitterPossible != "@font" &&
              twitterPossible != "@author"
              && twitterPossible != "@count" && twitterPossible.Length > 4)
            usTwitter.Twitters.Add(regExMatches.Value);
        }
        regExMatches = regExMatches.NextMatch();
      }
    }

    public class UrlString {
      public string Url { get; set; }
      public string Domain { get; set; }
      public string Link { get; set; }
      public List<string> Twitters { get; set; }
      public List<string> Emails { get; set; }
    }
  }
}
