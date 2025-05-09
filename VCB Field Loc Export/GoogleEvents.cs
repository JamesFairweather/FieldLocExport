using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Calendar.v3;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using System.Text.RegularExpressions;
using Google.Apis.Auth.OAuth2.Responses;

namespace VcbFieldExport
{
    internal partial class Program
    {
        [GeneratedRegex(@"(?<EventType>.+):\s(?<team>.+)")]
        public static partial Regex SummaryRegex();

        [GeneratedRegex(@"(?<visitingTeam>.+)\s@\s(?<homeTeam>.+)")]
        public static partial Regex DescriptionRegex();
    }

    class GoogleEvents
    {
        List<string> LOCATION_NAMES = new List<string> {
            "Chaldecott Park N diamond",
            "Chaldecott Park S diamond",
            "Hillcrest Park NE diamond",
            "Hillcrest Park SW diamond",
            "Nanaimo Park N diamond",
            "Nanaimo Park SE diamond",
            "Nanaimo Park batting cage",
            "Trafalgar Park",
        };

        List<VcbFieldEvent> mNewEventList;
        StreamWriter mLogger;

        public GoogleEvents(List<VcbFieldEvent> games, List<VcbFieldEvent> practices, StreamWriter logger)
        {
            mNewEventList = new List<VcbFieldEvent>(games.Count + practices.Count);
            mNewEventList.AddRange(games);
            mNewEventList.AddRange(practices);
            this.mLogger = logger;
        }


        CalendarService GetGoogleCalendarService(string locationName)
        {
            UserCredential credential;

            string[] Scopes = { CalendarService.Scope.Calendar };
            string ApplicationName = "Google Calendar Access";

            // Access to specific field calendars is provided through the fields@vcbmountiesbaseball.com
            // account.  Because all these accounts are part of the same Google organization, we don't
            // need to re-authenticate
            string credentials = "field_credentials.json";

            using (var stream =
              new FileStream(credentials, FileMode.Open, FileAccess.Read))
            {
                // The file token.json stores the user's access and refresh tokens, and is created
                // automatically when the authorization flow completes for the first time.
                credential = GoogleWebAuthorizationBroker.AuthorizeAsync(
                  GoogleClientSecrets.FromStream(stream).Secrets,
                  Scopes,
                  "user",
                  CancellationToken.None,
                  new FileDataStore($"{locationName}.json", true)).Result;
            }

            // Create Google Calendar API service.
            var service = new CalendarService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = ApplicationName,
            });

