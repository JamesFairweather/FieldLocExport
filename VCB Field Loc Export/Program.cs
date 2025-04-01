
namespace VcbFieldExport
{
    internal partial class Program
    {
        static int Main(string[] args)
        {
            Console.WriteLine("Fetching events from TeamSnap...");
            TeamSnapEvents teamSnap = new();
            teamSnap.FetchEvents();

            // Find conflicts in the game/practice schedule
            int errors = teamSnap.FindConflicts();

            Console.WriteLine("Fetching games from Assignr...");
            AssignrEvents assignr = new();
            assignr.Authenticate();
            assignr.FetchEventsFromService();

            // Cross-check the game list in TeamSnap with Assignr.  Any inconsistencies are output to the console window
            errors += assignr.Reconcile(teamSnap.getGames());

            // Reconcile the Google field calendars with the TeamSnap field calendars
            GoogleEvents googleEvents = new(teamSnap.getGames(), teamSnap.getPractices());
            googleEvents.Reconcile();

            // If there are problems, the exception handler takes care of them.  Conflicts are reported on the console window
            return errors;
        }
    }
}
