using CsvHelper;
using CsvHelper.Configuration;
using CsvHelper.TypeConversion;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using System;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using static System.Formats.Asn1.AsnWriter;
using static System.Net.Mime.MediaTypeNames;

namespace VcbFieldExport
{
    public enum EventType
    {
        Practice,
        Game,
    };

    public class VcbFieldEvent : IEquatable<VcbFieldEvent>
    {
        public VcbFieldEvent()
        {
            eventType = EventType.Practice;
            homeTeam = string.Empty;
            visitingTeamOrDescription = string.Empty;
            startTime = DateTime.MinValue;
            endTime = DateTime.MinValue;
            googleEventId = string.Empty;
        }

        public VcbFieldEvent(EventType _eventType, string _homeTeam, string _visitingTeam, DateTime start, DateTime end, string _googleEventId)
        {
            eventType = _eventType;
            homeTeam = _homeTeam;
            visitingTeamOrDescription = _visitingTeam;
            startTime = start;
            endTime = end;
            googleEventId = _googleEventId;
        }

        public EventType eventType { get; set; }
        public string homeTeam { get; set; }
        public string visitingTeamOrDescription { get; set; }
        public DateTime startTime { get; set; }
        public DateTime endTime { get; set; }
        public string googleEventId { get; set; }

        public override bool Equals(object? obj)
        {
            return this.Equals(obj as VcbFieldEvent);
        }

        public bool Equals(VcbFieldEvent? e)
        {
            if (e is null)
            {
                return false;
            }

            if (Object.ReferenceEquals(this, e))
            {
                return true;
            }

            return eventType == e.eventType &&
                homeTeam == e.homeTeam &&
                visitingTeamOrDescription == e.visitingTeamOrDescription &&
                startTime == e.startTime &&
                endTime == e.endTime;
        }

        public override int GetHashCode() => base.GetHashCode();
    };

    public class FieldEventMap : ClassMap<VcbFieldEvent>
    {
        public FieldEventMap()
        {
            Map(m => m.eventType).Index(0).Name("eventType");
            Map(m => m.homeTeam).Index(1).Name("homeTeam");
            Map(m => m.visitingTeamOrDescription).Index(2).Name("visitingTeamOrDescription");
            Map(m => m.startTime).Index(3).Name("startTime");
            Map(m => m.endTime).Index(4).Name("endTime");
            Map(m => m.googleEventId).Index(5).Name("googleEventId");
        }
    }

    class IdEqualityComparer : IEqualityComparer<VcbFieldEvent>
    {
        public bool Equals(VcbFieldEvent? a, VcbFieldEvent? b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a is null || b is null) return false;
            return a.eventType == b.eventType &&
                a.homeTeam == b.homeTeam &&
                a.visitingTeamOrDescription == b.visitingTeamOrDescription &&
                a.startTime == b.startTime && 
                a.endTime == b.endTime;
        }

