using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VcbFieldExport;
using System.Collections;

namespace VcbFieldExport
{
    internal class Token
    {
        Token()
        {
            AccessToken = string.Empty;
            TokenType = string.Empty;
            ExpiresIn = 0;
            Scope = string.Empty;
            Created = 0;
        }

        [JsonProperty("access_token")]
        public string AccessToken { get; set; }

        [JsonProperty("token_type")]
        public string TokenType { get; set; }

        [JsonProperty("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonProperty("scope")]
        public string Scope { get; set; }
        [JsonProperty("created_at")]
        public int Created { get; set; }
    }

    internal class AssignrCredentials
    {
        public string? client_id { get; set; }
        public string? client_secret { get; set; }
    }

    public class AssignrEvents
    {
        public AssignrEvents() {
            mHttpClient = new();
        }

        public void Authenticate()
        {
            AssignrCredentials? credentials;
            using (StreamReader reader = new StreamReader("assignr_credentials.json"))
            {
                string json = reader.ReadToEnd();
                credentials = System.Text.Json.JsonSerializer.Deserialize<AssignrCredentials>(json);

                if (credentials == null)
                {
                    throw new Exception("Unable to deserialize the Assignr credentials");
                }
            }

            mHttpClient.DefaultRequestHeaders.Clear();
            mHttpClient.DefaultRequestHeaders.Add("cache-control", "no-cache");

            var oauthHeaders = new Dictionary<string, string> {
                {"client_id", credentials.client_id},
                {"client_secret", credentials.client_secret},
                {"scope", "read" },
                {"grant_type", "client_credentials"},
              };

            string oauthUri = @"https://app.assignr.com/oauth/token";

            // Get a Bearer token for further API requests
            HttpResponseMessage tokenResponse = mHttpClient.PostAsync(oauthUri, new FormUrlEncodedContent(oauthHeaders)).Result;
            string jsonContent = tokenResponse.Content.ReadAsStringAsync().Result;
            if (string.IsNullOrEmpty(jsonContent))
            {
                throw new Exception($"Unexpected response from the Assignr service: {jsonContent}");
            }
            Token tok = JsonConvert.DeserializeObject<Token>(jsonContent);

            if (tok == null)
            {
                throw new Exception($"Unexpected response from the Assignr service: {jsonContent}");
            }

            mBearerToken = tok.AccessToken;
        }

        public void FetchEventsFromService()
        {
            int totalPages = -1;
            int currentPage = 0;

            while (currentPage != totalPages)
            {
                string VCB_SITE_ID = "12381";
                //string site_LMB = "627";
                //string site_Kerrisdale = "6561";
                //string site_Richmond = "19639";

                string start_date = DateTime.Now.ToString("yyyy-MM-dd");

                string gamesUri = $"https://api.assignr.com/api/v2/sites/{VCB_SITE_ID}/games?page={currentPage + 1}&limit=50&search[start_date]={start_date}";

                HttpRequestMessage msg = new HttpRequestMessage(HttpMethod.Get, gamesUri);
                msg.Headers.Add("accept", "application/json");
                msg.Headers.Add("authorization", $"Bearer {mBearerToken}");

                HttpResponseMessage gamesResponse = mHttpClient.Send(msg);

                if (gamesResponse.StatusCode != System.Net.HttpStatusCode.OK)
                {
                    throw new Exception("Failed to retrieve game information from Assignr");
                }

                string result = gamesResponse.Content.ReadAsStringAsync().Result;

                // Debugging
                // string result = File.ReadAllText("assignr_response.json");

                JObject? jsonRoot = JsonConvert.DeserializeObject(result) as JObject;

                if (totalPages == -1)
                {
                    JToken? a = jsonRoot["page"]["pages"];
                    totalPages = int.Parse(a.ToString());
                }

                JArray gameList = jsonRoot["_embedded"]["games"] as JArray;

                foreach (JObject game in gameList)
                {
                    string startTime = game["start_time"].ToString();
                    string ageGroup = game["age_group"].ToString();
                    string homeTeam = game["home_team"].ToString();
                    string awayTeam = game["away_team"].ToString();
                    string assignrVenue = game["_embedded"]["venue"]["name"].ToString();
                    string gameType = game["game_type"].ToString();

                    if (gameType == "Playoffs") {
                        continue;       // Ignore playoff games until the placeholders are added to TeamSnap.
                    }

                    if (ageGroup == "Mentor") {
                        // This is a placeholder for an umpire mentor assignment, it doesn't represent a game
                        continue;
                    }

                    if (ageGroup == "13U A")
                    {
                        homeTeam = "VCB 13U " + homeTeam;

                        if (awayTeam == "Dodgers" || awayTeam == "Blue Jays" || awayTeam == "Expos" || awayTeam == "Red Sox" || awayTeam == "Diamondbacks")
                        {
                            awayTeam = "VCB 13U " + awayTeam;
                        }
                        // The visiting team is a non-VCB team, TeamSnap & Assignr should have the same name
                    }
                    else if (ageGroup == "15U A")
                    {
                        if (ASSIGNR_TO_TEAMSNAP_NAMEMAP[ageGroup].ContainsKey(homeTeam))
                        {
                            homeTeam = ASSIGNR_TO_TEAMSNAP_NAMEMAP[ageGroup][homeTeam];
                        }
                        else
                        {
                            homeTeam = "VCB 15U " + homeTeam;
                        }

                        if (ASSIGNR_TO_TEAMSNAP_NAMEMAP[ageGroup].ContainsKey(awayTeam))
                        {
                            awayTeam = ASSIGNR_TO_TEAMSNAP_NAMEMAP[ageGroup][awayTeam];
                        }
                        else
                        {
                            awayTeam = "VCB 15U " + awayTeam;
                        }
                    }
                    else if (ageGroup == "18U AA")
                    {
                        homeTeam = "VCB 18U " + homeTeam;
                        awayTeam = "VCB 18U " + awayTeam;
                    }
                    else if (ASSIGNR_TO_TEAMSNAP_NAMEMAP.ContainsKey(ageGroup))
                    {
                        if (ASSIGNR_TO_TEAMSNAP_NAMEMAP[ageGroup].ContainsKey(homeTeam))
                        {
                            homeTeam = ASSIGNR_TO_TEAMSNAP_NAMEMAP[ageGroup][homeTeam];
                        }

                        if (ASSIGNR_TO_TEAMSNAP_NAMEMAP[ageGroup].ContainsKey(awayTeam))
                        {
                            awayTeam = ASSIGNR_TO_TEAMSNAP_NAMEMAP[ageGroup][awayTeam];
                        }
                        // The visiting team is a non-VCB team, TeamSnap & Assignr should have the same name
                    }
                    else
                    {
                        throw new Exception($"Unhandled age group {ageGroup} was sent by Assignr.  The program needs to be updated to handle this.");
                    }

                    DateTime start = DateTime.Parse(startTime);

                    if (!ASSIGNR_TO_TEAMSNAP_VENUE_MAP.ContainsKey(assignrVenue))
                    {
                        // Console.WriteLine($"Warning: A game in Assignr is hosted on a field not tracked in TeamSnap: {assignrVenue} on {startTime}.");
                        continue;
                    }

                    string teamSnapVenue = ASSIGNR_TO_TEAMSNAP_VENUE_MAP[assignrVenue];

                    if (IGNORED_GAMES.Find(e => e.location == teamSnapVenue && e.startTime == start) != null) {
                        continue;
                    }

                    mGames.Add(new VcbFieldEvent(VcbFieldEvent.Type.Game, teamSnapVenue, start, homeTeam, awayTeam, start.AddHours(2)));
                }

                currentPage = int.Parse(jsonRoot["page"]["current_page"].ToString());
            }
        }

        public int Reconcile(List<VcbFieldEvent> teamSnapGames)
        {
            int inconsistentGames = 0;

            foreach (var game in teamSnapGames) {
                VcbFieldEvent e = mGames.Find(e =>
                    e.location == game.location &&
                    e.startTime == game.startTime &&
                    e.homeTeam == game.homeTeam &&
                    e.visitingTeam == game.visitingTeam);

                if (e != null) {
                    mGames.Remove(e);
                }
                else if (IGNORED_GAMES.Find(e => e.location == game.location && e.startTime == game.startTime) == null) {
                    ++inconsistentGames;
                    Console.WriteLine($"A game in TeamSnap is missing from Assignr: {game.startTime} at {game.location} ({game.visitingTeam} @ {game.homeTeam}).");
                }
            }

            foreach (var game in mGames) {
                if (IGNORED_GAMES.Find(e => e.location == game.location && e.startTime == game.startTime) == null) {
                    ++inconsistentGames;
                    Console.WriteLine($"A game in Assignr is missing from TeamSnap: {game.startTime} at {game.location} ({game.visitingTeam} @ {game.homeTeam}).");
                }
            }

            return inconsistentGames;
        }

        HttpClient mHttpClient;
        string? mBearerToken;

        List<VcbFieldEvent> IGNORED_GAMES = new List<VcbFieldEvent> {
            new VcbFieldEvent(VcbFieldEvent.Type.Game, "Hillcrest Park SW diamond", new DateTime(2025, 04, 5, 10, 0, 0), "", "", DateTime.Now), // Mike Marlatt says the teams don't need umpires for this practice game
            new VcbFieldEvent(VcbFieldEvent.Type.Game, "Hillcrest Park SW diamond", new DateTime(2025, 04, 5, 12, 0, 0), "", "", DateTime.Now), // Same game as above, but in the Blue team's schedule
        };

        List<VcbFieldEvent> mGames = new();

        Dictionary<string, string> ASSIGNR_TO_TEAMSNAP_VENUE_MAP = new Dictionary<string, string> {
                { "Chaldecott North", "Chaldecott Park N diamond" },
                { "Chaldecott South", "Chaldecott Park S diamond" },
                { "Hillcrest North", "Hillcrest Park NE diamond" },
                { "Hillcrest South", "Hillcrest Park SW diamond" },
                { "Nanaimo North", "Nanaimo Park N diamond" },
                { "Nanaimo South East", "Nanaimo Park SE diamond" },
                { "Trafalgar", "Trafalgar Park" }
            };

        Dictionary<string, Dictionary<string, string>> ASSIGNR_TO_TEAMSNAP_NAMEMAP = new Dictionary<string, Dictionary<string, string>> {
            { "13U AA", new Dictionary<string, string> {
                { "Vancouver Mounties", "VCB 13U AA" }, }
            },
            { "13U AAA", new Dictionary<string, string> {
                { "Vancouver Mounties", "VCB 13U AAA" }, }
            },
            { "15U A", new Dictionary<string, string> {
                { "RS-Girls", "VCB 16U Red Sox-Girls" },
                { "Girls 1", "VCB 16U Red Sox-Girls" },
                { "Girls 2", "15U Girls" },
                { "TBD", "15U Girls" }, }
            },
            { "15U AA", new Dictionary<string, string>{
                { "Vancouver Mounties Blue", "VCB Expos 15U AA Blue" },
                { "Vancouver Mounties Red", "VCB 15U AA Red" }, }
            },
            { "15U AAA", new Dictionary<string, string> {
                { "Vancouver Mounties", "VCB 15U AAA"}, }
            },
            { "18U AAA", new Dictionary<string, string> {
                { "Vancouver Mounties Blue", "VCB 18U AAA Blue" },
                { "Vancouver Mounties White", "VCB 18U AAA White" },
                { "SOMBA", "Penticton Tigers" }, }
            },
        };
    }
}
