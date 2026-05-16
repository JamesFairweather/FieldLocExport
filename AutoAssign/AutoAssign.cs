
using Newtonsoft.Json;
using System.CommandLine;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using static System.Net.WebRequestMethods;

namespace AutoAssign
{
    // TODOs
    // * Add the ability to enter the assignments from a .CSV file.
    // This is the web call used to assign someone:
    // PUT https://littlemountainbaseball.assignr.com/assign/games/{gameId}
    // Requires two parameters
    //   * User ID (mine is 340011)
    //   * The assignment Id (e.g. 77267314)
    // It's not clear to me how these parameters are being passed back to the service from the
    // stream I can see in Chrome though (are they using URL parameters or are they passed in
    // message body)?

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

            assignedPlateUmpire = new();
            assignedBaseUmpire = new();
            plateRequests = new ();
            baseRequests = new();
        }

        public string localized_date { get; set; }
        public string localized_time { get; set; }

        public string age_group { get; set; }
        public bool cancelled { get; set; }
        public bool published { get; set; }

        [JsonProperty("_embedded")]
        public GameInfo details { get; set; }

        public Official assignedPlateUmpire;
        public Official assignedBaseUmpire;

        public List<Official> plateRequests;
        public List<Official> baseRequests;
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

    public class Official
    {
        public Official()
        {
            id = 0;
            firstName = string.Empty;
            lastName = string.Empty;
            plateAssignments = 0;
            baseAssignments = 0;
            plateRequestCount = 0;
            baseRequestCount = 0;
        }

        public static Official FromCsv(string csvLine)
        {
            string[] values = csvLine.Split(',');
            Official official = new Official();
            official.id = Convert.ToInt32(values[0]);
            official.lastName = values[1];
            official.firstName = values[2];
            return official;
        }

        // this is the public Id, and the one used when entering assignments
        public int id { get; set; }

        // There's also an account_id property, which is unique to each user and different from
        // Id, but I don't think it's relevant for this tool
        // public string account_id { get; set; }

        [JsonProperty("first_name")]
        public string firstName { get; set; }

        [JsonProperty("last_name")]
        public string lastName { get; set; }

        public string fullName()
        {
            return $"{lastName}, {firstName}";
        }

        // completed or accepted assignments
        public int plateAssignments { get; set; }
        public int baseAssignments { get; set; }

        // Requests for the current block of assignments
        public int plateRequestCount { get; set; }
        public int baseRequestCount { get; set; }
    }

    internal class AssignmentDetails
    {
        public AssignmentDetails()
        {
            official = new();
        }
        public Official official { get; set; }
    }

    internal class Assignment
    {
        public Assignment()
        {
            id = 0;
            assigned = false;

            position = string.Empty;

            details = new();
        }

        public int id { get; set; }
        public bool assigned { get; set; }
        public string position { get; set; }

        [JsonProperty("_embedded")]
        public AssignmentDetails details { get; set; }
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

    internal class PendingAssignment
    {
        public PendingAssignment()
        {
            id = 0;
            name = string.Empty;
            request = false;
        }

        public int id { get; set; }
        public string name { get; set; }
        public bool request { get; set; }
    }

    internal class AssignmentsResponse
    {
        public AssignmentsResponse()
        {
            pending = new();
        }

        [JsonProperty("users")]
        public List<PendingAssignment> pending { get; set; }
    }

    public class Assignr
    {
        public Assignr()
        {
            mHttpClient = new();
            mGameList = new();
            mUmpires = new();
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
            if (useSessionToken)
            {
                // Using a non-public API
                msg.Headers.Add("Cookie", $"_assignr_session={mSessionToken}");
            }
            else
            {
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


        public void FetchUnassignedGames(string siteId)
        {
            int totalPages = int.MaxValue;
            int currentPage = 1;

            while (currentPage <= totalPages)
            {
                // Fetch all the games from the start of the season to the current date plus 14 days
                string gamesUri = $"https://api.assignr.com/api/v2/sites/{siteId}/games?page={currentPage}&search[start_date]=2026-01-01&search[end_date]={DateTime.Today.AddDays(14).ToString("yyyy-MM-dd")}";

                string jsonResponse = HttpMessage(HttpMethod.Get, gamesUri);

                //JsonSerializerSettings settings = new();
                //settings.DateParseHandling = DateParseHandling.None;
                GamesResponse jsonRoot = JsonConvert.DeserializeObject<GamesResponse>(jsonResponse) ?? new();

                if (jsonRoot == null)
                {
                    throw new Exception("Unexpected response from Assignr service");
                }

                totalPages = jsonRoot.page.pages;

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
                            // increment the number of assignments this umpire has had already this season
                            Official? umpire = mUmpires.Find(e => e.id == assignment.details.official.id);

                            if (umpire == null)
                            {
                                throw new Exception($"Assignr returned an unrecognized official Id {assignment.details.official.id}");
                            }

                            if (assignment.position == "Plate Umpire")
                            {
                                ++umpire.plateAssignments;
                                game.assignedPlateUmpire = umpire;
                            }
                            else if (assignment.position == "Base Umpire")
                            {
                                ++umpire.baseAssignments;
                                game.assignedBaseUmpire = umpire;
                            }
                        }
                        else
                        {
                            // Assignr's API doesn't allow us to retrieve game requests,
                            // but we can still get them using a web API, which requires
                            // a different authorization token (happily this token seems
                            // to not have an expiration time).
                            string assignmentsUri = $"https://littlemountainbaseball.assignr.com/assign/assignments/{assignment.id}.json";

                            string assignmentsResponse = HttpMessage(HttpMethod.Get, assignmentsUri, true);

                            AssignmentsResponse assignments = JsonConvert.DeserializeObject<AssignmentsResponse>(assignmentsResponse) ?? new();

                            foreach (PendingAssignment user in assignments.pending)
                            {
                                if (user.request)
                                {
                                    Official? umpire = mUmpires.Find(e => e.id == user.id);

                                    if (umpire == null)
                                    {
                                        throw new Exception($"Assignr returned an unrecognized official Id for this game request");
                                    }

                                    if (assignment.position == "Plate Umpire")
                                    {
                                        ++umpire.plateRequestCount;
                                        game.plateRequests.Add(umpire);
                                    }
                                    else if (assignment.position == "Base Umpire")
                                    {
                                        ++umpire.baseRequestCount;
                                        game.baseRequests.Add(umpire);
                                    }
                                }
                            }
                        }
                    }

                    mGameList.Add(game);
                }

                ++currentPage;
            }
        }

        // For each game with at least one unassigned position, dump it to a CSV file along with any
        // pending requests for each position.
        public void WriteRequestsToFile()
        {
            StreamWriter sw = new StreamWriter("gamesToAssign.csv", false);

            sw.WriteLine("Age Group,Date,Time,Venue,Position,Assigned Umpire,Req1,Req2,Req3,Req4,Req5,Req6,Req7,Req8,Req9,Req10,Req11,Req12,Req13,Req14,Req15,Req16,Req17,Req18,Req19,Req20");

            foreach(Game game in mGameList)
            {
                if (game.assignedPlateUmpire.id == 0) {
                    sw.Write($"{game.age_group},{game.localized_date},{game.localized_time},{game.details.venue.name},Plate Umpire,,");

                    foreach (Official umpire in game.plateRequests)
                    {
                        sw.Write($"\"{umpire.fullName()}\",");
                    }

                    // trailing commas
                    for (int i = game.plateRequests.Count; i < 20; ++i)
                    {
                        sw.Write(",");
                    }
                    sw.WriteLine();
                }

                if (game.assignedBaseUmpire.id == 0)
                {
                    sw.Write($"{game.age_group},{game.localized_date},{game.localized_time},{game.details.venue.name},Base Umpire,,");

                    foreach (Official umpire in game.baseRequests)
                    {
                        sw.Write($"\"{umpire.fullName()}\",");
                    }

                    for (int i = game.baseRequests.Count; i < 20; ++i)
                    {
                        sw.Write(",");
                    }
                    sw.WriteLine();
                }
            }

            sw.Close();
        }

        // Dump the number of requested games & assignments for this official for this year
        // When there is more than one official who could be assigned to a game, I'll give
        // it to whoever has worked the fewest number.
        public void WriteUmpireRequestsToFile()
        {
            StreamWriter sw = new StreamWriter("gameRequestsByUmpire.csv", false);

            sw.WriteLine("Name,Total requests for this block,Previous plate assignments,Previous base assignments");

            foreach (Official umpire in mUmpires)
            {
                int totalRequests = umpire.plateRequestCount + umpire.baseRequestCount;
                if (totalRequests != 0)
                {
                    sw.WriteLine($"\"{umpire.lastName}, {umpire.firstName}\",{totalRequests},{umpire.plateAssignments},{umpire.baseAssignments}");
                }
            }

            sw.Close();
        }

        public void Assign()
        {
            // Take the assignments from a spreadsheet or .CSV file and push them to the
            // online database.  For each game, we need the two assignment Ids, as well as the 
            // Ids of the users to be assigned to each assignment.

            // This is the form data passed in the http request body
            /*
            ------WebKitFormBoundaryTIuxvJDmv6R4mkp4
            Content-Disposition: form-data; name="_method"

            patch
            ------WebKitFormBoundaryTIuxvJDmv6R4mkp4
            Content-Disposition: form-data; name="game[assignments_attributes][0][user_id]"

            1899180 <- Leo McTaggart
            ------WebKitFormBoundaryTIuxvJDmv6R4mkp4
            Content-Disposition: form-data; name="game[assignments_attributes][0][lock_version]"

            2 <- IDK what this means.  Normally it's 0.  Probably related to how many different people have been assigned?
              Would it matter if I didn't pass it?
            ------WebKitFormBoundaryTIuxvJDmv6R4mkp4
            Content-Disposition: form-data; name="game[assignments_attributes][0][id]"

            77190176 <- Game ID 27362731 Plate Assignment
            ------WebKitFormBoundaryTIuxvJDmv6R4mkp4
            Content-Disposition: form-data; name="game[assignments_attributes][1][user_id]"

            1499805 <- Hector Lopez
            ------WebKitFormBoundaryTIuxvJDmv6R4mkp4
            Content-Disposition: form-data; name="game[assignments_attributes][1][lock_version]"

            4 <- IDK what this means
            ------WebKitFormBoundaryTIuxvJDmv6R4mkp4
            Content-Disposition: form-data; name="game[assignments_attributes][1][id]"

            77190177 <- Game ID 27362731 Base Assignment
            ------WebKitFormBoundaryTIuxvJDmv6R4mkp4--
            */
        }


        public List<Official> Officials()
        {
            return mUmpires;
        }

        public void LoadUmpireDatabase(string fileName)
        {
            mUmpires = System.IO.File.ReadAllLines(fileName)
                .Skip(1)
                .Select(v => Official.FromCsv(v))
                .ToList();
        }

        HttpClient mHttpClient;
        string? mBearerToken;
        string? mSessionToken;
        List<Official> mUmpires;    // should probably be a dictionary
        List<Game> mGameList;
    }

    // Thsi program should have two commands:
    // FetchRequests - pulls the requests from Assignr and creates two or maybe three files
    //  - the list of requests
    //  - stats
    //  - proposed assignments for each game - this is the file I would update & modify if necessary, and the one that would be
    //    read back
    // UploadAssignments
    //  - read the assignments file and push them to Assignr.

    internal partial class AutoAssign
    {
        static async Task<int> Main(string[] args)
        {
            using (StreamReader reader = new StreamReader("../Shared/credentials.json"))
            {
                mCredentials = JsonConvert.DeserializeObject<Credentials>(reader.ReadToEnd());
            }

            if (mCredentials == null)
            {
                throw new Exception("Failed to read the service credentials");
            }

            Command fetchCommand = new("fetch", "Fetch all pending game requests from Assignr");
            fetchCommand.SetAction(parseResult => FetchRequests());
            Command assignCommand = new("assign", "Push all assignments to Assignr");

            RootCommand rootCommand = new("Fetch requests or update Assignr");

            rootCommand.Subcommands.Add(fetchCommand);
            // rootCommand.Subcommands.Add(assignCommand);

            ParseResult parseResult = rootCommand.Parse(args);
            return await parseResult.InvokeAsync();
        }

        static async Task<int> FetchRequests() {
            Assignr assignr = new();
            assignr.Authenticate(mCredentials.Assignr, mCredentials.AssignrSessionToken);

            string ASSIGNR_ID_LMB = "627";

            // Load the umpire database
            assignr.LoadUmpireDatabase("2026 Umpires.csv");

            // Get all the games that are published and have been assigned or need to be assigned
            assignr.FetchUnassignedGames(ASSIGNR_ID_LMB);
            assignr.WriteRequestsToFile();
            assignr.WriteUmpireRequestsToFile();

            /*
            var games = new List<RGame>
        {
            new RGame
            {
                GameId = "G1",
                Requests = new Dictionary<string, List<string>>
                {
                    ["A"] = new List<string> { "R1", "R2" },
                    ["B"] = new List<string> { "R1", "R3" }
                }
            },
            new RGame
            {
                GameId = "G2",
                Requests = new Dictionary<string, List<string>>
                {
                    ["A"] = new List<string> { "R2", "R3" },
                    ["B"] = new List<string> { "R1", "R3" }
                }
            }
        };

            var pastAssignments = new Dictionary<string, int>
            {
                ["R1"] = 10,
                ["R2"] = 4,
                ["R3"] = 2,
                ["R4"] = 0
            };

            var assignments = RefereeAssigner.AssignReferees(games, pastAssignments);

            foreach (var kvp in assignments.OrderBy(k => k.Key.GameId).ThenBy(k => k.Key.Position))
            {
                Console.WriteLine($"{kvp.Key.GameId} {kvp.Key.Position} -> {kvp.Value ?? "unfilled"}");
            }
            */

            return 0;
        }

        static Credentials? mCredentials;
    }
}
