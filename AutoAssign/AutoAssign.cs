
using Newtonsoft.Json;
using System.Text;
using static System.Net.WebRequestMethods;

namespace AutoAssign
{
    // To get game requests, we can use an undocumented API call:
    // https://littlemountainbaseball.assignr.com/assign/games/{gameId}/edit
    // This returns a small form in HTML format, but it's easily parsed.  Goto form->table->Pending Requests->find the tag data-game-id="{gameId}" data-assignment-id="{out_requestId_Plate}" e.g. 77199786
    // Then, we can do a get on this URL: https://littlemountainbaseball.assignr.com/assign/assignments/77199786.json
    // returns a JSON object with all users, each one indicating whether they've submitted a request for that game.  All the people who requested the game are listed at the top.
    // There isn't a public API to assign an official, but there is one to unassign all officials: /v2/games/{id}/unassign
    // To assign an official: 
    // PUT https://littlemountainbaseball.assignr.com/assign/games/{gameId}
    // Have to pass a User ID (mine is 340011)
    // As well as the assignment Id (e.g. 77267314)
    // It's not clear to me how these parameters are being passed back to the service from the stream I can see in Chrome

    internal class Game
    {
        public Game()
        {
            localized_date = string.Empty;
            localized_time = string.Empty;

            age_group = string.Empty;
            cancelled = false;
            published = false;
            details = new();
        }

        public string localized_date { get; set; }
        public string localized_time { get; set; }

        public string age_group { get; set; }
        public bool cancelled { get; set; }
        public bool published { get; set; }

        [JsonProperty("_embedded")]
        public GameInfo details { get; set; }
    }

    internal class GameInfo
    {
        public GameInfo()
        {
            assignments = new();
            venue = new();
        }

        public List<Assignment> assignments { get; set; }

        public Venue venue { get; set; }
    }

    internal class Assignment
    {
        public Assignment()
        {
            id = 0;
            assigned = false;

            position = string.Empty;
        }

        public int id { get; set; }
        public bool assigned { get; set; }
        public string position { get; set; }
    }


    internal class Page
    {
        public int pages { get; set; }
    }

    internal class Venue
    {
        public Venue()
        {
            name = string.Empty;
        }
        public string name { get; set; }
    }

    internal class GamesList
    {
        public GamesList()
        {
            games = new();
        }
        public List<Game> games { get; set; }
    }

    internal class GamesResponse
    {
        public GamesResponse()
        {
            page = new();
            games = new();
        }

        public Page page { get; set; }

        [JsonProperty("_embedded")]
        public GamesList games { get; set; }
    }

    internal class User
    {
        public User() {
            name = string.Empty;
            request = false;
        }

        public string name { get; set; }
        public bool request { get; set; }
    }

    internal class AssignmentsResponse
    {
        public AssignmentsResponse() {
            users = new();
        }

        public List <User> users { get; set; }
    }

    public class Assignr
    {
        public Assignr()
        {
            mHttpClient = new();
        }

