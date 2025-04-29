
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

            TeamSnapEvents teamSnap = new(logger);
            teamSnap.FetchEvents();
            teamSnap.addPlayoffPlaceHolderGames();

            // Find conflicts in the game/practice schedule
            errors += teamSnap.FindConflicts();

            string ASSIGNR_ID_LMB = "627";
            string ASSINGR_ID_VCB = "12381";

            //string ASSIGNR_ID_KLL = "6561";
            //string ASSIGNR_ID_RICHMOND = "19639";

            AssignrEvents assignr = new(logger);
            assignr.Authenticate();
            assignr.FetchEventsFromService(ASSINGR_ID_VCB);

            errors += assignr.Reconcile(teamSnap.getGames());

            // Update the Google field calendars with the TeamSnap calendars.
            GoogleEvents googleEvents = new(teamSnap.getGames(), teamSnap.getPractices(), logger);
            googleEvents.Reconcile();

            SportsEngine sportsEngine = new SportsEngine();
            sportsEngine.authenticate();
            sportsEngine.fetchEvents();

            assignr.clearEvents();
            assignr.FetchEventsFromService(ASSIGNR_ID_LMB);
            errors += assignr.Reconcile(sportsEngine.getGames());

            logger.Close();

            // If there are problems, the exception handler takes care of them.  Conflicts are reported on the console window
            return errors;
        }
    }
}
