using CsvHelper;
using CsvHelper.Configuration;
using CsvHelper.TypeConversion;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Logging;
using Google.Apis.Services;
using Google.Apis.Util;
using Google.Apis.Util.Store;
using System;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using static Google.Apis.Requests.BatchRequest;
using static System.Formats.Asn1.AsnWriter;
using static System.Net.Mime.MediaTypeNames;

// TODO:
// * improve handling of failures from the Google service
//   e.g. after a few days, the field account tokens expire and we get exceptions like this:
// Google.Apis.Auth.OAuth2.Responses.TokenResponseException: Error: "invalid_grant", Description: "Token has been expired or revoked.", Uri: ""
//    at Google.Apis.Auth.OAuth2.Responses.TokenResponse.FromHttpResponseAsync(HttpResponseMessage response, IClock clock, ILogger logger)
// 
// * there's probably some way to refresh the local tokens automatically.
// 
// * Before syncing the event list from TeamSnap, ensure service has its schedule mode set to:
// * Hide past games/events checked (true)
// * Show: Games and Events

// GET https://go.teamsnap.com/774786/league_schedule/preferences?height=220&location_id=69829182&modal=true&mode=list&width=300&random=1713540533176 HTTP/1.1
// > returns some HTML to display the dialog box and an authenticity token.

// POST https://go.teamsnap.com/774786/league_schedule/save_preferences HTTP/1.1