        public void Authenticate(Google.Apis.Auth.OAuth2.ClientSecrets? credentials, string assignrSessionToken)
        {
            if (credentials == null)
            {
                throw new ArgumentException("credentials cannot be null");
            }

            mHttpClient.DefaultRequestHeaders.Clear();
            mHttpClient.DefaultRequestHeaders.Add("cache-control", "no-cache");

            var oauthHeaders = new Dictionary<string, string> {
                {"client_id", credentials.ClientId ?? string.Empty},
                {"client_secret", credentials.ClientSecret ?? string.Empty},
                {"scope", "read write" },
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

            Token? tok = JsonConvert.DeserializeObject<Token>(jsonContent);

            if (tok == null)
            {
                throw new Exception($"Unexpected response from the Assignr service: {jsonContent}");
            }

            mBearerToken = tok.AccessToken;
            mSessionToken = assignrSessionToken;
        }

        public string HttpMessage(HttpMethod method, string uri, bool useSessionToken = false)
        {
            HttpRequestMessage msg = new HttpRequestMessage(method, uri);
            msg.Headers.Add("accept", "application/vnd.assignr.v2.hal+json");
            if (useSessionToken) {
                // Using a non-public API
                msg.Headers.Add("Cookie", $"_assignr_session={mSessionToken}");
            }
            else {
                // Using the public API
                msg.Headers.Add("authorization", $"Bearer {mBearerToken}");
            }

            HttpResponseMessage gamesResponse = mHttpClient.Send(msg);

            if (gamesResponse.StatusCode != System.Net.HttpStatusCode.OK)
            {
                throw new Exception("Failed to retrieve game information from Assignr");
            }

            return gamesResponse.Content.ReadAsStringAsync().Result;
        }


        public void FetchUnassignedGames(StreamWriter logger, string siteId)
        {
            int totalPages = int.MaxValue;
            int currentPage = 1;

            while (currentPage <= totalPages)
            {
                string gamesUri = $"https://api.assignr.com/api/v2/sites/{siteId}/games/unassigned?page={currentPage}&search[start_date]={DateTime.Today.ToString("yyyy-MM-dd")}&search[end_date]={DateTime.Today.AddDays(14).ToString("yyyy-MM-dd")}";

                string jsonResponse = HttpMessage(HttpMethod.Get, gamesUri);

                //JsonSerializerSettings settings = new();
                //settings.DateParseHandling = DateParseHandling.None;
                GamesResponse jsonRoot = JsonConvert.DeserializeObject<GamesResponse>(jsonResponse) ?? new();

                if (jsonRoot == null)
                {
                    throw new Exception("Unexpected response from Assignr service");
                }

                totalPages = jsonRoot.page.pages;

                logger.WriteLine("Age Group,Date,Time,Venue,Position,Req1,Req2,Req3,Req4,Req5,Req6,Req7,Req8,Req9,Req10,Req11,Req12,Req13,Req14,Req15,Req16,Req17,Req18,Req19,Req20");

                foreach (Game game in jsonRoot.games.games)
                {
                    if (!game.published || game.cancelled)
                    {
                        continue;
                    }

                    foreach (Assignment assignment in game.details.assignments)
                    {
                        if (assignment.assigned)
                        {
                            // ignore any positions that have already been assigned.  The API still returns the game
                            // if at least one position is still unassigned.
                            continue;
                        }

                        logger.Write($"{game.age_group},{game.localized_date},{game.localized_time},{game.details.venue.name},{assignment.position}");

                        // Assignr's API doesn't allow us to retrieve game requests, which seriously sucks.  I can still get them
                        // using a web session though.
                        string assignmentsUri = $"https://littlemountainbaseball.assignr.com/assign/assignments/{assignment.id}.json";

                        string assignmentsResponse = HttpMessage(HttpMethod.Get, assignmentsUri, true);

                        AssignmentsResponse assignments = JsonConvert.DeserializeObject<AssignmentsResponse>(assignmentsResponse) ?? new();

                        // assignments.users.ForEach();
                        int reqCount = 0;
                        foreach(User user in assignments.users)
                        {
                            if (user.request) {
                                logger.Write($",\"{user.name}\"");
                                ++reqCount;
                            }
                        }

                        if (reqCount > 20)
                        {
                            throw new Exception("More than 20 requests were received for this game.  Increase the max count");
                        }

                        string extraCommas = string.Empty;
                        for (; reqCount < 20; ++reqCount)
                        {
                            extraCommas += ",";
                        }

                        // I may need to add commas
                        logger.WriteLine(extraCommas);

                    }

                    // get a list of requests for each unassigned position
                }

                ++currentPage;
            }
        }
        HttpClient mHttpClient;
        string? mBearerToken;
        string? mSessionToken;
    }

    internal partial class AutoAssign
    {
        static int Main(string[] args)
        {
            Credentials? credentials;
            using (StreamReader reader = new StreamReader("../Shared/credentials.json")) {
                credentials = JsonConvert.DeserializeObject<Credentials>(reader.ReadToEnd());
            }

            if (credentials == null) {
                throw new Exception("Failed to read the service credentials");
            }

            StreamWriter logger = new StreamWriter("gamesToAssign.csv", false);

            Assignr assignr = new();
            assignr.Authenticate(credentials.Assignr, credentials.AssignrSessionToken);

            string ASSIGNR_ID_LMB = "627";

            // Get all the games that are published and need to be assigned
            assignr.FetchUnassignedGames(logger, ASSIGNR_ID_LMB);

            logger.Close();


            return 0;
        }
    }
}