            return service;
        }

        void addEventToGoogleCalendar(CalendarService calendarService, VcbFieldEvent vcbFieldEvent) {
            Event googleCalendarEvent = new();

            googleCalendarEvent.Start = new EventDateTime()
            { DateTimeDateTimeOffset = vcbFieldEvent.startTime.ToLocalTime() };

            googleCalendarEvent.End = new EventDateTime()
            { DateTimeDateTimeOffset = vcbFieldEvent.endTime.ToLocalTime() };

            switch (vcbFieldEvent.eventType)
            {
                case VcbFieldEvent.Type.Practice:
                    googleCalendarEvent.Summary = "Practice: " + vcbFieldEvent.homeTeam;
                    googleCalendarEvent.Description = vcbFieldEvent.description;
                    break;

                case VcbFieldEvent.Type.Game:
                    googleCalendarEvent.Summary = "Game: " + vcbFieldEvent.division;
                    googleCalendarEvent.Description = $"{vcbFieldEvent.visitingTeam} @ {vcbFieldEvent.homeTeam}";
                    break;

                case VcbFieldEvent.Type.PlayoffGame:
                    googleCalendarEvent.Summary = "Playoffs: " + vcbFieldEvent.division;
                    googleCalendarEvent.Description = $"{vcbFieldEvent.visitingTeam} @ {vcbFieldEvent.homeTeam}";
                    break;
           }

            Event result = new();

            try {
                calendarService.Events.Insert(googleCalendarEvent, "primary").Execute();
                if (vcbFieldEvent.eventType == VcbFieldEvent.Type.Practice) {
                    mLogger.WriteLine($"Added Practice on {vcbFieldEvent.startTime.ToLocalTime().ToString("g")} for team {vcbFieldEvent.homeTeam}");
                }
                else {
                    mLogger.WriteLine($"Added Game on {vcbFieldEvent.startTime.ToLocalTime().ToString("g")}: {vcbFieldEvent.visitingTeam} @ {vcbFieldEvent.homeTeam}");
                }
            }
            catch (Exception ex) {
                mLogger.WriteLine(ex.ToString());
            }
        }

        List<VcbFieldEvent> fetchEvents(string location, out CalendarService calendarService)
        {
            Google.Apis.Calendar.v3.Data.Events events;

            try {
                calendarService = GetGoogleCalendarService(location);

                // Define parameters of request.
                EventsResource.ListRequest request = calendarService.Events.List("primary");
                request.TimeMinDateTimeOffset = DateTime.Now.AddMonths(-4);
                request.ShowDeleted = false;
                request.SingleEvents = true;
                request.MaxResults = 300;
                request.OrderBy = EventsResource.ListRequest.OrderByEnum.StartTime;

                // List events.
                events = request.Execute();
            }
            catch (TokenResponseException)
            {
                // Handle an invalid_grant exception by retrying the request.
                // This will force the OAuth2 flow to start again.
                calendarService = GetGoogleCalendarService(location);

                EventsResource.ListRequest request = calendarService.Events.List("primary");
                request.TimeMinDateTimeOffset = DateTime.Now.AddMonths(-4);
                request.ShowDeleted = false;
                request.SingleEvents = true;
                request.MaxResults = 300;
                request.OrderBy = EventsResource.ListRequest.OrderByEnum.StartTime;
                events = request.Execute();
            }
            List<VcbFieldEvent> results = new();

            if (events.Items != null && events.Items.Count > 0)
            {
                results.Capacity = events.Items.Count;
                mLogger.WriteLine($"Found {events.Items.Count} existing events on this calendar.");
                foreach (Event eventItem in events.Items)
                {
                    VcbFieldEvent vcbFieldEvent = new VcbFieldEvent();
                    vcbFieldEvent.location = location;
                    Match match = Program.SummaryRegex().Match(eventItem.Summary);
                    if (match.Success)
                    {
                        switch (match.Groups["EventType"].Value)
                        {
                            case "Practice":
                                vcbFieldEvent.eventType = VcbFieldEvent.Type.Practice;
                                break;
                            case "Game":
                                vcbFieldEvent.eventType = VcbFieldEvent.Type.Game;
                                break;
                            case "Playoffs":
                                vcbFieldEvent.eventType = VcbFieldEvent.Type.PlayoffGame;
                                break;
                        }
                        if (vcbFieldEvent.eventType == VcbFieldEvent.Type.Practice) {
                            vcbFieldEvent.homeTeam = match.Groups["team"].Value;
                        }
                        else {
                            // Game: get the teams from the event description property
                            match = Program.DescriptionRegex().Match(eventItem.Description);
                            if (match.Success) {
                                vcbFieldEvent.homeTeam = match.Groups["homeTeam"].Value ?? string.Empty;
                                vcbFieldEvent.visitingTeam = match.Groups["visitingTeam"].Value ?? string.Empty;
                            }
                            else {
                                mLogger.WriteLine("Could not parse the description for a Google event");
                            }
                        }
                    }
                    else if (eventItem.Summary == "No permit")
                    {
                        continue;   // ignore this event
                    }
                    else
                    {
                        mLogger.WriteLine("Could not parse the summary for a Google event");
                    }

                    vcbFieldEvent.startTime = (eventItem.Start.DateTimeDateTimeOffset ?? DateTime.MinValue).UtcDateTime;
                    vcbFieldEvent.endTime = (eventItem.End.DateTimeDateTimeOffset ?? DateTime.MinValue).UtcDateTime;

                    // Record the event Id in case we need to delete it
                    vcbFieldEvent.googleEventId = eventItem.Id;

                    results.Add(vcbFieldEvent);
                }
            }
            else
            {
                mLogger.WriteLine("Found no events on this account calendar.");
            }

            return results;
        }

        public void Reconcile()
        {
            foreach (string locationId in LOCATION_NAMES)
            {
                mLogger.WriteLine();
                mLogger.WriteLine($"Syncing events for location {locationId} to the Google field calendar...");

                CalendarService calendarService;

                // Clear a calendar of all existing events
                // GetGoogleCalendarService(locationId).Calendars.Clear("primary").Execute();

                List<VcbFieldEvent> currentEvents = fetchEvents(locationId, out calendarService);

                // delete existing events that have been removed in TeamSnap from the Google calendar
                currentEvents.ForEach(e =>
                {
                    if (e.location == locationId && !mNewEventList.Contains(e))
                    {
                        try
                        {
                            calendarService.Events.Delete("primary", e.googleEventId).Execute();
                            mLogger.WriteLine($"Deleted event {e.eventType} on {e.startTime.ToLocalTime().ToString("g")} for team {e.homeTeam}");
                        }
                        catch (Exception ex)
                        {
                            mLogger.WriteLine(ex.ToString());
                        }
                    }
                });

                // insert new events into the Google calendar
                List<VcbFieldEvent> eventsToAdd = new List<VcbFieldEvent>();
                mNewEventList.ForEach(e =>
                {
                    if (e.location == locationId && !currentEvents.Contains(e)) {
                        addEventToGoogleCalendar(calendarService, e);
                    }
                });
            }
        }
    }
}
