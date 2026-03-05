
using Newtonsoft.Json;

namespace VcbFieldExport
{
    public class Credentials
    {
        [JsonProperty("teamsnap_bearer_token")]
        // TODO: Figure out how to get a bearer token from TeamSnap's service using the client ID & secret.
        // Fortunately, the bearer token seems to be perpertual so we can just put that into our credential file
        // until the above TODO is done.
        public string? Teamsnap { get; set; }
        public Google.Apis.Auth.OAuth2.ClientSecrets? Sportsengine { get; set; }
        public Google.Apis.Auth.OAuth2.ClientSecrets? Assignr { get; set; }
        public Google.Apis.Auth.OAuth2.ClientSecrets? Google { get; set; }
    }

    public class Field
    {
        public Field(DateTime start, DateTime end, string teamSnapId, string googleID)
        {
            this.startDate = start;
            this.endDate = end;
            this.teamSnapId = teamSnapId;
            this.googleId = googleID;
        }

        public DateTime startDate { get; }     // first day of the season we start tracking
        public DateTime endDate { get; }       // last day of the season to track

        public string teamSnapId { get; }

        public string googleId { get; }

        public bool Valid()
        {
            return DateTime.Today > startDate && DateTime .Today < endDate;
        }
    };

    internal partial class Program
    {
        // TODO for 2026: use date ranges for spring regular-season, spring playoffs, and summer seasons
        // so I don't need to keep updating the code to handle different cases as we move through the year
        // These dates should be in a configuration file
        // DateTime SpringSeason_13UAPlayoffs_Start = new(2025, 06, 08);   // 13U A games on or after this day are playoff games
        // DateTime SpringSeason_15UAPlayoffs_Start = new(2025, 06, 14);   // 15U A games on or after this day are playoff games
        // DateTime SpringSeason_18UAPlayoffs_Start = new(2025, 06, 16);   // 18U AA games on or after this day are playoff games
        // DateTime SummerSeason_Start = new(2025, 06, 30);                // start of the summer season for 13U A, 15U A, 18U AA.  These are scheduled as regular-season games

        // VCB Organization locations
        // https://go.teamsnap.com/1054301/league_location/list

        static List<Field> fieldInfo = new List<Field> {
            new Field(new DateTime(2026, 03, 01), new DateTime(2026, 9, 30),    "77851189", "Chaldecott Park N (VCB)"),
            new Field(new DateTime(2026, 03, 01), new DateTime(2026, 9, 30),    "77851190", "Chaldecott Park S (VCB)"),
            new Field(new DateTime(2026, 03, 01), new DateTime(2026, 10, 31),     "77851188", "Hillcrest Park NE (VCB)"),
            new Field(new DateTime(2026, 03, 01), new DateTime(2026, 10, 31),     "77851187", "Hillcrest Park SW (VCB)"),
            new Field(new DateTime(2026, 03, 01), new DateTime(2026, 10, 31),   "77851191", "Nanaimo Park N (VCB)"),
            new Field(new DateTime(2026, 03, 01), new DateTime(2026, 10, 31),   "77851192", "Nanaimo Park SE (VCB)"),
            // new Field(new DateTime(2025, 04, 01), new DateTime(2025, 10, 31),   "74242257", "Nanaimo Park batting cage"), disabled for 2026.  Need a new Org-wide location created for this resource
            new Field(new DateTime(2026, 03, 01), new DateTime(2026, 9, 30),    "77851193", "Trafalgar Park (VCB)"),
        };

        static int Main(string[] args)
        {
            DateTime now = DateTime.Now;
            int errors = 0;

            string logFileName = $"fieldSync.{now.Year}-{now.Month}-{now.Day}.log";
            StreamWriter logger = new StreamWriter(logFileName, false);

            Credentials? credentials;
            using (StreamReader reader = new StreamReader("credentials.json")) {
                credentials = JsonConvert.DeserializeObject<Credentials>(reader.ReadToEnd());
            }

            if (credentials == null) {
                throw new Exception("Failed to read the service credentials");
            }

            logger.WriteLine("Checking Vancouver Community Baseball's Assignr schedule for consistency");
            TeamSnapEvents teamSnap = new(logger);
            teamSnap.FetchEvents(credentials.Teamsnap ?? string.Empty, fieldInfo);

            // Find conflicts in the game/practice schedule
            errors += teamSnap.FindConflicts();

            string ASSINGR_ID_VCB = "12381";

            //string ASSIGNR_ID_LMB = "627";
            //string ASSIGNR_ID_KLL = "6561";
            //string ASSIGNR_ID_RICHMOND = "19639";

            AssignrEvents assignr = new(logger);
            assignr.Authenticate(credentials.Assignr);
            assignr.FetchEventsFromService(ASSINGR_ID_VCB, false);
            // TODO for 2026: implement a better way of managing playoff games
            //teamSnap.addPlayoffPlaceHolderGames(assignr.getGames().FindAll(x => {
            //    // Playoff games will only be in TeamSnap if both teams are known, so add
            //    // placeholder for these games from Assignr if the game isn't in TeamSnap yet.
            //    return x.eventType == VcbFieldEvent.Type.PlayoffGame
            //        && (x.division == "15U A" || x.division == "18U AA")
            //        && teamSnap.getGames().Find(t => {
            //            return x.location == t.location && x.startTime == t.startTime;
            //            }) == null;
            //}));

            errors += assignr.Reconcile(teamSnap.getGames());

            logger.WriteLine();

            // Update the Google field calendars with the TeamSnap calendars.
            GoogleEvents googleEvents = new(teamSnap.getGames(), teamSnap.getPractices(), logger);
            googleEvents.Reconcile(credentials.Google, fieldInfo);

            //logger.WriteLine();
            //logger.WriteLine("Checking Little Mountain Baseball's Assignr schedule for consistency");

            //SportsEngine sportsEngine = new SportsEngine();
            //sportsEngine.authenticate(credentials.Sportsengine);
            //sportsEngine.fetchEvents();

            //assignr.clearEvents();
            //assignr.FetchEventsFromService(ASSIGNR_ID_LMB, false);
            //errors += assignr.Reconcile(sportsEngine.getGames());

            logger.Close();

            // If there are problems, the exception handler takes care of them.  Conflicts are reported on the console window
            return errors;
        }
    }
}
