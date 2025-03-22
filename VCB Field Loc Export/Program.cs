
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
            assignr.Reconcile(teamSnap.getEventList());

            GoogleEvents googleEvents = new(teamSnap.getEventList());
            googleEvents.Reconcile();
            googleEvents.SaveEvents();

            // If there are problems, the exception handler takes care of them
            return 0;
        }
    }
}
