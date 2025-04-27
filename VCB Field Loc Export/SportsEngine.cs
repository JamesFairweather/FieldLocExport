using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Text;

namespace VcbFieldExport
{
    internal class OAuthCredentials
    {
        public OAuthCredentials()
        {
            Id = string.Empty;
            Secret = string.Empty;
        }

        [JsonProperty("client_id")]
        public string Id { get; set; }

        [JsonProperty("client_secret")]
        public string Secret { get; set; }
    }

    internal class Data
    {
        public Data() {
            events = new();
        }

        public Events events { get; set; }
    }

    internal class Events
    {
        public Events() {
            pageInformation = new();
            results = new();
        }

        public PageInformation pageInformation { get; set; }
        public List<Result> results { get; set; }
    }

    internal class EventTeam
    {
        public EventTeam()
        {
            team = new();
            homeTeam = true;
        }

        public Team team { get; set; }
        public bool homeTeam { get; set; }
    }

    internal class Location
    {
        public Location() {
            name = string.Empty;
        }

        public string name { get; set; }
    }

    internal class PageInformation
    {
        public int pages { get; set; }
        public int count { get; set; }
        public int page { get; set; }
        public int perPage { get; set; }
    }

    internal class Result
    {
        Result()
        {
            name = string.Empty;
            type = string.Empty;
            location = new();
            eventTeams = new();
        }

        public string name { get; set; }
        public string type { get; set; }
        public DateTime start { get; set; }
        public DateTime end { get; set; }
        public Location location { get; set; }
        public List<EventTeam> eventTeams { get; set; }
    }

    internal class EventQueryResult
    {
        public EventQueryResult() {
            data = new();
        }

        public Data data { get; set; }
    }

    internal class Team
    {
        public Team() {
            name = string.Empty; 
        }

        public string name { get; set; }
    }

    internal class SportsEngine
    {
        public SportsEngine() {
            mHttpClient = new();
        }

        public void authenticate() {
            OAuthCredentials? credentials;
            using (StreamReader reader = new StreamReader("sportsengine.json"))
            {
                string json = reader.ReadToEnd();
                credentials = JsonConvert.DeserializeObject<OAuthCredentials>(json);

                if (credentials == null)
                {
                    throw new Exception("Unable to deserialize the SportsEngine credentials");
                }
            }

            string authUrl = $"https://user.sportsengine.com/oauth/token";

            mHttpClient.DefaultRequestHeaders.Clear();
            mHttpClient.DefaultRequestHeaders.Add("cache-control", "no-cache");

            var oauthHeaders = new Dictionary<string, string> {
                {"client_id", credentials.Id},
                {"client_secret", credentials.Secret},
                {"grant_type", "client_credentials"},
              };

            string oauthUri = "https://user.sportsengine.com/oauth/token";

            // Get a Bearer token for further API requests
            HttpResponseMessage tokenResponse = mHttpClient.PostAsync(oauthUri, new FormUrlEncodedContent(oauthHeaders)).Result;
            string jsonContent = tokenResponse.Content.ReadAsStringAsync().Result;

            if (string.IsNullOrEmpty(jsonContent)) {
                throw new Exception($"Unexpected response from the Assignr service: {jsonContent}");
            }

            Token? tok = JsonConvert.DeserializeObject<Token>(jsonContent);

            if (tok == null) {
                throw new Exception($"Unexpected response from the Assignr service: {jsonContent}");
            }

            if (tok.TokenType != "bearer") {
                throw new Exception($"We expected the returned token to be a bearer token.  But it is {tok.TokenType} instead.");
            }

            mBearerToken = tok.AccessToken;
        }

        public void fetchEvents()
        {
            string LMB_ORGANIZATION_ID = "144187";  // from the query above

            string pageInfo = @"pageInformation { pages count page perPage }";
            string results = @"results { name type start end location { name } eventTeams { team { name } homeTeam } } }";

            int pageNumber = 1;
            int eventCount = 0;

            while (true) {

                // If we want to put these events on a public calendar, we'll need GAMEs and EVENTs for the calendarEventType
                string query = $"query events {{ events( organizationId: {LMB_ORGANIZATION_ID} from: \\\"{DateTime.Today:O}\\\" perPage: 100 page: {pageNumber} calendarEventType: GAME) {{ {pageInfo} {results} }}";

                string uri = "https://api.sportsengine.com/graphql";
                HttpRequestMessage msg = new HttpRequestMessage(HttpMethod.Post, uri);
                msg.Headers.Add("authorization", $"Bearer {mBearerToken}");
                msg.Content = new StringContent($"{{ \"query\": \"{query}\", \"operationName\": \"events\" }}", Encoding.UTF8, "application/json");

                HttpResponseMessage gamesResponse = mHttpClient.Send(msg);

                if (gamesResponse.StatusCode != System.Net.HttpStatusCode.OK)
                {
                    throw new Exception("Failed to retrieve game information from SportsEngine");
                }
                string jsonResponse = gamesResponse.Content.ReadAsStringAsync().Result;

                // The default behavior of the JSON deserializer is to convert datetime strings, but it appears to have a
                // bug because it's ignoring the "Z" suffix, which means the timestamp is in UTC, not local
                JsonSerializerSettings settings = new();
                settings.DateParseHandling = DateParseHandling.None;
                EventQueryResult eventQueryResult = JsonConvert.DeserializeObject<EventQueryResult>(jsonResponse) ?? new EventQueryResult();

                eventCount = eventQueryResult.data.events.pageInformation.count;

                if (pageNumber != eventQueryResult.data.events.pageInformation.page) {
                    throw new Exception("Unexpected page returned");
                }

                foreach (Result e in eventQueryResult.data.events.results) {
                    string homeTeam = e.eventTeams[0]?.team?.name ?? "TBD";
                    string visitingTeam = e.eventTeams[1]?.team?.name ?? "TBD";
                    bool officialsRequired = homeTeam.StartsWith("MINB") || homeTeam.StartsWith("MINA") || homeTeam.StartsWith("MAJ");

                    mGames.Add(new VcbFieldEvent(e.location.name ?? "TBD", e.start, string.Empty, homeTeam, visitingTeam, officialsRequired));
                }

                if (mGames.Count == eventCount) {
                    break;  // while(true} loop exit
                }

                ++pageNumber;
            }
        }

        public List<VcbFieldEvent> getGames() {
            return mGames;
        }

        List<VcbFieldEvent> mGames = new();

        HttpClient mHttpClient;
        string? mBearerToken;
    }
}
