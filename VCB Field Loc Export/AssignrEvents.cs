using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using static Google.Apis.Requests.BatchRequest;
using System.Xml.Linq;

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
        public AssignrCredentials()
        {
            Id = string.Empty;
            Secret = string.Empty;
        }

        [JsonProperty("client_id")]
        public string Id { get; set; }

        [JsonProperty("client_secret")]
        public string Secret { get; set; }
    }

    internal class Game
    {
        public Game()
        {
            start_time = new();
            age_group = string.Empty;
            home_team = string.Empty;
            away_team = string.Empty;
            game_type = string.Empty;
            cancelled = false;
            public_note = string.Empty;
            _embedded = new();
        }
        public DateTime start_time { get; set; }
        public string age_group { get; set; }
        public string home_team { get; set; }
        public string away_team { get; set; }
        public string game_type { get; set; }
        public bool cancelled { get; set; }
        public string public_note { get; set; }
        public Embedded _embedded { get; set; }
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

    internal class Embedded
    {
        public Embedded() {
            games = new();
            venue = new();
        }
        public List<Game> games { get; set; }
        public Venue venue { get; set; }
    }

    internal class AssignrResponseRoot
    {
        public AssignrResponseRoot()
        {
            page = new();
            _embedded = new();
        }

        public Page page { get; set; }
        public Embedded _embedded { get; set; }
    }

    public class AssignrEvents
    {
        public AssignrEvents(StreamWriter logger) {
            mHttpClient = new();
            mGames = new();
            mLogger = logger;
        }

        public void Authenticate()
        {
            AssignrCredentials? credentials;
            using (StreamReader reader = new StreamReader("assignr_credentials.json"))
            {
                string json = reader.ReadToEnd();
                credentials = JsonConvert.DeserializeObject<AssignrCredentials>(json);

                if (credentials == null)
                {
                    throw new Exception("Unable to deserialize the Assignr credentials");
                }
            }

            mHttpClient.DefaultRequestHeaders.Clear();
            mHttpClient.DefaultRequestHeaders.Add("cache-control", "no-cache");

            var oauthHeaders = new Dictionary<string, string> {
                {"client_id", credentials.Id},
                {"client_secret", credentials.Secret},
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
        }

        public void clearEvents() {
            mGames.Clear();
        }

        public void FetchEventsFromService(string siteId, bool includePlayoffGames)
        {
            int totalPages = int.MaxValue;
            int currentPage = 1;

            while (currentPage <= totalPages)
            {
                string gamesUri = $"https://api.assignr.com/api/v2/sites/{siteId}/games?page={currentPage}&search[start_date]={DateTime.Today.ToString("yyyy-MM-dd")}";

                HttpRequestMessage msg = new HttpRequestMessage(HttpMethod.Get, gamesUri);
                msg.Headers.Add("accept", "application/vnd.assignr.v2.hal+json");
                msg.Headers.Add("authorization", $"Bearer {mBearerToken}");

                HttpResponseMessage gamesResponse = mHttpClient.Send(msg);

                if (gamesResponse.StatusCode != System.Net.HttpStatusCode.OK)
                {
                    throw new Exception("Failed to retrieve game information from Assignr");
                }

                string jsonResponse = gamesResponse.Content.ReadAsStringAsync().Result;

                JsonSerializerSettings settings = new();
                settings.DateParseHandling = DateParseHandling.None;
                AssignrResponseRoot jsonRoot = JsonConvert.DeserializeObject<AssignrResponseRoot>(jsonResponse) ?? new();

                if (jsonRoot == null) {
                    throw new Exception("Unexpected response from Assignr service");
                }

                totalPages = jsonRoot.page.pages;

                foreach (Game game in jsonRoot._embedded.games)
                {
                    string homeTeam = game.home_team;
                    string awayTeam = game.away_team;
                    string age_group = game.age_group ?? string.Empty;

                    if (game.cancelled) {
                        continue;
                    }

                    if (age_group == "Mentor") {
                        // This is a placeholder for an umpire mentor assignment, it doesn't represent a game
                        continue;
                    }

                    if (age_group == "8U") {
                        // Little Mountain has this age group, which we will ignore
                        continue;
                    }

                    if (!includePlayoffGames && game.game_type == "Playoffs") {
                        // Ignore playoff games for Little Mountain, these games are not in SportsEngine
                        continue;
                    }

                    string division = game.age_group ?? string.Empty;

                    if (age_group == "13U A")
                    {
                        try {
                            homeTeam = ASSIGNR_TO_PUBLIC_NAMEMAP[age_group][homeTeam];
                        }
                        catch (KeyNotFoundException) {
                            // homeTeam name is already correct
                        }

                        try {
                            awayTeam = ASSIGNR_TO_PUBLIC_NAMEMAP[age_group][awayTeam];
                        }
                        catch (KeyNotFoundException) {
                            // awayTeam name is already correct
                        }
                    }
                    else if (age_group == "15U A")
                    {
                        try {
                            homeTeam = ASSIGNR_TO_PUBLIC_NAMEMAP[age_group][homeTeam];
                        }
                        catch (KeyNotFoundException) {
                            homeTeam = "VCB 15U " + homeTeam;
                        }

                        try {
                            awayTeam = ASSIGNR_TO_PUBLIC_NAMEMAP[age_group][awayTeam];
                        }
                        catch (KeyNotFoundException) {
                            awayTeam = "VCB 15U " + awayTeam;
                        }
                    }
                    else if (game.age_group == "18U AA")
                    {
                        homeTeam = "VCB 18U " + homeTeam;
                        awayTeam = "VCB 18U " + awayTeam;
                    }
                    else if (ASSIGNR_TO_PUBLIC_NAMEMAP.ContainsKey(age_group))
                    {
                        try {
                            homeTeam = ASSIGNR_TO_PUBLIC_NAMEMAP[age_group][homeTeam];
                        }
                        catch (KeyNotFoundException) {
                            // homeTeam name is already correct
                        }

                        try {
                            awayTeam = ASSIGNR_TO_PUBLIC_NAMEMAP[age_group][awayTeam];
                        }
                        catch (KeyNotFoundException) {
                            // awayTeam name is already correct
                        }

                        // The visiting team is a non-VCB team, TeamSnap & Assignr should have the same name
                    }
                    else
                    {
                        throw new Exception($"Unhandled age group {game.age_group} was sent by Assignr.  The program needs to be updated to handle this.");
                    }

                    string description = string.Empty;
                    VcbFieldEvent.Type gameType = VcbFieldEvent.Type.Game;
                    if (game.game_type == "Playoffs") {
                        description = game.public_note;
                        gameType = VcbFieldEvent.Type.PlayoffGame;
                    }

                    if (!ASSIGNR_TO_TEAMSNAP_VENUE_MAP.ContainsKey(game._embedded.venue.name))
                    {
                        // mLogger.WriteLine($"Warning: A game in Assignr is hosted on a field not tracked publically: {game._embedded.venue.name} on {startTime}.");
                        continue;
                    }

                    string teamSnapVenue = ASSIGNR_TO_TEAMSNAP_VENUE_MAP[game._embedded.venue.name];
                    DateTime start = game.start_time.ToUniversalTime();

                    if (IGNORED_GAMES.Find(e => e.location == teamSnapVenue && e.startTime == start) != null) {
                        continue;
                    }

                    mGames.Add(new VcbFieldEvent(gameType, teamSnapVenue, start, division, homeTeam, awayTeam, description, true));
                }

                ++currentPage;
            }
        }

        public List<VcbFieldEvent> getGames() { return mGames; }

        public int Reconcile(List<VcbFieldEvent> teamGames)
        {
            int inconsistentGames = 0;

            mLogger.WriteLine("Reconciling team & Assignr game schedules...");

            foreach (var game in teamGames) {

                if (!game.officialsRequired) {
                    continue;
                }

                VcbFieldEvent? e = mGames.Find(e =>
                    e.location == game.location &&
                    e.startTime == game.startTime &&
                    e.homeTeam == game.homeTeam &&
                    e.visitingTeam == game.visitingTeam);

                if (e != null) {
                    mGames.Remove(e);
                }
                else if (IGNORED_GAMES.Find(e => e.location == game.location && e.startTime == game.startTime) == null) {
                    ++inconsistentGames;
                    mLogger.WriteLine($"A game on the public schedule is not in Assignr: {game.startTime.ToLocalTime().ToString("g")} at {game.location} ({game.visitingTeam} @ {game.homeTeam}).");
                }
            }

            foreach (var game in mGames) {
                if (IGNORED_GAMES.Find(e => e.location == game.location && e.startTime == game.startTime) == null) {
                    ++inconsistentGames;
                    mLogger.WriteLine($"An Assignr game is not on the public game schedule: {game.startTime.ToLocalTime().ToString("g")} at {game.location} ({game.visitingTeam} @ {game.homeTeam}).");
                }
            }

            if (inconsistentGames == 0) {
                mLogger.WriteLine("No inconsistencies were found between Assignr and the public game schedules");
            }

            return inconsistentGames;
        }

        HttpClient mHttpClient;
        StreamWriter mLogger;
        string? mBearerToken;

        readonly List<VcbFieldEvent> IGNORED_GAMES = new List<VcbFieldEvent> {
        };

        List<VcbFieldEvent> mGames;

        Dictionary<string, string> ASSIGNR_TO_TEAMSNAP_VENUE_MAP = new Dictionary<string, string> {
            { "Chaldecott North", "Chaldecott Park N diamond" },
            { "Chaldecott South", "Chaldecott Park S diamond" },
            { "Hillcrest North", "Hillcrest Park NE diamond" },
            { "Hillcrest South", "Hillcrest Park SW diamond" },
            { "Nanaimo North", "Nanaimo Park N diamond" },
            { "Nanaimo South East", "Nanaimo Park SE diamond" },
            { "Trafalgar", "Trafalgar Park" },
            { "Challenger", "Variety Challenger Field" },
            { "Columbia Park", "Columbia Park" },
            { "Hillcrest Park", "Hillcrest Main Diamond" },
            { "Oak Park", "Oak Park North Diamond" },
        };

        Dictionary<string, Dictionary<string, string>> ASSIGNR_TO_PUBLIC_NAMEMAP = new Dictionary<string, Dictionary<string, string>> {
            { "Majors", new Dictionary<string, string> {
                {"Majors Royals", "MAJ1 MAIN ST PHYSIO ROYALS" },
                {"Majors Twins", "MAJ2 HOLBORN TWINS" },
                {"Majors Mariners", "MAJ3 ANTHEM MARINERS" },
                {"Majors Legion", "MAJ4 LEGION" },
                {"Majors Cardinals", "MAJ5 DWELL CARDINALS" },
                {"Majors Rangers", "MAJ6 DAKOTA HOMES RANGERS" },
                {"Majors Angels", "MAJ7 UNCLE BILLS PLUMBING ANGELS" },
            }},
            { "Minors A", new Dictionary<string, string> {
                { "Minor A Orioles", "MINA1 TEAM KERR ORIOLES" },
                { "Minor A Padres", "MINA2 PEA-HESU PADRES" },
                { "Minor A Giants", "MINA3 INSPIRE DENTAL GIANTS" },
                { "Minor A Pirates", "MINA4 BEASTVAN PIRATES" },
                { "Minor A Diamondbacks", "MINA5 Stillwater Counselling DBACKS" },
                { "Minor A Brewers", "MINA6 BREWERS" },
                { "Minor A Rockies", "MINA7 ROCKIES" },
            }},
            { "Minors B", new Dictionary<string, string> {
                { "Minor B Ironpigs", "MINB1 AJ Tigers Ironpigs" },
                { "Minor B Chihuahuas", "MINB2 Chihuahuas" },
                { "Minor B Isotopes", "MINB3 Isotopes" },
                { "Minor B Knights", "MINB4 Pristine Labour Knights" },
                { "Minor B Jumbo Shrimp", "MINB5 Jumbo Shrimp" },
                { "Minor B WooSox", "MINB6 WooSox" },
                { "Minor B Bulls", "MINB7 Bulls" },
                { "Minor B Bees", "MINB8 Bees" },
                { "Minor B Bison", "MINB9 Bison" },
                { "Minor B Canadians", "MINB10 CLEAR HR CANADIANS" },
                { "Minor B River Cats", "MINB11 RIVER CATS" },
            }},
            { "13U A", new Dictionary<string, string> {
                { "Dodgers", "VCB 13U Dodgers" },
                { "Blue Jays", "VCB 13U Blue Jays" },
                { "Diamondbacks", "VCB 13U Diamondbacks" },
                { "Expos", "VCB 13U Expos" },
                { "Vancouver Expos", "VCB 13U Expos" },
                { "Red Sox", "VCB 13U Red Sox" },
            }},
            { "13U AA", new Dictionary<string, string> {
                { "Vancouver Mounties", "VCB 13U AA" },
            }},
            { "13U AAA", new Dictionary<string, string> {
                { "Vancouver Mounties", "VCB 13U AAA" },
            }},
            { "15U A", new Dictionary<string, string> {
                { "Burnaby", "Burnaby 15UA" },
                { "RS-Girls", "VCB 16U Red Sox-Girls" },
                { "Girls 1", "VCB 16U Red Sox-Girls" },
                { "Girls 2", "15U Girls" },
                { "TBD", "15U Girls" },
            }},
            { "15U AA", new Dictionary<string, string>{
                { "Vancouver Mounties Blue", "VCB Expos 15U AA Blue" },
                { "Vancouver Mounties Red", "VCB 15U AA Red" },
                { "Burnaby Braves", "North Burnaby" },
            }},
            { "15U AAA", new Dictionary<string, string> {
                { "Vancouver Mounties", "VCB 15U AAA"},
            }},
            { "18U AAA", new Dictionary<string, string> {
                { "Vancouver Mounties Blue", "VCB 18U AAA Blue" },
                { "Vancouver Mounties White", "VCB 18U AAA White" },
                { "SOMBA", "Penticton Tigers" },
            }},
            { "26U", new Dictionary<string, string> {
                { "Vancouver Mounties", "VCB 26U Mounties" },
                { "Vancouver Expos", "VCB 26U Expos" },
            }},
        };
    }
}
