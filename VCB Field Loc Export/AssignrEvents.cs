using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.ComponentModel;
using System.Xml.Linq;
using static Google.Apis.Requests.BatchRequest;

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

        public void Authenticate(Google.Apis.Auth.OAuth2.ClientSecrets? credentials)
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
                    string division = game.age_group ?? string.Empty;

                    if (game.cancelled) {
                        continue;
                    }

                    if (division == "Mentor") {
                        // This is a placeholder for an umpire mentor assignment, it doesn't represent a game
                        continue;
                    }

                    if (division == "8U") {
                        // Little Mountain has this age group, which we will ignore
                        continue;
                    }

                    if (!includePlayoffGames && game.game_type == "Playoffs") {
                        // Playoff games at Little Mountain are not in SportsEngine, so ignore them
                        continue;
                    }

                    if (game.game_type == "Tournament") {
                        // Tournament games are not in the SportsEngine schedule, so ignore them
                        continue;
                    }

                    // Add client-side filter for games on a specific field here, if you want.  The Assignr
                    // service does not support querying on a specific venue, so this is the best we can do
                    //if (game._embedded.venue.name != "Hillcrest South") {
                    //    continue;
                    //}

                    if (ASSIGNR_TO_PUBLIC_NAMEMAP.ContainsKey(division)) {
                        if (ASSIGNR_TO_PUBLIC_NAMEMAP[division].ContainsKey(homeTeam)) {
                            homeTeam = ASSIGNR_TO_PUBLIC_NAMEMAP[division][homeTeam];
                        }

                        if (ASSIGNR_TO_PUBLIC_NAMEMAP[division].ContainsKey(awayTeam)) {
                            awayTeam = ASSIGNR_TO_PUBLIC_NAMEMAP[division][awayTeam];
                        }
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
                    mLogger.WriteLine($"A game on the public schedule is not in Assignr: {game.startTime.ToLocalTime().ToString("g")} at {game.location}: {game.division} {game.visitingTeam} @ {game.homeTeam}.");
                }
            }

            foreach (var game in mGames) {
                if (IGNORED_GAMES.Find(e => e.location == game.location && e.startTime == game.startTime) == null) {
                    ++inconsistentGames;
                    mLogger.WriteLine($"An Assignr game is not on the public game schedule: {game.startTime.ToLocalTime().ToString("g")} at {game.location}: {game.division} {game.visitingTeam} @ {game.homeTeam}.");
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
            { "Chaldecott North", "Chaldecott Park N (VCB)" },
            { "Chaldecott South", "Chaldecott Park S (VCB)" },
            { "Hillcrest North", "Hillcrest Park NE (VCB)" },
            { "Hillcrest South", "Hillcrest Park SW (VCB)" },
            { "Nanaimo North", "Nanaimo Park N (VCB)" },
            { "Nanaimo South East", "Nanaimo Park SE (VCB)" },
            { "Trafalgar", "Trafalgar Park (VCB)" },
            { "Challenger", "Variety Challenger Field" },
            { "Columbia Park", "Columbia Park" },
            { "Hillcrest Park", "Hillcrest Main Diamond" },
            { "Oak Park", "Oak Park North Diamond" },
        };

        Dictionary<string, Dictionary<string, string>> ASSIGNR_TO_PUBLIC_NAMEMAP = new Dictionary<string, Dictionary<string, string>> {
            { "Majors", new Dictionary<string, string> {
                {"Majors Royals", "MAJ1 MAIN ST PHYSIO ROYALS" },
                {"Majors Twins", "MAJ7 UNCLE BILLS PLUMBING TWINS" },
                {"Majors Mariners", "MAJ3 HOLBORN MARINERS" },
                {"Majors Legion", "MAJ4 LEGION ATHLETICS" },
                {"Majors Angels", "MAJ5 BEASTVAN ANGELS" },
                {"Majors Dodgers", "MAJ6 DODGERS" },
                {"Majors Marlins", "MAJ2 DAKOTA HOMES MARLINS" },
            }},
            { "Minors A", new Dictionary<string, string> {
                { "Minor A Orioles", "MINA9 ORIOLES" },
                { "Minor A Padres", "MINA2 PADRES" },
                { "Minor A Giants", "MINA8 INSPIRE DENTAL GIANTS" },
                { "Minor A Pirates", "MINA6 WHITE SPOT PIRATES" },
                { "Minor A Diamondbacks", "MINA1 TEAM KERR DIAMONDBACKS" },
                { "Minor A Brewers", "MINA3 BELL ALLIANCE BREWERS" },
                { "Minor A Rockies", "MINA5 WHITE SPOT ROCKIES" },
                { "Minor A Cardinals", "MINA4 WHITE SPOT CARDINALS" },
                { "Minor A Rangers", "MINA7 WHITE SPOT RANGERS" }
            }},
            { "Minors B", new Dictionary<string, string> {
                { "Minor B Ironpigs", "MINB7 IRON PIGS" },
                { "Minor B Chihuahuas", "MINB3 DULUX PAINTS CHIHUAHUAS" },
                { "Minor B Isotopes", "MINB10 ISOTOPES" },
                { "Minor B Knights", "MINB6 PRISTINE LABOUR KNIGHTS" },
                { "Minor B Jumbo Shrimp", "MINB4 JUMBO SHRIMP" },
                { "Minor B WooSox", "MINB1 CPA DEVELOPMENT WOOSOX" },
                { "Minor B Bulls", "MINB9 BULLS" },
                { "Minor B Bees", "MINB2 MAHNGER HOMES BEES" },
                { "Minor B Bison", "MINB8 SHOPPERS BISONS" },
                { "Minor B Canadians", "MINB12 CANADIANS" },
                { "Minor B River Cats", "MINB11 RIVER CATS" },
                { "Minor B Stripers", "MINB5 AJ TIGERS STRIPERS" },
            }},
            { "12 Selects", new Dictionary<string, string> {
            }},
            { "13U A", new Dictionary<string, string> {
            }},
            { "13U AA", new Dictionary<string, string> {
                { "Vancouver Mounties", "VCB 13U AA" },
            }},
            { "13U AAA", new Dictionary<string, string> {
                { "Vancouver Mounties", "VCB 13U AAA" },
            }},
            { "15U A", new Dictionary<string, string> {
                { "A's", "Athletics" }
            }},
            { "15U AA", new Dictionary<string, string>{
                { "Vancouver Mounties Blue", "VCB 15U AA Expos Blue" },
                { "Vancouver Mounties Red", "VCB 15U AA Red" }
            }},
            { "15U AAA", new Dictionary<string, string> {
                { "Vancouver Mounties", "VCB 15U AAA"},
                { "Cloverdale", "Cloverdale Rangers 15U AAA" },
                { "Township", "Township (Langley)" },
                { "Cowichan", "Cowichan 15U AAA"},
                { "Abbotsford", "Abby AAA"},
                { "Victoria", "Victoria Seawolves 15U AAA" },
                { "Ridge Meadows", "Ridge Meadows 15U AAA" },
                { "SOMBA", "Somba 15U AAA" },
                { "North Shore", "NSBA 15U AAA" },
                { "Kamloops", "Kamloops Riverdogs 15U AAA" },
                { "COMBA", "Comba 15U AAA" },
                { "Tri-City", "Tri City" }
            }},
            { "18U AA", new Dictionary<string, string> {
                { "A's", "Athletics" },
                { "Yankees", "Yankees 18U 2026" }
            }},
            { "18U AAA", new Dictionary<string, string> {
                { "Vancouver Mounties Blue", "VCB 18U AAA Blue Expos" },
                { "Vancouver Mounties White", "VCB 18U AAA White Mounties" },
                { "UBC", "UBC Jr Thunder" },
                { "Cowichan", "Cowichan Valley" },
                { "COMBA", "Comba" }
            }},
            { "26U", new Dictionary<string, string> {
                { "Vancouver Mounties", "26U Mounties" },
                { "Vancouver Expos", "26U Expos" },
                { "Vancouver Vipers", "26U Vipers" }
            }},
        };
    }
}
