
namespace VcbFieldExport
{
    internal partial class Program
    {
        static int Main(string[] args)
        {
            Console.WriteLine("Fetching events from TeamSnap...");
            TeamSnapEvents teamSnap = new();
            teamSnap.FetchEvents();

            Console.WriteLine("Fetching games from Assignr...");
            AssignrEvents assignr = new();
            assignr.Authenticate();
            assignr.FetchEventsFromService();

            // Now, cross-check the game list in TeamSnap with Assignr.  Inconsistencies are output to the console window
            assignr.Reconcile(teamSnap.getEventList());

            // Add or removed events to the Google field calendars.
            GoogleEvents googleEvents = new(teamSnap.getEventList());
            googleEvents.Reconcile();

            // save out the updated events
            googleEvents.SaveEvents();

            // If there are problems, the exception handler takes care of them
            return 0;
        }
    }
}
