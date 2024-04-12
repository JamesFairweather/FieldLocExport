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
        LeagueControlledGame,
        TeamControlledGame,
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
        }

        public VcbFieldEvent(EventType _eventType, string _homeTeam, string _visitingTeam, DateTime start, DateTime end)
        {
            eventType = _eventType;
            homeTeam = _homeTeam;
            visitingTeamOrDescription = _visitingTeam;
            startTime = start;
            endTime = end;
        }

        public EventType eventType { get; set; }
        public string homeTeam { get; set; }
        public string visitingTeamOrDescription { get; set; }
        public DateTime startTime { get; set; }
        public DateTime endTime { get; set; }

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
            Map(m => m.endTime).Index(4).Name("endTime"); //.TypeConverter<CalendarExceptionEnumConverter<CalendarExceptionEntityType>>();
        }
    }

    class IdEqualityComparer : IEqualityComparer<VcbFieldEvent>
    {
        public bool Equals(VcbFieldEvent? a, VcbFieldEvent? b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a is null || b is null) return false;
            return a.eventType == b.eventType;
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
            { 69829182, "Killarney Park W diamond" },
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

            foreach (int locationId in locationIds.Keys)
            {
                List<VcbFieldEvent> currentEvents = FetchEvents(sessionId, locationId);
                List<VcbFieldEvent> savedEvents = LoadEvents(locationIds[locationId]);

                // find events to remove and delete them from the Google calendar
                List<VcbFieldEvent> eventsToRemove = new List<VcbFieldEvent>();
                savedEvents.ForEach(e =>
                {
                    if (!currentEvents.Contains(e))
                    {
                        eventsToRemove.Add(e);
                    }
                });

                // find events to add and insert them into the Google calendar
                List<VcbFieldEvent> eventsToAdd = new List<VcbFieldEvent>();
                currentEvents.ForEach(e =>
                {
                    if (!savedEvents.Contains(e))
                    {
                        eventsToAdd.Add(e);
                    }
                });

                PostNewEventsToGoogleCalendar(eventsToAdd, locationIds[locationId]);

                SaveEvents(currentEvents, locationIds[locationId]);
            }

            return returnValue;
        }

        // If modifying these scopes, delete your previously saved credentials
        // at ~/.credentials/calendar-dotnet-quickstart.json
        static string[] Scopes = { CalendarService.Scope.Calendar };
        static string ApplicationName = "Google Calendar API .NET Quickstart";

        static void PostNewEventsToGoogleCalendar(List<VcbFieldEvent> newEvents, string locationName)
        {
            UserCredential credential;

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

            foreach (VcbFieldEvent e in newEvents) {
                Event googleCalendarEvent = new();

                googleCalendarEvent.Start = new EventDateTime()
                { DateTimeDateTimeOffset = e.startTime };

                googleCalendarEvent.End = new EventDateTime()
                { DateTimeDateTimeOffset = e.endTime };

                googleCalendarEvent.Summary = e.homeTeam + " : " + e.eventType.ToString();
                googleCalendarEvent.Description = (e.eventType == EventType.Practice ? string.Empty : "vs. ") + e.visitingTeamOrDescription;

                var calendarId = "primary"; //Always primary.

                Event result = new();

                try {
                    result = service.Events.Insert(googleCalendarEvent, calendarId).Execute();
                } catch(Exception ex) {
                    Console.WriteLine(ex.ToString());
                }

                Thread.Sleep(250);  // to comply with rate limits
            }
            //// Define parameters of request.
            //EventsResource.ListRequest request = service.Events.List("primary");
            //request.TimeMin = DateTime.Now;
            //request.ShowDeleted = false;
            //request.SingleEvents = true;
            //request.MaxResults = 10;
            //request.OrderBy = EventsResource.ListRequest.OrderByEnum.StartTime;

            //// List events.
            //Events events = request.Execute();
            //Console.WriteLine("Upcoming events:");
            //if (events.Items != null && events.Items.Count > 0)
            //{
            //    foreach (var eventItem in events.Items)
            //    {
            //        string when = eventItem.Start.DateTime.ToString();
            //        if (String.IsNullOrEmpty(when))
            //        {
            //            when = eventItem.Start.Date;
            //        }
            //        Console.WriteLine("{0} ({1})", eventItem.Summary, when);
            //    }
            //}
            //else
            //{
            //    Console.WriteLine("No upcoming events found.");
            //}
            //Console.Read();
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

            Console.WriteLine($"Processing events for location {locationIds[locationId]}");
            int currentPage = 1;

            string responseHtml = GetVcbFieldEvents(client, locationId, currentPage);

            int contentStartIndex = responseHtml.IndexOf("<div id=\"content\">");

            if (contentStartIndex == -1)
            {
                throw new Exception("Error - did not find the expected event table start tag - check the returned HTML content and update the script");
            }

            Match m = PaginationRegex().Match(responseHtml, contentStartIndex);

            int expectedPageCount = m.Success ? Int32.Parse(m.Groups[1].Value) : 1;

            Console.WriteLine($"Expecting {expectedPageCount} pages of events for this location.");

            while (currentPage <= expectedPageCount)
            {
                Console.WriteLine($"Processing page {currentPage} ...");

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
                            events.Add(new VcbFieldEvent(EventType.Practice, teamEvent.Groups["TeamName"].Value, teamEvent.Groups["EventTitle"].Value, startTime, endTime));
                        }
                        else
                        {
                            Match leagueControlledGame = LeagueControlledGameRegex().Match(gameOrEventString);

                            if (leagueControlledGame.Success)
                            {
                                events.Add(new VcbFieldEvent(EventType.LeagueControlledGame, leagueControlledGame.Groups["HomeTeamName"].Value, leagueControlledGame.Groups["VisitorTeamName"].Value, startTime, endTime));
                            }
                            else
                            {
                                Match teamControlledGame = TeamControlledGameRegex().Match(gameOrEventString);
                                if (teamControlledGame.Success)
                                {
                                    events.Add(new VcbFieldEvent(EventType.TeamControlledGame, teamControlledGame.Groups["VcbTeamName"].Value, teamControlledGame.Groups["OpponentName"].Value, startTime, endTime));
                                }
                                else
                                {
                                    Console.WriteLine($"Warning: unable to parse the event string for the event {gameOrEventString}.  Check the returned HTML");
                                    events.Add(new VcbFieldEvent(EventType.Practice, gameOrEventString, "", startTime, endTime));
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

                Console.WriteLine($"Parsed {eventCount} events from this page.");

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
            Console.WriteLine($"Saving events to file {location}.csv");
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
