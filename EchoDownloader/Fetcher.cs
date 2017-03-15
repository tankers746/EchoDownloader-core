using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using System.Net;
using AngleSharp.Parser.Html;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace echoDownloader
{
    class Fetcher
    {
        private ILogger logger { get; } = Cli.LoggerFactory.CreateLogger<Fetcher>();
        private BlackboardConnector bc { get; set; }
        public Config c { get; set; }

        public Fetcher(Config c, string username, string password)
        {
            this.c = c;
            bc = new BlackboardConnector(username, password);
        }

        public async Task FetchAsync()
        {
            if (await bc.loginLMSAsync())
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                logger.LogInformation("Fetching lectures...");
                int echoCount = c.echoes.Count;
                Dictionary<string, string> loadedUnits = await bc.loadUnitsAsync();
                var units = loadedUnits.Where(u => !c.excludeUnits.Any(u2 => u.Value.ToLowerInvariant().Contains(u2.ToLowerInvariant())));
                var tasks = units.Select(u => FetchEchoesAsync(u)).ToArray();
                await Task.WhenAll(tasks);

                if (c.echoes.Count - echoCount > 0)
                {
                    logger.LogInformation("Fetched {count} lectures", c.echoes.Count - echoCount);
                    c.SaveEchoFile();
                }
                watch.Stop();
                logger.LogDebug("Fetching lectures took {ms}ms", watch.ElapsedMilliseconds);
            }
            else
            {
                logger.LogError("Unable to fetch lectures without LMS login");
            }
        }


        async Task FetchEchoesAsync(KeyValuePair<string, string> unit)
        {
            (string echoBase, JObject echo360) apiData;
            HttpClientHandler handler = new HttpClientHandler();
            foreach (Cookie cookie in bc.cookies) handler.CookieContainer.Add(new Uri(bc.blackboard), cookie);

            using (HttpClient client = new HttpClient(handler))
            {
                //get the JSON data from the API
                logger.LogDebug("Getting API data for course ID {ID} - {Name}", unit.Key, unit.Value);
                try
                {
                    apiData = await GetAPIDataAsync(unit.Key, client);
                }
                catch (Exception ex)
                {
                    logger.LogError("Failed to get data from the API for {ID} - {Name}", unit.Key, unit.Value);
                    logger.LogDebug(ex.StackTrace);
                    return;
                }
            }
            try
            {
                await ParseEchoesAsync(unit.Key, apiData.echoBase, apiData.echo360, handler.CookieContainer);
            }
            catch (Exception ex)
            {
                logger.LogError("Failed to parse the API data for {ID} - {Name}", unit.Key, unit.Value);
                logger.LogDebug(ex.StackTrace);
            }
        }

        async Task<(string echoBase, JObject echo360)> GetAPIDataAsync(string courseID, HttpClient client)
        {
            //open the echo module on blackboard
            var url = $"{ bc.blackboard }/webapps/osc-BasicLTI-BBLEARN/window.jsp?course_id={ courseID }&id=lectur";
            logger.LogDebug("Getting url {url}", url);
            var blackboardLTI = await (await client.GetAsync(url)).Content.ReadAsStringAsync();
            //create a html doc from the response
            var parser = new HtmlParser();
            var lmsEcho = parser.Parse(blackboardLTI);    
            //get the blackboard auth form being sent to echo360
            var formNode = lmsEcho.QuerySelector("form");
            var echoUrl = formNode.Attributes["action"].Value;
            var formValues = new List<KeyValuePair<string, string>>();
            var uri = new Uri(echoUrl);
            var echoBase = $"{ uri.Scheme }://{ uri.Authority }";
            //capture all of the data being sent in the form
            foreach (var node in formNode.QuerySelectorAll("input"))
                formValues.Add(new KeyValuePair<string, string>(node.Attributes["name"].Value, node.Attributes["value"].Value));
            //send our own post request with the form data
            var echoAuthRequest = new HttpRequestMessage(HttpMethod.Post, echoUrl);
            echoAuthRequest.Content = new FormUrlEncodedContent(formValues);
            logger.LogDebug("Posting auth request to {url}", echoUrl);
            var authenticated = parser.Parse(await (await client.SendAsync(echoAuthRequest)).Content.ReadAsStringAsync());
            //extract the ID that is used for the API
            var courseEchoUrl = authenticated.QuerySelector("iframe").Attributes["src"].Value;
            var coursePage = parser.Parse(await (await client.GetAsync(courseEchoUrl)).Content.ReadAsStringAsync());
            var iframeSrc = new Uri(new Uri(echoBase), new Uri(coursePage.QuerySelector("iframe").Attributes["src"].Value, UriKind.Relative));
            var apiSectionID = iframeSrc.Segments[iframeSrc.Segments.Length - 1];
            //get the course data (presentations, etc) from the echo360 api
            await client.GetAsync(iframeSrc);
            var apiurl = $"{ echoBase }/ess/client/api/sections/{ apiSectionID }/section-data.json?&pageSize=999";
            var json = await (await client.GetAsync(apiurl)).Content.ReadAsStringAsync();
            return (echoBase, JObject.Parse(json));
        }

        async Task ParseEchoesAsync(string courseID, string echoBase, JObject echo360, CookieContainer cookies)
        {
            var section = echo360["section"];
            var sectionUUID = section["uuid"];
            var unit = section["course"]["identifier"].ToString();
            var unitName = section["course"]["name"].ToString().Split('[')[0];
            var presentations = section["presentations"]["pageContents"];
            List<Task> tasks = new List<Task>();

            for (var i = 0; i < presentations.Count(); i++)
            {
                var e = new Echo();
                var uuid = presentations[i]["uuid"].ToString();
                e.unit = unit;
                e.unitName = unitName;
                e.episode = presentations.Count() - i;
                if (!c.echoes.ContainsKey(uuid))
                    tasks.Add(PopulateEchoAsync(e, (JObject)presentations[i], cookies, echoBase, uuid));     
            }
            await Task.WhenAll(tasks);
        }

        async Task PopulateEchoAsync(Echo e, JObject presentation, CookieContainer cookies, string echoBase, string uuid)
        {
            try
            {
                HttpClientHandler handler = new HttpClientHandler { CookieContainer = cookies };
                using (HttpClient client = new HttpClient(handler))
                {
                    (e.contentDir, e.url) = await getPresentationDirsAsync(client, echoBase, uuid);
                    var doc = XDocument.Parse(await (await client.GetAsync($"{ e.contentDir }presentation.xml")).Content.ReadAsStringAsync());
                    e.venue = doc.Descendants("location").FirstOrDefault().Value;
                }
                e.duration = presentation["durationMS"].ToObject<long>();
                e.description = presentation["title"].ToString();
                e.date = presentation["startTime"].ToObject<DateTime>();
                e.title = string.Format("{0:MMMM d}{1} ({2:dddd})", e.date, GetDateSuffix(e.date.Day), e.date);
                e.thumbnail = presentation["thumbnails"].Select(t => t.ToString()).FirstOrDefault(t => t.Contains("low"));
                //if no thumbnail use audio file as there is no video (very rare)
                if (e.thumbnail == null) e.url = $"{ e.contentDir }audio.mp3";
                c.echoes[uuid] = e;
            }
            catch (Exception ex)
            {
                logger.LogWarning("Failed to add echo {uuid}", uuid);
                logger.LogDebug(ex.StackTrace);
            }

        }

        async Task<(string, string)> getPresentationDirsAsync(HttpClient client, string echoBase, string uuid)
        {
            var parser = new HtmlParser();
            var presentation = parser.Parse(await (await client.GetAsync(echoBase + "/ess/echo/presentation/" + uuid)).Content.ReadAsStringAsync());
            var requestUri = new Uri(presentation.QuerySelector("iframe").Attributes["src"].Value);
            var queryDictionary = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(requestUri.Query);
            var contentDir = queryDictionary["contentDir"];
            var streamDir = new Uri(queryDictionary["streamDir"]);
            var url = $"http://{ streamDir.Host }:1935{ streamDir.PathAndQuery }mp4:audio-vga-streamable.m4v/playlist.m3u8";
            return (contentDir, url);
        }

        static string GetDateSuffix(int day)
        {
            switch (day)
            {
                case 1: case 21: case 31:
                    return ("st");
                case 2: case 22:
                    return ("nd");
                case 3: case 23:
                    return ("rd");
                default:
                    return ("th");
            }
        }
    }
}
