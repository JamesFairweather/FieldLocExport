
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

    internal partial class Program
    {
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
            teamSnap.FetchEvents(credentials.Teamsnap ?? string.Empty);

            // Find conflicts in the game/practice schedule
            errors += teamSnap.FindConflicts();

            string ASSINGR_ID_VCB = "12381";

            //string ASSIGNR_ID_LMB = "627";
            //string ASSIGNR_ID_KLL = "6561";
            //string ASSIGNR_ID_RICHMOND = "19639";

            AssignrEvents assignr = new(logger);
            assignr.Authenticate(credentials.Assignr);
            assignr.FetchEventsFromService(ASSINGR_ID_VCB, true);
            teamSnap.addPlayoffPlaceHolderGames(assignr.getGames().FindAll(x => {
                // Playoff games will only be in TeamSnap if both teams are known, so add
                // placeholder for these games from Assignr if the game isn't in TeamSnap yet.
                return x.eventType == VcbFieldEvent.Type.PlayoffGame
                    && (x.division == "15U A" || x.division == "18U AA")
                    && teamSnap.getGames().Find(t => {
                        return x.location == t.location && x.startTime == t.startTime;
                        }) == null;
            }));

            errors += assignr.Reconcile(teamSnap.getGames());

            logger.WriteLine();

            // Update the Google field calendars with the TeamSnap calendars.
            GoogleEvents googleEvents = new(teamSnap.getGames(), teamSnap.getPractices(), logger);
            googleEvents.Reconcile(credentials.Google);

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
