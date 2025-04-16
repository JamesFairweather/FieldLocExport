
namespace VcbFieldExport
{
    internal partial class Program
    {
        static int Main(string[] args)
        {
            DateTime now = DateTime.Now;

            string logFileName = $"fieldSync.{now.Year}-{now.Month}-{now.Day}.log";
            StreamWriter logger = new StreamWriter(logFileName, false);

            //SportsEngine sportsEngine = new SportsEngine();
            //sportsEngine.authenticate();
            //sportsEngine.fetchEvents();

            TeamSnapEvents teamSnap = new(logger);
            teamSnap.FetchEvents();

            // Find conflicts in the game/practice schedule
            int errors = teamSnap.FindConflicts();

            string ASSIGNR_ID_KLL = "6561";
            string ASSIGNR_ID_LMB = "627";
            string ASSIGNR_ID_RICHMOND = "19639";
            string ASSINGR_ID_VCB = "12381";

            AssignrEvents assignr = new(logger);
            assignr.Authenticate();
            assignr.FetchEventsFromService(ASSINGR_ID_VCB);

            // Cross-check the game list in TeamSnap with Assignr.  Any inconsistencies are output to the console window
            errors += assignr.Reconcile(teamSnap.getGames());

            // Reconcile the Google field calendars with the TeamSnap field calendars
            GoogleEvents googleEvents = new(teamSnap.getGames(), teamSnap.getPractices(), logger);
            googleEvents.Reconcile();

            logger.Close();

            // If there are problems, the exception handler takes care of them.  Conflicts are reported on the console window
            return errors;
        }
    }
}