// request body:
// Events only:
// authenticity_token=XP2%2FHCKgRgENhecb4EMUqs68JgZ3pbDMvjeTJrES%2B5g%3D&mode=list&location_id=69829182&roster_entry%5Bhide_old_events%5D=0&roster_entry%5Bhide_old_events%5D=1&roster_entry%5Bgame_filter%5D=3&x=57&y=20
// Games only:
// authenticity_token=XP2%2FHCKgRgENhecb4EMUqs68JgZ3pbDMvjeTJrES%2B5g%3D&mode=list&location_id=69829182&roster_entry%5Bhide_old_events%5D=0&roster_entry%5Bhide_old_events%5D=1&roster_entry%5Bgame_filter%5D=2&x=39&y=17
// Games & Events:
// authenticity_token=XP2%2FHCKgRgENhecb4EMUqs68JgZ3pbDMvjeTJrES%2B5g%3D&mode=list&location_id=69829182&roster_entry%5Bhide_old_events%5D=0&roster_entry%5Bhide_old_events%5D=1&roster_entry%5Bgame_filter%5D=1&x=42&y=9

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

        // OrgIds: 
        // Minor: 774786
        // 26U: 817084

        class TeamSnapFieldInfo
        {
            public TeamSnapFieldInfo(int o, int f)
            {
                OrgId = o;
                FieldId = f;
            }

            public int OrgId;
            public int FieldId;
        }

        // map a list of fields to a list of TeamSnap OrgId& FieldId pairs.

        static readonly Dictionary<string, TeamSnapFieldInfo[]> locationIds = new Dictionary<string, TeamSnapFieldInfo[]> {
            { "Chaldecott Park N diamond", new TeamSnapFieldInfo[] { new (774786, 69829171) } },
            { "Chaldecott Park S diamond", new TeamSnapFieldInfo[] { new(774786, 69829169) } },
            { "Hillcrest Park NE diamond", new TeamSnapFieldInfo[] { new(774786, 69829163) } },
            { "Hillcrest Park SW diamond", new TeamSnapFieldInfo[] { new(774786, 69829157) } },
            { "Killarney Park W diamond", new TeamSnapFieldInfo[] { new(774786, 69829182), new(817084, 70511218) } },
            { "Nanaimo Park N diamond", new TeamSnapFieldInfo[] { new(774786, 69829177) } },
            { "Nanaimo Park SE diamond",  new TeamSnapFieldInfo[] { new(774786, 69829180), new(817084, 70511212) } },
            { "Trafalgar Park", new TeamSnapFieldInfo[] { new(774786, 69829186) } },
        };

        static int Main(string[] args)
        {
            int returnValue = 0;

            string sessionId = File.ReadAllText("_ts_session_cookie");

            // Uncomment these lines to remove all the events for a specific calendar
            // string removeAllEventsFromFieldCalendar = "Chaldecott Park N diamond";
            // DeleteAllCalendarEvents(GetGoogleCalendarService(removeAllEventsFromFieldCalendar));
            // return 0;

            foreach (string locationId in locationIds.Keys)
            {
                Console.WriteLine($"Processing events for location {locationId} ...");

                List<VcbFieldEvent> savedEvents = LoadEvents(locationId);

                var googleCalendarService = GetGoogleCalendarService(locationId);

                List<VcbFieldEvent> currentEvents = [];

                foreach (TeamSnapFieldInfo fieldInfo in locationIds[locationId])
                {
                    currentEvents = FetchEvents(currentEvents, sessionId, fieldInfo);
                }

                if (currentEvents.Count == 0)
                {
                    Console.WriteLine("Found no events for this field in the returned event list.  Skipping.");
                    continue;
                }

                Console.WriteLine($"Found {currentEvents.Count} events, reconciling with last snapshot...");

                // find events to remove and delete them from the Google calendar
                List<VcbFieldEvent> eventsToRemove = new List<VcbFieldEvent>();
                savedEvents.ForEach(e =>
                {
                    if (!currentEvents.Contains(e))
                    {
                        if (e.googleEventId != string.Empty)
                        {
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
                    var existingEvent = savedEvents.Find(savedEvent => savedEvent.Equals(e));
                    if (existingEvent != null)
                    {
                        // copy the event Id from the old list to the new one
                        e.googleEventId = existingEvent.googleEventId;
                    }
                    else
                    {
                        eventsToAdd.Add(e);
                    }
                });

                if (eventsToAdd.Count > 0)
                {
                    Console.WriteLine($"Adding {eventsToAdd.Count} event(s) to the calendar...");
                    PostNewEventsToGoogleCalendar(eventsToAdd, googleCalendarService);
                }
                else
                {
                    Console.WriteLine($"No events to add found");
                }

                if (currentEvents.Count != 0)
                {
                    SaveEvents(currentEvents, locationId);
                }
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
            foreach (VcbFieldEvent e in newEvents)
            {
                Event googleCalendarEvent = new();

                googleCalendarEvent.Start = new EventDateTime()
                { DateTimeDateTimeOffset = e.startTime };

                googleCalendarEvent.End = new EventDateTime()
                { DateTimeDateTimeOffset = e.endTime };

                googleCalendarEvent.Summary = e.eventType.ToString() + " : " + e.homeTeam;
                googleCalendarEvent.Description = (e.eventType == EventType.Practice ? string.Empty : "vs. ") + e.visitingTeamOrDescription;

                var calendarId = "primary"; //Always primary.

                Event result = new();

                try
                {
                    result = service.Events.Insert(googleCalendarEvent, calendarId).Execute();
                    e.googleEventId = result.Id;
                }
                catch (Exception ex)
                {
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

        static List<VcbFieldEvent> FetchEvents(List<VcbFieldEvent> events, string sessionId, TeamSnapFieldInfo fieldInfo)
        {
            string eventPattern = @"<tr.+>\s" +
            @"<td (colspan=""2"" )?class=""l-Schedule__col-game-event"">\s(<a .+>)?(?<EventName>[^<]+)(</a>)?\s</td>\s" +
            @"(<td class=""l-Schedule__col-result"">\s(.+\s){3}</td>\s)?<td class=""l-Schedule__col-date"">\s(?<Date>.+)\s</td>\s" +
            @"<td class=""l-Schedule__col-time"">\s(?<Time>(<span class=""red"">CANCELED</span>)|(\d+:\d+ (AM|PM) - \d+:\d+ (AM|PM))|(\d+:\d+ (AM|PM)))\s((.+)\s)?((.+)\s)?</td>\s";

            Regex eventRegex = new(eventPattern, RegexOptions.ExplicitCapture | RegexOptions.NonBacktracking | RegexOptions.Compiled);

            using HttpClient client = new();
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Add("Cookie", $"_ts_session={sessionId}");

            int currentPage = 1;

            string responseHtml = GetVcbFieldEvents(client, fieldInfo, currentPage);

            int contentStartIndex = responseHtml.IndexOf("<div id=\"content\">");

            if (contentStartIndex == -1)
            {
                throw new Exception("Error - did not find the expected event table start tag - check the returned HTML content and update the script");
            }

            int eventCount = 0;

            while (true)
            {
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
                    }
                    // If the time regex fails to match the time field, the event has been cancelled so just ignore it.

                    eventCount++;

                    eventMatches = eventMatches.NextMatch();
                }

                // if we get fewer than 30 events, we've reached the last page so terminate the while (true) loop above.
                if (eventCount < 30)
                    break;

                currentPage++;
                eventCount = 0;

                responseHtml = GetVcbFieldEvents(client, fieldInfo, currentPage);
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

        static string GetVcbFieldEvents(HttpClient client, TeamSnapFieldInfo fieldInfo, int page)
        {
            return client.GetStringAsync($"https://go.teamsnap.com/{fieldInfo.OrgId}/league_schedule?mode=list&location_id={fieldInfo.FieldId}&page={page}").Result;
        }
    }
}
