using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Http;
using Newtonsoft.Json;
using System.Net;
using Microsoft.Extensions.Logging;

namespace echoDownloader
{
    class BlackboardConnector
    {
        private ILogger logger { get; } = Cli.LoggerFactory.CreateLogger<BlackboardConnector>();
        public readonly string blackboard = "https://lms.uwa.edu.au";
        readonly string SSO = "https://sso.uwa.edu.au/siteminderagent/forms/uwalogin.fcc";
        readonly string smagentname = "GCS23xIxgS7fdRlcwRobZbcKcGiz2HARAVD4LGRn6JtGwfdc1G0BnNt9BwOjIZgtJ9SRUO+9A7TRrHTxoGYqXK0A3Vgwllbu";
        readonly string target = "HTTPS://blackboardsso.webservices.uwa.edu.au/BlackBoardSSO.aspx?env=prod";

        private string username;
        private string password;
        public CookieCollection cookies;

        public BlackboardConnector(string username, string password)
        {
            this.username = username;
            this.password = password;
            cookies = new CookieCollection();
        }

        public async Task<bool> loginLMSAsync()
        {
            HttpClientHandler handler = new HttpClientHandler();
            using (HttpClient client = new HttpClient(handler))
            {
                var loginRequest = new HttpRequestMessage(HttpMethod.Post, SSO);

                loginRequest.Content = new FormUrlEncodedContent(new[] {
                    new KeyValuePair<string, string>("PASSWORD", password),
                    new KeyValuePair<string, string>("USER", username),
                    new KeyValuePair<string, string>("smagentname", smagentname),
                    new KeyValuePair<string, string>("target", target)
                });
                bool loggedin = (await client.SendAsync(loginRequest)).Headers.Any(h => h.Key.ToLowerInvariant().Contains("blackboard"));
                if (loggedin)
                {
                    cookies.Add(handler.CookieContainer.GetCookies(new Uri(blackboard)));
                    logger.LogInformation("Logged into Blackboard with username {username}", username);
                    return true;
                }
            }
            logger.LogWarning("Failed to login to Blackboard with username {username}", username);
            return false;  
        }

        public async Task<Dictionary<string, string>> loadUnitsAsync()
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            Dictionary<string, string> units = new Dictionary<string, string>();
            HttpClientHandler handler = new HttpClientHandler();
            foreach (Cookie cookie in cookies)
                handler.CookieContainer.Add(new Uri(blackboard), cookie);
            try
            {
                using (HttpClient client = new HttpClient(handler))
                {
                    //Get course memberships
                    var membershipsJSON = await (await client.GetAsync($"{ blackboard }/learn/api/public/v1/users/userName:{ username }/courses?&limit=200")).Content.ReadAsStringAsync();
                    dynamic memberships = JsonConvert.DeserializeObject(membershipsJSON);
                    //Get courses
                    foreach (var membership in memberships.results)
                    {
                        if (membership.created.Value.Year == DateTime.Now.Year)
                        {
                            var courseJSON = await (await client.GetAsync($"{ blackboard }/learn/api/public/v1/courses/{ membership.courseId }")).Content.ReadAsStringAsync();
                            dynamic course = JsonConvert.DeserializeObject(courseJSON);
                            var end = course.availability.duration.end;
                            //only add courses that haven't ended
                            if (end != null && DateTime.Now < end.Value)
                                units.Add(course.id.Value, course.courseId.Value);
                        }
                    }
                }
                watch.Stop();
                logger.LogDebug("Loading units took {ms}ms", watch.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                logger.LogError("Failed to load units.");
            }

            return units;

        }
    }
}
