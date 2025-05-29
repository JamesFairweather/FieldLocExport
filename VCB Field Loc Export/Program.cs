
namespace VcbFieldExport
{
    internal partial class Program
    {
        static int Main(string[] args)
        {
            DateTime now = DateTime.Now;
            int errors = 0;

            string logFileName = $"fieldSync.{now.Year}-{now.Month}-{now.Day}.log";
            StreamWriter logger = new StreamWriter(logFileName, false);

            logger.WriteLine("Checking Vancouver Community Baseball's Assignr schedule for consistency");
            TeamSnapEvents teamSnap = new(logger);
            teamSnap.FetchEvents();

            // Find conflicts in the game/practice schedule
            errors += teamSnap.FindConflicts();

            string ASSIGNR_ID_LMB = "627";
            string ASSINGR_ID_VCB = "12381";

            //string ASSIGNR_ID_KLL = "6561";
            //string ASSIGNR_ID_RICHMOND = "19639";

            AssignrEvents assignr = new(logger);
            assignr.Authenticate();
            assignr.FetchEventsFromService(ASSINGR_ID_VCB, true);
            teamSnap.addPlayoffPlaceHolderGames(assignr.getGames().FindAll(x => {
                // add 15U and 18U AA playoff games from Assignr.  As the teams are set
                // and the games added to TeamSnap, I'll need to remove those games using
                // this filter.
                DateTime RoundRobinEndDate_13UA = new DateTime(2025, 6, 10);
                return x.eventType == VcbFieldEvent.Type.PlayoffGame && (x.division != "13U A" || x.startTime > RoundRobinEndDate_13UA);
            }));

            errors += assignr.Reconcile(teamSnap.getGames());

            logger.WriteLine();

            // Update the Google field calendars with the TeamSnap calendars.
            GoogleEvents googleEvents = new(teamSnap.getGames(), teamSnap.getPractices(), logger);
            googleEvents.Reconcile();

            logger.WriteLine();
            logger.WriteLine("Checking Little Mountain Baseball's Assignr schedule for consistency");

            SportsEngine sportsEngine = new SportsEngine();
            sportsEngine.authenticate();
            sportsEngine.fetchEvents();

            assignr.clearEvents();
            assignr.FetchEventsFromService(ASSIGNR_ID_LMB, false);
            errors += assignr.Reconcile(sportsEngine.getGames());

            logger.Close();

            // If there are problems, the exception handler takes care of them.  Conflicts are reported on the console window
            return errors;
        }
    }
}
