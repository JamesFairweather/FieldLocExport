using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Calendar.v3;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VcbFieldExport;
using CsvHelper;
using System.Globalization;
using CsvHelper.Configuration;
using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Google.Apis.Auth.OAuth2.Responses;

namespace VcbFieldExport
{
    // TODO
    // * find out whether we can reduce the frequency of how often we have to redo the full OAuth 2 flow
    //   as of April 7, 2025, tokens are valid for 2 weeks.  I'd prefer to have a token that has no expiration time
    internal partial class Program
    {
        [GeneratedRegex(@"(?<EventType>.+)\s:\s(?<homeTeam>.+)")]
        public static partial Regex SummaryRegex();

        [GeneratedRegex(@"vs\.\s(?<visitingTeam>.+)")]
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
            "Trafalgar Park",
        };

        List<VcbFieldEvent> mNewEventList;

        public GoogleEvents(List<VcbFieldEvent> games, List<VcbFieldEvent> practices)
        {
            mNewEventList = new List<VcbFieldEvent>(games.Count + practices.Count);
            mNewEventList.AddRange(games);
            mNewEventList.AddRange(practices);
        }


        CalendarService GetGoogleCalendarService(string locationName)
        {
            UserCredential credential;

            // If the scopes are modified, you will need a new credentials.json file and re-generate the tokens.

            string[] Scopes = { CalendarService.Scope.Calendar };
            string ApplicationName = "Google Calendar Access";

            string credentials = locationName == "Nanaimo Park N diamond" ? "nanaimoNorth_credentials.json" : "google_credentials.json";

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

            googleCalendarEvent.Summary = vcbFieldEvent.eventType.ToString() + " : " + vcbFieldEvent.homeTeam;
            googleCalendarEvent.Description = (vcbFieldEvent.eventType == VcbFieldEvent.Type.Practice ? vcbFieldEvent.description : "vs. " + vcbFieldEvent.visitingTeam);

            Event result = new ();

            try {
                calendarService.Events.Insert(googleCalendarEvent, "primary").Execute();
                Console.WriteLine($"Added event {vcbFieldEvent.eventType} at {vcbFieldEvent.location} on {vcbFieldEvent.startTime.ToLocalTime()} for team {vcbFieldEvent.homeTeam}");
            }
            catch (Exception ex) {
                Console.WriteLine(ex.ToString());
            }
        }

        List<VcbFieldEvent> fetchEvents(string location, out CalendarService calendarService)
        {
            Events events;

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
                Console.WriteLine($"Found {events.Items.Count} event(s) on this calendar.");
                foreach (Event eventItem in events.Items)
                {
                    VcbFieldEvent vcbFieldEvent = new VcbFieldEvent();
                    vcbFieldEvent.location = location;
                    Match match = Program.SummaryRegex().Match(eventItem.Summary);
                    if (match.Success)
                    {
                        vcbFieldEvent.eventType = match.Groups["EventType"].Value == "Practice" ? VcbFieldEvent.Type.Practice : VcbFieldEvent.Type.Game;
                        vcbFieldEvent.homeTeam = match.Groups["homeTeam"].Value;
                    }
                    else
                    {
                        Console.WriteLine("Could not parse the summary for a Google event");
                    }

                    vcbFieldEvent.startTime = (eventItem.Start.DateTimeDateTimeOffset ?? DateTime.MinValue).UtcDateTime;
                    vcbFieldEvent.endTime = (eventItem.End.DateTimeDateTimeOffset ?? DateTime.MinValue).UtcDateTime;

                    if (vcbFieldEvent.eventType == VcbFieldEvent.Type.Practice) {
                        vcbFieldEvent.description = eventItem.Description;
                    }
                    else {
                        match = Program.DescriptionRegex().Match(eventItem.Description);
                        if (match.Success)
                        {
                            vcbFieldEvent.visitingTeam = match.Groups["visitingTeam"].Value ?? string.Empty;
                        }
                        else
                        {
                            Console.WriteLine("Could not parse the description for a Google event");
                        }
                    }

                    // Record the event Id in case we need to delete it
                    vcbFieldEvent.googleEventId = eventItem.Id;

                    results.Add(vcbFieldEvent);
                }
            }
            else
            {
                Console.WriteLine("Found no events on this account calendar.");
            }

            return results;
        }

        public void Reconcile()
        {
            foreach (string locationId in LOCATION_NAMES)
            {
                Console.WriteLine($"Processing events for location {locationId} ...");

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
                            Console.WriteLine($"Deleted event {e.eventType} at {e.location} on {e.startTime.ToLocalTime()} for team {e.homeTeam}");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(ex.ToString());
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