        public int GetHashCode(VcbFieldEvent obj)
        {
            if (obj is null) return 0;
            return obj.GetHashCode();
        }
    }

    internal partial class Program
    {
        [GeneratedRegex(@"(\d+:\d+ (AM|PM))( - (\d+:\d+ (AM|PM)))?")]
        private static partial Regex TimeRegex();

        [GeneratedRegex(@"(?<TeamName>.+): (?<EventTitle>.+)", RegexOptions.ExplicitCapture)]
        private static partial Regex TeamEventRegex();

        [GeneratedRegex(@"(?<VisitorTeamName>.+) at (?<HomeTeamName>.+)", RegexOptions.ExplicitCapture)]
        private static partial Regex LeagueControlledGameRegex();

        [GeneratedRegex(@"(?<VcbTeamName>.+) vs\. (?<OpponentName>.+) \(Team Controlled\)?", RegexOptions.ExplicitCapture)]
        private static partial Regex TeamControlledGameRegex();

        [GeneratedRegex(@"<div class=""pagination .*<a.*\d+"">(\d+)</a>.*</a></div>")]
        private static partial Regex PaginationRegex();

        static readonly Dictionary<int, string> locationIds = new Dictionary<int, string> {
            { 69829171, "Chaldecott Park N diamond" },
            { 69829169, "Chaldecott Park S diamond" },
            { 69829163, "Hillcrest Park NE diamond" },
            { 69829157, "Hillcrest Park SW diamond" },
            { 69829182, "Killarney Park W diamond" },
            { 69829177, "Nanaimo Park N diamond" },
            { 69829180, "Nanaimo Park SE diamond" },
            { 69829186, "Trafalgar Park" },
        };

        static int Main(string[] args)
        {
            int returnValue = 0;

            string sessionId = args[0];

            if (string.IsNullOrEmpty(sessionId))
            {
                Console.WriteLine("Usage VcbFieldExport.exe <TeamSnap session Id>");
                return -1;
            }

            // Uncomment these lines to remove all the events for a specific calendar
            // string removeAllEventsFromFieldCalendar = "Chaldecott Park N diamond";
            // DeleteAllCalendarEvents(GetGoogleCalendarService(removeAllEventsFromFieldCalendar));
            // return 0;

            foreach (int locationId in locationIds.Keys)
            {
                Console.WriteLine($"Processing events for location {locationIds[locationId]} ...");

                List<VcbFieldEvent> currentEvents = FetchEvents(sessionId, locationId);

                if (currentEvents.Count == 0)
                {
                    Console.WriteLine("Found no events for this field in the returned event list.  Skipping.");
                    continue;
                }

                Console.WriteLine($"Found {currentEvents.Count} events, reconciling with last snapshot...");

                List<VcbFieldEvent> savedEvents = LoadEvents(locationIds[locationId]);

                var googleCalendarService = GetGoogleCalendarService(locationIds[locationId]);

                // find events to remove and delete them from the Google calendar
                List<VcbFieldEvent> eventsToRemove = new List<VcbFieldEvent>();
                savedEvents.ForEach(e =>
                {
                    if (!currentEvents.Contains(e))
                    {
                        if (e.googleEventId != string.Empty) {
                            eventsToRemove.Add(e);
                        }
                        else
                        {
                            Console.WriteLine($"Warning event deleted that doesn't have an event ID ({locationIds[locationId]}: {e.eventType} {e.homeTeam}, {e.startTime}).  This event should be manually removed from the Google calendar and the CSV file.");
                        }
                    }
                });

                if (eventsToRemove.Count > 0)
                {
                    Console.WriteLine($"Removing {eventsToRemove.Count} event(s) from the calendar...");
                    RemoveEventsFromGoogleCalendar(eventsToRemove, googleCalendarService);
                }
                else
                {
                    Console.WriteLine($"No events to remove found");
                }

                // find events to add and insert them into the Google calendar
                List<VcbFieldEvent> eventsToAdd = new List<VcbFieldEvent>();
                currentEvents.ForEach(e =>
                {
                    if (!savedEvents.Contains(e))
                    {
                        eventsToAdd.Add(e);
                    }
                });

                if (eventsToAdd.Count > 0) {
                    Console.WriteLine($"Adding {eventsToAdd.Count} event(s) to the calendar...");
                    PostNewEventsToGoogleCalendar(eventsToAdd, googleCalendarService);
                }
                else
                {
                    Console.WriteLine($"No events to add found");
                }

                SaveEvents(currentEvents, locationIds[locationId]);
            }

            return returnValue;
        }

        static Google.Apis.Calendar.v3.CalendarService GetGoogleCalendarService(string locationName)
        {
            UserCredential credential;

            // If the scopes are modified, you will need a new credentials.json file and re-generate the tokens.

            string[] Scopes = { CalendarService.Scope.Calendar };
            string ApplicationName = "Google Calendar API .NET Quickstart";

            using (var stream =
                new FileStream("credentials.json", FileMode.Open, FileAccess.Read))
            {
                // The file token.json stores the user's access and refresh tokens, and is created
                // automatically when the authorization flow completes for the first time.
                string credPath = $"{locationName}.json";
                credential = GoogleWebAuthorizationBroker.AuthorizeAsync(
                    GoogleClientSecrets.FromStream(stream).Secrets,
                    Scopes,
                    "user",
                    CancellationToken.None,
                    new FileDataStore(credPath, true)).Result;
            }

            // Create Google Calendar API service.
            var service = new CalendarService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = ApplicationName,
            });

            return service;
        }

        static void RemoveEventsFromGoogleCalendar(List<VcbFieldEvent> eventsToRemove, Google.Apis.Calendar.v3.CalendarService service)
        {
            foreach (VcbFieldEvent e in eventsToRemove)
            {
                var calendarId = "primary"; //Always primary.

                try
                {
                    service.Events.Delete(calendarId, e.googleEventId).Execute();
                    Console.WriteLine($"Deleted eventId {e.googleEventId}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                }

                Thread.Sleep(250);  // to comply with rate limits
            }
        }

        static void PostNewEventsToGoogleCalendar(List<VcbFieldEvent> newEvents, Google.Apis.Calendar.v3.CalendarService service)
        {
            foreach (VcbFieldEvent e in newEvents) {
                Event googleCalendarEvent = new();

                googleCalendarEvent.Start = new EventDateTime()
                { DateTimeDateTimeOffset = e.startTime };

                googleCalendarEvent.End = new EventDateTime()
                { DateTimeDateTimeOffset = e.endTime };

                googleCalendarEvent.Summary = e.eventType.ToString() + " : " + e.homeTeam;
                googleCalendarEvent.Description = (e.eventType == EventType.Practice ? string.Empty : "vs. ") + e.visitingTeamOrDescription;

                var calendarId = "primary"; //Always primary.

                Event result = new();

                try {
                    result = service.Events.Insert(googleCalendarEvent, calendarId).Execute();
                    e.googleEventId = result.Id;
                } catch(Exception ex) {
                    Console.WriteLine(ex.ToString());
                }

                Thread.Sleep(250);  // to comply with rate limits
            }
        }

        static void DeleteAllCalendarEvents(Google.Apis.Calendar.v3.CalendarService service)
        {
            // Define parameters of request.
            EventsResource.ListRequest request = service.Events.List("primary");
            request.TimeMinDateTimeOffset = DateTime.Now.AddYears(-2);
            request.ShowDeleted = true;
            request.SingleEvents = true;
            request.MaxResults = 300;
            request.OrderBy = EventsResource.ListRequest.OrderByEnum.StartTime;

            // List events.
            Events events = request.Execute();
            if (events.Items != null && events.Items.Count > 0)
            {
                Console.WriteLine($"Removing {events.Items.Count} event(s) from this calendar.");
                foreach (var eventItem in events.Items)
                {
                    try
                    {
                        Console.WriteLine($"Deleted eventId {eventItem.Id}");
                        service.Events.Delete("primary", eventItem.Id).Execute();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.ToString());
                    }

                    Thread.Sleep(250);  // to comply with rate limits
                }
            }
            else
            {
                Console.WriteLine("Found no events on this account calendar.");
            }
        }

        static List<VcbFieldEvent> FetchEvents(string sessionId, int locationId)
        {

            string eventPattern = @"<tr.+>\s" +
            @"<td (colspan=""2"" )?class=""l-Schedule__col-game-event"">\s(<a .+>)?(?<EventName>[^<]+)(</a>)?\s</td>\s" +
            @"(<td class=""l-Schedule__col-result"">\s(.+\s){3}</td>\s)?<td class=""l-Schedule__col-date"">\s(?<Date>.+)\s</td>\s" +
            @"<td class=""l-Schedule__col-time"">\s(?<Time>(<span class=""red"">CANCELED</span>)|(\d+:\d+ (AM|PM) - \d+:\d+ (AM|PM))|(\d+:\d+ (AM|PM)))\s((.+)\s)?((.+)\s)?</td>\s";

            Regex eventRegex = new(eventPattern, RegexOptions.ExplicitCapture | RegexOptions.NonBacktracking | RegexOptions.Compiled);

            List<VcbFieldEvent> events = new List<VcbFieldEvent>();

            using HttpClient client = new();
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Add("Cookie", $"_ts_session={sessionId}");

            int currentPage = 1;

            string responseHtml = GetVcbFieldEvents(client, locationId, currentPage);

            int contentStartIndex = responseHtml.IndexOf("<div id=\"content\">");

            if (contentStartIndex == -1)
            {
                throw new Exception("Error - did not find the expected event table start tag - check the returned HTML content and update the script");
            }

            Match m = PaginationRegex().Match(responseHtml, contentStartIndex);

            int expectedPageCount = m.Success ? Int32.Parse(m.Groups[1].Value) : 1;

            while (currentPage <= expectedPageCount)
            {
                int eventCount = 0;
                Match eventMatches = eventRegex.Match(responseHtml, contentStartIndex);

                while (eventMatches.Success)
                {
                    DateTime startTime, endTime;

                    Match timeMatch = TimeRegex().Match(eventMatches.Groups["Time"].Value);

                    if (timeMatch.Success)
                    {
                        startTime = DateTime.Parse(eventMatches.Groups["Date"].Value) + DateTime.Parse(timeMatch.Groups[1].Value).TimeOfDay;

                        if (!string.IsNullOrEmpty(timeMatch.Groups[4].Value))
                        {
                            endTime = DateTime.Parse(eventMatches.Groups["Date"].Value) + DateTime.Parse(timeMatch.Groups[4].Value).TimeOfDay;
                        }
                        else
                        {
                            endTime = startTime.AddHours(3);
                        }

                        string gameOrEventString = eventMatches.Groups["EventName"].Value.Trim();

                        // Event: VCB 13U A Giants: Practice,  // Events will have a colon that separates
                        // Game (league controlled): VCB 13U A Red Sox at VCB 13U A Diamondbacks  // Games will have a <visiting team> "at" <VCB team>
                        // Game (team-controlled): VCB Expos 15U AA - Blue vs.Vic - Layritz(Team Controlled)

                        Match teamEvent = TeamEventRegex().Match(gameOrEventString);

                        if (teamEvent.Success)
                        {
                            events.Add(new VcbFieldEvent(EventType.Practice, teamEvent.Groups["TeamName"].Value, teamEvent.Groups["EventTitle"].Value, startTime, endTime, string.Empty));
                        }
                        else
                        {
                            Match leagueControlledGame = LeagueControlledGameRegex().Match(gameOrEventString);

                            if (leagueControlledGame.Success)
                            {
                                events.Add(new VcbFieldEvent(EventType.Game, leagueControlledGame.Groups["HomeTeamName"].Value, leagueControlledGame.Groups["VisitorTeamName"].Value, startTime, endTime, string.Empty));
                            }
                            else
                            {
                                Match teamControlledGame = TeamControlledGameRegex().Match(gameOrEventString);
                                if (teamControlledGame.Success)
                                {
                                    events.Add(new VcbFieldEvent(EventType.Game, teamControlledGame.Groups["VcbTeamName"].Value, teamControlledGame.Groups["OpponentName"].Value, startTime, endTime, string.Empty));
                                }
                                else
                                {
                                    Console.WriteLine($"Warning: unable to parse the event string for the event {gameOrEventString}.  Check the returned HTML");
                                    events.Add(new VcbFieldEvent(EventType.Practice, gameOrEventString, "", startTime, endTime, string.Empty));
                                }
                            }
                        }

                        eventCount++;
                    }
                    else
                    {
                        Console.WriteLine($"Warning: Event {eventMatches.Groups["EventName"].Value.Trim()} on {eventMatches.Groups["Date"].Value} has an unparsable time value ({eventMatches.Groups["Time"].Value}).  Skipping...");
                    }

                    eventMatches = eventMatches.NextMatch();
                }

                currentPage++;

                responseHtml = GetVcbFieldEvents(client, locationId, currentPage);
                contentStartIndex = responseHtml.IndexOf("<div id=\"content\">");

                if (contentStartIndex == -1)
                {
                    throw new Exception("Error - did not find the expected event table start tag - check the returned HTML content and update the script");
                }
            }

            return events;
        }

        static List<VcbFieldEvent> LoadEvents(string location)
        {
            if (!File.Exists($"{location}.csv"))
            {
                return [];
            }

            using (StreamReader reader = new StreamReader($"{location}.csv"))
            {
                using (var csv = new CsvReader(reader, CultureInfo.InvariantCulture))
                {
                    csv.Context.RegisterClassMap<FieldEventMap>();
                    var events = csv.GetRecords<VcbFieldEvent>();
                    return events.ToList();
                }
            }
        }

        static void SaveEvents(List<VcbFieldEvent> events, string location)
        {
            using (StreamWriter writer = new($"{location}.csv", false))
            {
                using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
                {
                    csv.WriteHeader<VcbFieldEvent>();
                    csv.NextRecord();
                    csv.WriteRecords(events);
                }
            }
        }

        static string GetVcbFieldEvents(HttpClient client, int locationId, int page)
        {
            return client.GetStringAsync($"https://go.teamsnap.com/774786/league_schedule?location_id={locationId}&page={page}").Result;
        }
    }
}
