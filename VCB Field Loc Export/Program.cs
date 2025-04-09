
namespace VcbFieldExport
{
    internal partial class Program
    {
        static int Main(string[] args)
        {
            DateTime now = DateTime.Now;

            string logFileName = $"fieldSync.{now.Year}-{now.Month}-{now.Day}.log";
            StreamWriter logger = new StreamWriter(logFileName, false);

            TeamSnapEvents teamSnap = new(logger);
            teamSnap.FetchEvents();

            // Find conflicts in the game/practice schedule
            int errors = teamSnap.FindConflicts();

            AssignrEvents assignr = new(logger);
            assignr.Authenticate();
            assignr.FetchEventsFromService();

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
